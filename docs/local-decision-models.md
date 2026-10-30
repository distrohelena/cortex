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

Os testes `LocalModels` e o benchmark (abaixo) medem isso em todas as ordens.

A correção padrão é tirar a média entre as ordens. No perfil de avaliação, o `openjev` aceita o parâmetro opcional `optionOrder`:

- **`"fixed"`** (padrão, também quando o parâmetro falta): uma chamada por pergunta, como descrito acima, com `ProbabilityPolicy = "OpenJevLabelSoftmax"`.
- **`"rotations"`**: para uma pergunta com n opções, n chamadas, uma por rotação cíclica da lista (deslocamento 0…n−1). Os rótulos ficam sempre `A`, `B`, … na ordem da lista girada. A probabilidade de cada opção original é a média aritmética das n probabilidades. A escolha é o argmax da média; no booleano, P(Yes) médio é comparado com o limiar. O `observeDecision` recebe uma única decisão sintetizada por campo, com as opções na ordem original, `Probability` = média, `Logprob` = ln(média) (−∞ quando todas as ordens deram 0 à opção) e tokens e tempo somados. O resultado observado soma o uso de todas as chamadas e usa `ProbabilityPolicy = "OpenJevLabelSoftmaxRotationAverage"`.

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

Eles baixam o que falta, sobem o servidor em uma porta livre de loopback, esperam `/health`, rodam os casos de demo pelo caminho completo `ProviderFlowCompletion` → provedor e encerram a árvore de processos mesmo em falha. Todos os testes que sobem servidor ficam na mesma coleção xUnit sem paralelismo (`LocalModelServers`), então só um servidor ocupa a GPU por vez. Exigem GPU CUDA (para o Laya, também Python 3.11 com o launcher `py`). Faltar GPU, rede ou servidor é **falha** com mensagem clara, nunca skip. A primeira execução baixa vários GB; o Laya leva alguns minutos para montar o venv.

Os casos (`LocalModelDemoCases.cs`) são os dois exemplos da demo do OpenJev e um booleano óbvio:

- `account`: fila de suporte → `Account access support`
- `email`: e-mail de folha de pagamento pedindo credenciais → `Phishing`
- `boolean`: o e-mail pede credenciais? → `true`

Cada caso roda em **todas as rotações cíclicas** das opções, porque uma ordem fixa esconde o viés de posição:

- **Escolha:** a rotação vai pelo esquema. O `enum` lista as opções na ordem girada, e o fluxo as pergunta nessa ordem (no Laya, é a ordem dos `criteria`).
- **Booleano no OpenJev:** o fluxo sempre pergunta `A. Yes / B. No`, então o esquema não consegue girar. A ordem `Yes/No` vai pelo fluxo; a ordem `No/Yes` vai direto para `OpenJevClient.DecideAsync`, com a pergunta montada exatamente como o fluxo monta (tarefa, linha em branco, descrição). É a mesma chamada que o modo `AverageRotations` faz para a segunda ordem.
- **Booleano no Laya:** é uma pergunta `noul` sem `criteria`, com uma única ordem.

Para cada ordem, o log mostra P(resposta certa).

Validações:

- **OpenJev** (`qwen3-0.6b`, `minicpm5-2b`, `qwen3.5-4b`): `CUDA0` presente, `model` = alias, todas as opções na ordem enviada, probabilidades finitas, soma 1 ± 1e-6, escolha = argmax (no booleano, P(Yes) ≥ 0.5). Dois testes por modelo:
  - `Demo_cases_are_answered_in_every_fixed_option_order`: modo `Fixed`, uma chamada por ordem.
  - `Demo_cases_are_answered_with_rotation_averaging`: modo `AverageRotations` pelo fluxo.

  Para MiniCPM5 2B e Qwen3.5 4B, a escolha correta é obrigatória **em todas as ordens** e com a média. No Qwen3 0.6B, só é registrada. `Label_bias_does_not_change_label_distribution` compara a distribuição com e sem `logit_bias` (tolerância 1e-4).
