# Modelos locais de decisão tipada: OpenJev e Laya

O Cortex tem dois provedores locais de decisão tipada. Eles respondem campos `enum` e `boolean` com probabilidades, não texto livre, e têm o mesmo contrato de saída de `SystemOneFlowCompletion` (`jev`):

- **`openjev`**: o método do [OpenJev](https://openjev.com) contra um `llama-server` (llama.cpp) local, para qualquer GGUF de instrução. Código em `src/Cortex.Inference/OpenJev/`.
- **`laya`**: o servidor `laya-serve` (`convaiinnovations/laya`) local, pelo `SystemOneClient` existente com `SystemOneProbabilityPolicy.LayaFourDecimals`.

Um nó pode trocar `jev`/`laya` por `openjev` mudando só provedor, modelo e endpoint. Perguntas `score` e texto livre continuam recusados. O Cortex não gerencia o ciclo de vida desses servidores em produção: o host aponta para um endpoint que já está rodando. Só os testes e `tools/start-openjev.ps1` sobem servidores.

Desenho: [2026-10-30-local-decision-models-design.md](superpowers/specs/2026-10-30-local-decision-models-design.md).

## Configurar `openjev`

Na avaliação, o endpoint é registrado pelo host em `EvaluationProviderFactory` (`EvaluationEndpoint(alias, "openjev", uri, http, credential)`):

- **Endpoint:** a URL **base** do `llama-server`, absoluta, HTTP(S), loopback, sem userinfo, query nem fragment, e terminada em `/`. Exemplo: `http://127.0.0.1:18080/`. O cliente chama `tokenize` e `v1/chat/completions` relativos a essa base. Qualquer outra forma gera `InvalidDataException`.
- **Modelo:** o `model` do perfil é o `--alias` com que o `llama-server` foi iniciado. O adaptador exige que o `model` da resposta seja igual ao pedido; um servidor com outro GGUF/alias gera `InvalidDataException` (`model_mismatch`), sem resultado.
- **Limiar:** o mesmo campo `threshold` de `jev`/`laya`. Um booleano vira duas opções, `A. Yes` e `B. No`, e o resultado é `true` quando P(Yes) ≥ limiar (o `noul` observado é P(Yes)). Os testes usam 0.5. O `tools/start-openjev.ps1` só sobe o servidor e não tem limiar: o limiar é do perfil ou de quem compõe o `OpenJevFlowCompletion`.
- **Esquema:** objeto fechado de campos obrigatórios, cada um `enum` (2 a 20 valores, rótulos `A`…`T`) ou `boolean`. Um `enum` ou `choiceQuestions` com mais de 20 opções já falha na validação do perfil (`profile.options_exceed_provider_limit` / `profile.choice_question_invalid`), antes de qualquer trial. Perfis de escolha (`choiceQuestions`) são aceitos como em `jev`/`laya`.
- **Contabilidade:** cada `tokenize` e cada chat passam pelo `EvaluationHttpHandler` e viram uma tentativa no ledger, com a reserva do endpoint. O `tokenize` não faz inferência: é observado com uso 0/0 e atribuído ao `model` enviado no pedido; o chat usa `usage` e `model` da resposta. Com termos de preço `openjev` de taxa zero, `AttemptPriceProjection` dá custo 0 e `TrialMeasurement` soma os tokens. O cache de rótulos não atravessa trials: cada trial cria seu próprio cliente e registra as tokenizações que justificam o seu `logit_bias`.

Fora da avaliação, a composição é direta:

```csharp
var completion = new OpenJevFlowCompletion(
    new OpenJevClient(http, new Uri("http://127.0.0.1:18080/")),
    booleanThreshold: 0.5);
// request.Provider = "openjev", request.Model = "<alias do llama-server>"
```

Por campo, a pergunta é a tarefa (`request.System`), uma linha em branco e a `description` do campo no esquema; sem descrição não vazia, vai o nome do campo. Com perfil de escolha, acrescenta-se uma linha em branco e a orientação do perfil. Não há cabeçalho `Field: <nome>`. É feita uma chamada sequencial com o prompt exato do OpenJev, `max_tokens:1`, `temperature:1`, `top_k:0`, `top_p:1`, `logprobs:true`, `top_logprobs:20`, `logit_bias` +100 nos rótulos, gramática `root ::= "A" | "B" | …`, `cache_prompt:false` e `chat_template_kwargs:{"enable_thinking":false}`. Antes de usar um rótulo pela primeira vez com um modelo, o cliente o tokeniza por `POST /tokenize` (`{"model","content","add_special":false}`), só os rótulos que a pergunta precisa (`A`…o n-ésimo); o cache é por instância de `OpenJevClient` e modelo, e uma pergunta com mais opções tokeniza só os rótulos novos. Um rótulo que não vira exatamente um token é erro explícito, antes do chat. A probabilidade é o softmax dos logprobs dos rótulos; rótulo ausente do top 20 é erro, nunca renormalização. A observação usa `ProbabilityPolicy = "OpenJevLabelSoftmax"`, e os logprobs brutos vão pelo callback `observeDecision`.

### Ordem das opções: `optionOrder`

Modelos pequenos têm viés forte de posição: preferem um rótulo pela posição, não pelo conteúdo. Uma sonda que girou as opções (`C:\temp\localmodels-probe\rotate.py`) mediu a probabilidade dada à resposta certa em cada rotação cíclica:

- Qwen3 0.6B, caso `email`: 0.86 / 0.02 / 1.00.
- MiniCPM5 2B, caso `boolean`: 0.06 / 0.98.

A correção padrão é tirar a média entre as ordens. No perfil de avaliação, o `openjev` aceita o parâmetro opcional `optionOrder`:

- **`"fixed"`** (padrão, também quando o parâmetro falta): uma chamada por pergunta, como descrito acima, com `ProbabilityPolicy = "OpenJevLabelSoftmax"`.
- **`"rotations"`**: para uma pergunta com n opções, n chamadas, uma por rotação cíclica da lista (deslocamento 0…n−1). Os rótulos ficam sempre `A`, `B`, … na ordem da lista girada. A probabilidade de cada opção original é a média aritmética das n probabilidades. A escolha é o argmax da média; no booleano, P(Yes) médio é comparado com o limiar. O `observeDecision` recebe uma única decisão sintetizada por campo, com as opções na ordem original, `Probability` = média, `Logprob` = ln(média) e tokens e tempo somados. O resultado observado soma o uso de todas as chamadas e usa `ProbabilityPolicy = "OpenJevLabelSoftmaxRotationAverage"`.

Qualquer outro valor (inclusive outra caixa, número ou `null`) é recusado na validação do perfil com `profile.parameter_invalid` em `parameters.optionOrder`. Os outros provedores continuam recusando o parâmetro (`profile.parameter_unsupported`).

**Custo:** n chamadas de chat por pergunta em vez de uma. Um booleano custa 2; um `enum` de 20 valores custa 20. Tokens e latência crescem na mesma proporção, e cada chamada vira uma tentativa no ledger. A tokenização dos rótulos não muda: os rótulos são os mesmos em todas as rotações.

**Quando usar:** com modelos pequenos, quando a probabilidade importa mais que a latência, ou quando as respostas mudam com a ordem das opções. Com poucas opções (booleanos, `enum` curtos) o custo é pequeno. Para `enum` longos, ou para um modelo que já se mostrou estável à ordem das opções, `fixed` basta. Fora da avaliação, passe `optionOrder: OpenJevOptionOrder.AverageRotations` ao construtor de `OpenJevFlowCompletion`.

## Configurar `laya`

- **Endpoint:** a URL **completa** `.../v1/systemone`, absoluta, HTTP(S), loopback, sem userinfo, query nem fragment. Exemplo: `http://127.0.0.1:18765/v1/systemone`.
- **Modelo:** `english`, `multilingual` ou `typed-decisions`. Outros nomes são recusados (auto-roteamento implícito é proibido).
- **Limiar:** o mesmo campo `threshold`, aplicado ao `noul`.

`SystemOneFlowCompletion` e a validação do manifesto também aceitam os ids do Hugging Face `convaiinnovations/laya`, `convaiinnovations/laya-multilingual` e `convaiinnovations/laya-typed-decisions`. No Laya 0.4.18, `convaiinnovations/laya` é o id do pacote e significa "escolha por detecção de idioma": com texto em inglês vai para `english` (`routing.reason: "English Latin text"`), com texto em português vai para `multilingual` (`"Latin script but language looks like 'pt', not English"`). Como a resposta sempre traz `model: "laya-rl-agent"`, o cliente não detectaria esse desvio. Por isso, no Cortex, `convaiinnovations/laya` significa o checkpoint `english`: o adaptador envia `model: "english"` ao Laya. Os outros dois ids são roteados explicitamente pelo próprio Laya (`routing.reason: "explicit model='multilingual'"` / `"explicit model='typed-decisions'"`) e vão como estão. Os nomes curtos continuam sendo a forma preferida.

Para subir o Laya fora dos testes, com as mesmas versões e o mesmo ambiente de `LayaLocalModelTests`:

```powershell
$root = if ($env:CORTEX_LOCAL_MODELS_DIR) { $env:CORTEX_LOCAL_MODELS_DIR } else { "$env:LOCALAPPDATA\Cortex\local-models" }
$env:LAYA_HOST = '127.0.0.1'; $env:LAYA_PORT = '18765'
$env:LAYA_DEVICE = 'cuda'; $env:LAYA_PRELOAD = '1'
$env:LAYA_MODELS = 'typed-decisions'   # o checkpoint que o perfil usa: english, multilingual ou typed-decisions
$env:LAYA_REVISION = '7b928d828b7b0e022f929d9bd2e44165aa270148'
$env:LAYA_API_KEY = ''                 # sem chave = sem credencial; no PowerShell, '' remove uma chave herdada
& "$root\laya-venv\Scripts\python.exe" -m laya.serve
```

Como nos testes, cada servidor pré-carrega um único checkpoint (`LAYA_MODELS`). Para usar outro, suba outro servidor (outra porta) com o `LAYA_MODELS` correspondente. `$root` segue a mesma regra do cache de downloads (abaixo). O venv é criado pelos testes `LocalModels` (ou manualmente com `py -3.11 -m venv` e `pip install --extra-index-url https://download.pytorch.org/whl/cu126 "torch==2.14.1+cu126" "laya[serve]==0.4.18"`). `GET /health` mostra `checkpoint_devices` e `revisions` de cada checkpoint.

## `tools/start-openjev.ps1`

Sobe um `llama-server` fixado em loopback, em primeiro plano, fora dos testes. Pare com Ctrl+C.

```powershell
powershell -File tools/start-openjev.ps1                       # minicpm5-2b, porta livre
powershell -File tools/start-openjev.ps1 -Model qwen3-0.6b -Port 18080
```

- `-Model`: `qwen3-0.6b`, `minicpm5-2b` (padrão) ou `qwen3.5-4b`, as chaves de `gguf` em `config/local-models.json`. O alias do servidor é a própria chave.
- `-Port`: se omitido, escolhe uma porta livre de loopback.
- Lê o mesmo manifesto e o mesmo cache dos testes, baixa o que falta para `*.partial`, confere o SHA-256 e usa o mesmo lock de arquivo, então não conflita com testes em execução.
- Executa `llama-server -m <gguf> --alias <modelo> --host 127.0.0.1 --port <porta> -ngl 999 -c 2048` e imprime:

```
Endpoint: http://127.0.0.1:<porta>/  Model: <modelo>
```

Esses dois valores são o endpoint e o modelo de `openjev`. Com `-c 2048`, um estado maior que o contexto faz o `llama-server` responder 400, que chega como `HttpRequestException` com `failureCategory`.

## Versões fixadas

`config/local-models.json` é a fonte única para testes e script:

| Item | Pin |
|---|---|
| llama.cpp | `b12015`, `llama-b12015-bin-win-cuda-12.4-x64.zip` + `cudart-llama-bin-win-cuda-12.4-x64.zip`, com SHA-256 |
| `qwen3-0.6b` | `Qwen/Qwen3-0.6B-GGUF@23749fef…`, `Qwen3-0.6B-Q8_0.gguf` |
| `minicpm5-2b` | `openbmb/MiniCPM5-2B-GGUF@2079a22f…`, `MiniCPM5-2B-Q4_K_M.gguf` |
| `qwen3.5-4b` | `bartowski/Qwen_Qwen3.5-4B-GGUF@4168f45a…`, `Qwen_Qwen3.5-4B-Q4_K_M.gguf` |
| Laya | `laya[serve]==0.4.18`, pesos `convaiinnovations/laya@7b928d82…`, `torch==2.14.1+cu126` |

## Cache de downloads

Os downloads são persistentes, para não baixar gigabytes a cada execução. A raiz é `%LOCALAPPDATA%\Cortex\local-models\`, ou o diretório em `CORTEX_LOCAL_MODELS_DIR`:

- `llama.cpp\`: pasta única, apagada e substituída quando a tag fixada muda.
- `models\`: os GGUFs.
- `laya-venv\`: o venv do Laya (Python 3.11).

Os pesos do Laya ficam no cache padrão do Hugging Face (`%USERPROFILE%\.cache\huggingface\hub`), não na raiz acima. Todo download vai para `*.partial`, tem o SHA-256 verificado e só então é renomeado. Hash divergente apaga o parcial e falha; a próxima execução baixa de novo. Para liberar espaço, apague a raiz (e, se quiser, o checkpoint no cache HF).

## Testes de integração

Os testes reais ficam em `tests/Cortex.Inference.Tests/LocalModels/`, com `[Trait("Category", "LocalModels")]`:

```powershell
dotnet test tests/Cortex.Inference.Tests --filter "Category=LocalModels"
dotnet test tests/Cortex.Inference.Tests --filter "Category=LocalModels&FullyQualifiedName~OpenJev"
dotnet test tests/Cortex.Inference.Tests --filter "Category=LocalModels&FullyQualifiedName~Laya"
```

Eles baixam o que falta, sobem o servidor em uma porta livre de loopback, esperam `/health`, rodam três casos pelo caminho completo `ProviderFlowCompletion` → provedor e encerram a árvore de processos mesmo em falha. Exigem GPU CUDA (para o Laya, também Python 3.11 com o launcher `py`). Faltar GPU, rede ou servidor é **falha** com mensagem clara, nunca skip. A primeira execução baixa vários GB; o Laya leva alguns minutos para montar o venv.

Os casos (`LocalModelDemoCases.cs`) são os dois exemplos da demo do OpenJev e um booleano óbvio:

- `account`: fila de suporte → `Account access support`
- `email`: e-mail de folha de pagamento pedindo credenciais → `Phishing`
- `boolean`: o e-mail pede credenciais? → `true`

Validações:

- **OpenJev** (`qwen3-0.6b`, `minicpm5-2b`, `qwen3.5-4b`): `CUDA0` presente, `model` = alias, todas as opções na ordem, probabilidades finitas, soma 1 ± 1e-6, escolha = argmax. A escolha correta é obrigatória para MiniCPM5 2B e Qwen3.5 4B; no Qwen3 0.6B só é registrada. `Label_bias_does_not_change_label_distribution` compara a distribuição com e sem `logit_bias` (tolerância 1e-4).
- **Laya** (`english`, `multilingual`, `typed-decisions`): `/health` deve mostrar o checkpoint em `cuda` e na revisão fixada, a resposta passa pelo `SystemOneClient` estrito e `routing.model` deve ser o checkpoint pedido. A correção semântica só é registrada.

A seleção offline do [README](../README.md) exclui esses testes com o filtro `Category!=LocalModels`. Um `dotnet test` sem filtro executa esses testes.

## Resultado observado

Execução de 2026-10-30, RTX 4090 (driver 617.42), com as versões fixadas acima (llama.cpp b12015, Laya 0.4.18, pesos `7b928d82…`, torch 2.14.1+cu126). Latência é o tempo HTTP por decisão.

### OpenJev

Números atuais: execução de 2026-10-30 depois de tirar o cabeçalho `Field: <nome>` da pergunta e trocar as opções booleanas para `Yes`/`No` (antes: `Field: <campo>\n<descrição>` e `true`/`false`). Os limiares e as expectativas não mudaram. No caso `boolean` mudaram o texto das opções no teste (`["Yes", "No"]`, que o teste compara com o que vai no fio) e a tarefa: de `Answer the field about this email.`, que citava um "field" que o prompt não nomeia mais, para `Classify this email.`. O teste do MiniCPM5 2B **continua falhando** pelo caso `boolean`.

| Modelo | Caso | Escolha | Probabilidades | Antes (prompt antigo) | Tokens in/out | Latência |
|---|---|---|---|---|---|---|
| qwen3-0.6b | account | Account access support ✔ | Account 0.8023 / Billing 0.0006 / Close 0.1972 | Account 0.9864 / 0.0028 / 0.0108 | 106/1 | 39 ms |
| qwen3-0.6b | email | Phishing ✔ | Legitimate 0.0595 / Spam 0.0259 / Phishing 0.9147 | Phishing 0.8236 (0.1578 / 0.0186) | 123/1 | 19 ms |
| qwen3-0.6b | boolean | true ✔ | Yes 0.9980 / No 0.0020 | true 0.9893 | 124/1 | 22 ms |
| minicpm5-2b | account | Account access support ✔ | Account 0.9190 / Billing 0.0491 / Close 0.0318 | Account 0.4251 / 0.1576 / 0.4173 | 111/1 | 27 ms |
| minicpm5-2b | email | Phishing ✔ | Legitimate 0.0000001 / Spam 0.0541 / Phishing 0.9459 | Phishing 0.7319 (0.000001 / 0.2681) | 129/1 | 25 ms |
| minicpm5-2b | boolean | **false ✘** | Yes 0.0638 / No 0.9362 | true 0.1318 ✘ | 130/1 | 18 ms |
| qwen3.5-4b | account | Account access support ✔ | Account 0.9622 / Billing 0.0106 / Close 0.0271 | Account 0.9498 / 0.0052 / 0.0449 | 111/1 | 145 ms |
| qwen3.5-4b | email | Phishing ✔ | Legitimate 0.000020 / Spam 0.000038 / Phishing 0.999941 | Phishing 0.999975 | 128/1 | 101 ms |
| qwen3.5-4b | boolean | true ✔ | Yes 0.9183 / No 0.0817 | true 0.9343 | 130/1 | 93 ms |

Observações:

- **O novo formato ajuda as escolhas**: o MiniCPM5 deixa de quase empatar no `account` (0.919, antes 0.425 vs 0.417) e sobe no `email` (0.946, antes 0.732; chamando `DecideAsync` direto com só a tarefa, 0.993). O Qwen3 0.6B sobe no `email` e cai no `account` (0.80, ainda correto). O Qwen3.5 4B fica praticamente igual.
- **MiniCPM5 2B erra o `boolean`** de forma determinística. Com a tarefa `Classify this email.`, P(Yes) = 0.064 (antes, com o prompt antigo, P(true) = 0.13). Com a tarefa anterior, `Answer the field about this email.`, eram 0.015. Uma sonda direta (`DecideAsync`, mesmo estado e essa tarefa anterior) mostrou que tirar o cabeçalho `Field:` é o que mais pesa nesse caso: com `Field:`, 0.132 (`true`/`false`) e 0.181 (`Yes`/`No`); sem `Field:`, 0.025 e 0.015. Fica registrado como limite do MiniCPM5 2B nesse tipo de pergunta. Para decisões sim/não com modelo pequeno, prefira o Qwen3 0.6B ou o Qwen3.5 4B, que acertam com folga.
- **`logit_bias` e logprobs:** no `llama-server` b12015, os logprobs informados são bit a bit idênticos com e sem `logit_bias` +100 (MiniCPM5 2B, `email`: Legitimate −15.8934, Spam −5.0233, Phishing −0.0066). O servidor aparentemente informa `top_logprobs` antes de aplicar o viés. O teste confirma que o viés não distorce as probabilidades informadas, mas não mostra o efeito do viés na amostragem.
- Com o GGUF em cache, o `llama-server` sobe em 1–2 s.

### Laya

Os três testes passam. Todos os checkpoints respondem **Spam** para o e-mail de phishing. Isso é só registrado, conforme o desenho.

| Checkpoint | Caso | Escolha | Probabilidades / noul | Confiança | Tokens in/out | Latência |
|---|---|---|---|---|---|---|
| english | account | Account access support ✔ | 0.8364 / 0.0402 / 0.1235 | 0.5113 | 82/0 | 372 ms (1ª chamada) |
| english | email | **Spam ✘** | Legitimate 0.1652 / Spam 0.6678 / Phishing 0.1670 | 0.2118 | 101/0 | 26 ms |
| english | boolean | true ✔ | noul 0.8478 | 0.8478 | 118/0 | 30 ms |
| multilingual | account | Account access support ✔ | 0.4480 / 0.2717 / 0.2803 | 0.0258 | 80/0 | 304 ms |
| multilingual | email | **Spam ✘** | Legitimate 0.0230 / Spam 0.7588 / Phishing 0.2182 | 0.4280 | 98/0 | 25 ms |
| multilingual | boolean | true ✔ | noul 0.6837 | 0.6837 | 116/0 | 23 ms |
| typed-decisions | account | Account access support ✔ | 0.7266 / 0.0982 / 0.1752 | 0.3036 | 82/0 | 330 ms |
| typed-decisions | email | **Spam ✘** | Legitimate 0.2322 / Spam 0.4330 / Phishing 0.3347 | 0.0280 | 101/0 | 32 ms |
| typed-decisions | boolean | true ✔ | noul 0.6555 | 0.6555 | 118/0 | 37 ms |

O Laya sempre informa 0 tokens de saída. A primeira execução levou 2,85 min, quase tudo para montar o venv e baixar os pesos.