- **Laya** (`english`, `multilingual`, `typed-decisions`): `/health` deve mostrar o checkpoint em `cuda` e na revisão fixada, a resposta passa pelo `SystemOneClient` estrito e `routing.model` deve ser o checkpoint pedido, em todas as ordens dos `criteria`. A correção semântica só é registrada.

### Benchmark

```powershell
dotnet test tests/Cortex.Inference.Tests --filter Category=LocalModelsBenchmark
```

`LocalDecisionBenchmarkTests.Benchmark_local_decision_models` tem a categoria própria `LocalModelsBenchmark`, fora de `LocalModels`. Ele roda o corpus `tests/fixtures/local-decision-cases.json`: 24 casos de resposta inequívoca, 12 em inglês e 12 em português do Brasil, sendo 16 escolhas de 2 a 5 opções e 8 booleanos. Os domínios são triagem de suporte, phishing e spam, urgência, intenção, se o assistente deve responder num grupo de WhatsApp, e tom/sentimento.

- **OpenJev:** cada GGUF responde todos os casos em todas as rotações (`Fixed`) e de novo com `AverageRotations`.
- **Laya:** cada checkpoint responde todos os casos em todas as rotações dos `criteria`.

Um servidor por vez, iniciado como nos testes `LocalModels`. O benchmark não faz assert de acurácia: só valida a estrutura (probabilidades finitas, soma 1, escolha válida). Quando um rótulo falta no top 20 de logprobs, o OpenJev recusa a resposta. O benchmark registra isso como resposta "rejeitada", que conta como erro, e não aborta.

O relatório é sempre gravado em **`artifacts/local-models/benchmark.md`**, sobrescrito a cada execução. Ele traz data, GPU, versões fixadas, a tabela-resumo, os recortes por idioma e tipo, e a lista de casos errados com P(resposta certa) por ordem. Nesta máquina, o benchmark inteiro leva menos de 1 min.

A seleção offline do [README](../README.md) exclui os dois grupos com `Category!=LocalModels&Category!=LocalModelsBenchmark`. Um `dotnet test` sem filtro executa os dois.

## Resultado observado

Execução de 2026-10-30, RTX 4090 (driver 617.42), com as versões fixadas acima (llama.cpp b12015, Laya 0.4.18, pesos `7b928d82…`, torch 2.14.1+cu126).

### Viés de posição

Modelos pequenos escolhem em parte pela **posição** da opção (o rótulo `A`, `B`, …), não só pelo conteúdo. Uma única ordem fixa pode acertar por sorte e errar quando as mesmas opções vêm em outra ordem. Por isso as métricas abaixo separam:

- **média por rotação:** por caso, a fração das ordens em que acertou, com média sobre os casos;
- **pior caso:** a fração dos casos certos em *todas* as ordens;
- **com média:** para o OpenJev, o modo `AverageRotations` (média das probabilidades entre as ordens); para o Laya, a mesma média calculada depois, sobre as ordens já perguntadas, já que o Laya não tem esse modo.

Um modelo sem viés de posição tem média por rotação = pior caso. A distância entre as duas mede o viés.

### Benchmark (24 casos)

Do `artifacts/local-models/benchmark.md` desta execução. Latência é a mediana por chamada HTTP de inferência. "Rejeitadas" são respostas recusadas pelo OpenJev por rótulo fora do top 20; contam como erro.

| Modelo | Modo | Média por rotação | Pior caso | Com média | Latência | Chamadas | Rejeitadas |
|---|---|---|---|---|---|---|---|
| openjev `qwen3-0.6b` | fixed, todas as rotações | 63.9% | 41.7% | — | 11 ms | 69 | 0 |
| openjev `qwen3-0.6b` | `AverageRotations` | — | — | 70.8% | 11 ms | 69 | 0 |
| openjev `minicpm5-2b` | fixed, todas as rotações | 67.2% | 33.3% | — | 15 ms | 69 | 3 |
| openjev `minicpm5-2b` | `AverageRotations` | — | — | 66.7% | 15 ms | 65 | 3 |
| openjev `qwen3.5-4b` | fixed, todas as rotações | 91.7% | 91.7% | — | 58 ms | 69 | 0 |
| openjev `qwen3.5-4b` | `AverageRotations` | — | — | 91.7% | 58 ms | 69 | 0 |
| laya `english` | todas as rotações dos `criteria` | 79.2% | 75.0% | 79.2% | 29 ms | 61 | 0 |
| laya `multilingual` | todas as rotações dos `criteria` | 76.8% | 66.7% | 79.2% | 19 ms | 61 | 0 |
| laya `typed-decisions` | todas as rotações dos `criteria` | 79.2% | 79.2% | 79.2% | 24 ms | 61 | 0 |

Recortes (média por rotação / pior caso; nas linhas `AverageRotations`, a acurácia com média):

| Modelo | Modo | en (12) | pt (12) | escolha (16) | booleano (8) |
|---|---|---|---|---|---|
| `qwen3-0.6b` | fixed | 63.9% / 33.3% | 63.9% / 50.0% | 70.8% / 50.0% | 50.0% / 25.0% |
| `qwen3-0.6b` | `AverageRotations` | 75.0% | 66.7% | 81.2% | 50.0% |
| `minicpm5-2b` | fixed | 68.5% / 33.3% | 66.0% / 33.3% | 72.7% / 31.2% | 56.2% / 37.5% |
| `minicpm5-2b` | `AverageRotations` | 66.7% | 66.7% | 75.0% | 50.0% |
| `qwen3.5-4b` | fixed | 91.7% / 91.7% | 91.7% / 91.7% | 100.0% / 100.0% | 75.0% / 75.0% |
| `qwen3.5-4b` | `AverageRotations` | 91.7% | 91.7% | 100.0% | 75.0% |
| laya `english` | criteria | 77.8% / 75.0% | 80.6% / 75.0% | 87.5% / 81.2% | 62.5% / 62.5% |
| laya `multilingual` | criteria | 77.2% / 66.7% | 76.4% / 66.7% | 77.7% / 62.5% | 75.0% / 75.0% |
| laya `typed-decisions` | criteria | 83.3% / 83.3% | 75.0% / 75.0% | 87.5% / 87.5% | 62.5% / 62.5% |

O que os números mostram:

- **Qwen3.5 4B** é o único sem viés de posição mensurável: média por rotação = pior caso = com média = 91.7%. Acerta todas as 16 escolhas em todas as ordens. Os dois erros são o mesmo caso nos dois idiomas: no WhatsApp, quando o assistente deveria ficar calado (conversa entre amigos; "bom dia" em família), ele responde que deve falar, com P(certo) ≤ 0.015. Custa cerca de 4–5× a latência dos menores (58 ms por chamada).
- **Qwen3 0.6B e MiniCPM5 2B** têm viés forte: o pior caso (41.7% e 33.3%) fica muito abaixo da média por rotação (63.9% e 67.2%). No Qwen3 0.6B, `AverageRotations` sobe a acurácia de 63.9% para 70.8% (escolhas: 70.8% → 81.2%). No MiniCPM5 2B, a média não ajuda (66.7%). Além disso, em 3 de 69 ordens um rótulo saiu do top 20 de logprobs, e o OpenJev recusou a resposta. Com `AverageRotations`, uma ordem recusada derruba a pergunta inteira.
- **Booleanos são o ponto fraco de todos.** Os três GGUFs erram os dois casos "ficar calado" no WhatsApp; os menores também erram "a resposta é para hoje?" e "é reclamação?" em alguma ordem.
- **Laya** fica em ~79% e é bem mais estável à ordem que os GGUFs pequenos (`typed-decisions`: pior caso = média). Erra phishing vs. spam (o SMS bancário em português sai errado nos três checkpoints, em todas as ordens). Os três checkpoints erram booleanos de WhatsApp, e `english` e `typed-decisions` também erram o golpe dos gift cards. O `multilingual` não é melhor que o `english` em português (76.4% vs. 80.6%).

### Recomendação

Com esses dados:

- Para decisões tipadas locais, **`openjev` com `qwen3.5-4b`** é a melhor opção medida. É o mais preciso, e a ordem das opções não muda suas respostas. `optionOrder: "fixed"` basta para ele: `AverageRotations` custou n× as chamadas sem mudar nenhuma resposta.
- Se a latência importa mais que a precisão, o **Laya `typed-decisions`** ou o **`english`** (~25 ms, ~79%, estáveis à ordem) ficam à frente dos GGUFs pequenos. A ressalva é a fraqueza em phishing vs. spam e em booleanos.
- **Qwen3 0.6B** só com `AverageRotations`, e mesmo assim abaixo do Laya. **MiniCPM5 2B** não é recomendado: tem o pior pior-caso (33.3%), a média não o corrige, e algumas respostas são recusadas.
- Nenhum GGUF medido acerta "ficar calado" num grupo de WhatsApp. Essa decisão não deve ficar só com um desses modelos.

Ressalva: são 24 casos; um caso vale 4.2 pontos percentuais. As diferenças pequenas (por exemplo, entre os checkpoints do Laya) não são conclusivas.

### Casos de demo em todas as ordens

Do teste honesto (`LocalModels`), P(resposta certa) em cada rotação. Na escolha, a ordem 0 é a do caso e a ordem k começa pela opção k. No booleano do OpenJev, as ordens são `Yes/No` e `No/Yes`. Em **negrito**, as respostas erradas.

| Modelo | `account` | `email` | `boolean` | Com média (`account` / `email` / `boolean`) |
|---|---|---|---|---|
| openjev `qwen3-0.6b` | 0.80 / 0.65 / 0.73 | 0.91 / **0.08** / 1.00 | 1.00 / 0.64 | 0.73 / 0.66 / 0.82 |
| openjev `minicpm5-2b` | 0.92 / 1.00 / 1.00 | 0.95 / 1.00 / 1.00 | **0.06** / 0.98 | 0.97 / 0.98 / 0.52 |
| openjev `qwen3.5-4b` | 0.96 / 1.00 / 0.99 | 1.00 / 1.00 / 1.00 | 0.92 / 0.84 | 0.98 / 1.00 / 0.88 |
| laya `english` | 0.84 / 0.87 / 0.79 | **0.17 / 0.11 / 0.28** | 0.87 | — |
| laya `multilingual` | 0.45 / **0.37 / 0.32** | **0.22 / 0.44 / 0.06** | **0.10** | — |
| laya `typed-decisions` | 0.73 / 0.73 / 0.68 | **0.33 / 0.33 / 0.38** | 0.62 | — |

Resultado dos testes:

- O teste `Fixed` do **MiniCPM5 2B fica vermelho**: ele erra o `boolean` na ordem `Yes/No` (P(Yes) = 0.064) e acerta em `No/Yes` (0.98).
- Com `AverageRotations`, o MiniCPM5 2B passa, mas no limite: P(Yes) = 0.52.
- Qwen3.5 4B passa nos dois modos.
- O Qwen3 0.6B erra o `email` numa ordem (`Spam` com P(Phishing) = 0.08), o que só é registrado.
- O Laya erra o `email` em todas as ordens dos três checkpoints, também só registrado.

### Notas de execução

- **`logit_bias` e logprobs:** no `llama-server` b12015, os logprobs informados são bit a bit idênticos com e sem `logit_bias` +100. Por exemplo, MiniCPM5 2B no `email`: Legitimate −15.8934, Spam −5.0233, Phishing −0.0066. O servidor aparentemente informa `top_logprobs` antes de aplicar o viés. Por isso um rótulo muito improvável pode ficar fora do top 20 mesmo com viés, e é isso que gera as respostas rejeitadas do MiniCPM5 2B no benchmark.
- Com o GGUF em cache, o `llama-server` sobe em 1–2 s.
- O Laya sempre informa 0 tokens de saída. A primeira execução levou 2,85 min, quase tudo para montar o venv e baixar os pesos.
