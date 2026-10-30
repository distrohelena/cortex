# Modelos locais de decisão tipada: OpenJev e Laya

Data: 2026-10-30
Estado: rascunho para aprovação da Helena.

## Intenção

A Helena quer que o Cortex suporte modelos locais de decisão tipada. Esses modelos respondem escolhas e booleanos com probabilidades, não texto livre, como o Jev da TypeSafe faz hoje pela nuvem. Ela nunca rodou nenhum deles. O pedido é que o suporte exista, fique pronto para uso e seja **testado de verdade nesta máquina**: um teste de integração baixa os modelos, sobe o servidor e confere as respostas.

Ela citou dois modelos:

- **OpenJev** (hoje chamado SemIf, https://openjev.com). É uma demo que roda só no navegador, com wllama/llama.cpp sobre WebGPU. Não tem servidor nem pacote. A Helena aprovou explicitamente que o Cortex rode o método dele localmente ("OpenJev seria lindo rodar local").
- **Laya** (`convaiinnovations/laya`). O Cortex já fala com ele pelo provedor `laya`, mas ele nunca foi executado de verdade aqui.

## O que se sabe sobre o OpenJev

O código está em `https://openjev.com/worker.js`. Uma cópia de investigação fica em `C:\temp\openjev-recon\`. O método:

- Rótulos `A`, `B`, `C`… para 2 a 20 opções.
- Mensagens:
  - system: `Make the requested decision from the supplied state. Follow the output format exactly.`
  - user: `State:\n{state}\n\nQuestion:\n{question}\n\nAllowed options:\n{A. opção\nB. opção…}\n\nReply with exactly one option letter from: A, B, ….`
- Uma chamada de chat no formato OpenAI com:
  - `max_tokens: 1`, `temperature: 1`, `top_k: 0`, `top_p: 1`
  - `logprobs: true`, `top_logprobs: 20`
  - `logit_bias` de +100 nos tokens dos rótulos
  - gramática GBNF `root ::= "A" | "B" | …`
  - `chat_template_kwargs: { enable_thinking: false }`
  - `cache_prompt: false`
- Probabilidades: lê o logprob de cada rótulo em `choices[0].logprobs.content[0].top_logprobs` (pelo `token` ou pelo byte único) e aplica softmax só sobre os rótulos. Se faltar algum rótulo, é erro.
- Modelos fixados:
  - `Qwen/Qwen3-0.6B-GGUF@23749fefcc72300e3a2ad315e1317431b06b590a`, arquivo `Qwen3-0.6B-Q8_0.gguf`
  - `openbmb/MiniCPM5-2B-GGUF@2079a22f3beaa4e306449978533478fe0522f4b3`, arquivo `MiniCPM5-2B-Q4_K_M.gguf`
  - `bartowski/Qwen_Qwen3.5-4B-GGUF@4168f45a16a1290d65a4ec0fa312ae917a4c15d6`, arquivo `Qwen_Qwen3.5-4B-Q4_K_M.gguf`
- O modo "geração", em que o modelo escreve as probabilidades em JSON, é só a comparação da demo.

Essa API é a do `llama-server` do llama.cpp, que tem release oficial para Windows com CUDA. A máquina tem RTX 4090 (24 GB), Python 3.11 e LM Studio. Não tem Bun.

## O que se sabe sobre o Laya

Instala com `pip install "laya[serve]"` e roda com `LAYA_DEVICE=cuda LAYA_PRELOAD=1 laya-serve`. Escuta em `0.0.0.0:8000`, `POST /v1/systemone`, no mesmo formato do Jev. Sem credencial, a menos que `LAYA_API_KEY` esteja definida. Tem três checkpoints:

- raiz/`english`: ModernBERT-large, cerca de 808 MB
- `multilingual`: mmBERT-base, cerca de 647 MB, já presente no cache HF local
- `typed-decisions`

O Windows não é documentado no model card, só no README do GitHub. O cliente existente é `SystemOneClient` com `SystemOneProbabilityPolicy.LayaFourDecimals`. Os nomes de modelo aceitos estão em `SystemOneFlowCompletion`.

## Objetivos

1. **Provedor `openjev`** no Cortex que reproduz fielmente o método acima contra um `llama-server` local, para **qualquer** GGUF de instrução e não só os três da demo. Esse é o sentido de "outros modelos estruturais locais".
2. Mesmo contrato de saída de `SystemOneFlowCompletion`. Um nó de grafo pode trocar `jev`/`laya` por `openjev` mudando só provedor, modelo e endpoint.
3. `openjev` reconhecido em todo lugar que hoje enumera `jev`/`laya`: validação de manifesto e de perfil da avaliação, `EvaluationProviderFactory`, ledger, HTTP handler, parser de observação, projeção de preço (custo zero, local) e a regra de "só decisão" em `ObservedShadowRun`. Também em qualquer composição de provedores dos hosts e do editor que liste provedores. O builder deve localizar todos por busca (`"laya"`), não por esta lista.
4. **Testes de integração reais** para OpenJev (os três modelos) e Laya (os três checkpoints). Eles baixam o que falta, sobem o servidor, executam e encerram tudo.
5. Um script para a Helena rodar o OpenJev local fora dos testes, com as mesmas versões fixadas.

## Não objetivos

- Modo geração do OpenJev.
- Open Spark Jev e LocalJev, que ficam para depois.
- Perguntas `score` e texto livre. Continuam recusados, como no SystemOne.
- O Cortex gerenciar o ciclo de vida do `llama-server` em produção. O host aponta para um endpoint; só os testes e o script sobem o servidor.
- Roteamento automático ou eleição de "melhor modelo".

## Anti-objetivos

- Um teste "de integração" que na verdade usa respostas simuladas. A Helena pediu execução real.
- Probabilidades inventadas ou renormalizações silenciosas. Rótulo ausente, modelo errado ou tokenização ambígua é erro explícito, no estilo de validação estrita que o `SystemOneClient` já tem.
- Endpoint não-loopback para `openjev` ou para o Laya.
- Downloads sem versão fixada ou sem verificação de hash.

## Desenho

### Adaptador `openjev`

Fica em `src/Cortex.Inference/OpenJev/` e implementa `IFlowCompletion`.

- **Esquema aceito:** o mesmo subconjunto de `SystemOneFlowCompletion`. É um objeto fechado de campos obrigatórios, cada um uma string `enum` (2 a 20 valores, o limite do OpenJev) ou um `boolean`. Os perfis de escolha (`SystemOneChoiceQuestion`) são aceitos da mesma forma.
- **Uma chamada por campo**, com o prompt exato do OpenJev:
  - `state` = `request.Inputs` serializado em JSON compacto (ou a string, se for string).
  - `question` = tarefa (`request.System`), uma linha em branco e a `description` do campo no esquema (o nome do campo, se não houver descrição não vazia) e, se houver, a orientação do perfil de escolha. Não há cabeçalho `Field: <nome>`: ele piorava medidamente os modelos pequenos.
  - opções = descrições dos critérios (do perfil) ou o próprio valor do enum.
- **Booleano** vira duas opções, `A. Yes` e `B. No`, depois da descrição do campo. O resultado é `true` quando P(A) = P(Yes) ≥ limiar, com a mesma semântica de limiar do `noul`.
- **Tokens dos rótulos:** antes da primeira chamada para um modelo, usar `POST /tokenize` do `llama-server` (sem tokens especiais) em `"A"`…`"T"`. Cada rótulo precisa virar exatamente um token, senão é erro. Os ids vão para o `logit_bias` e ficam em cache por endpoint+modelo. Isso substitui o `labelBase` fixo da demo e é o que permite qualquer GGUF.
- **Modelo:** o `llama-server` é iniciado com `--alias <modelo>`. O adaptador exige que o `model` da resposta seja igual a `request.Model`.
- **Endpoint:** absoluto, HTTP(S), loopback, sem userinfo/query/fragment.
- **Saída:** igual à do SystemOne (valor escolhido ou booleano). Um callback de observação, análogo ao de `SystemOneFlowCompletion`, recebe as probabilidades e logits por campo. O `usage` (soma de `prompt_tokens`/`completion_tokens`) e as falhas vão para `CompletionDiagnostics`, com as mesmas categorias de falha usadas pelos outros adaptadores.

### Versões fixadas

Um manifesto único, `config/local-models.json`, guarda:

- a release do llama.cpp: tag, URLs dos zips Windows CUDA 12 (binário + cudart) e SHA-256;
- os GGUFs: repo, commit, arquivo e SHA-256;
- a versão do pacote `laya` e o commit/revisão dos pesos.

Testes e script leem esse arquivo.

### Cache de downloads

Os downloads não são temporários, para não baixar gigabytes a cada execução. Ficam em uma raiz persistente, `%LOCALAPPDATA%\Cortex\local-models\`, que pode ser trocada pela variável `CORTEX_LOCAL_MODELS_DIR`:

- `llama.cpp\` é uma pasta única, substituída quando a versão fixada muda. Não guardar uma pasta por versão.
- `models\` guarda os GGUFs.
- `laya-venv\` é o venv do Laya. Os pesos do Laya continuam no cache HF padrão.

Downloads vão para arquivo parcial, têm o hash verificado e só então são renomeados.

### Testes

- **Unitários (offline, sempre rodam)** em `tests/Cortex.Inference.Tests`, com `HttpMessageHandler` falso. Cobrem:
  - forma exata da requisição: prompt, gramática, `logit_bias`, parâmetros;
  - softmax;
  - rótulo ausente e modelo divergente;
  - rótulo com vários tokens;
  - esquema não suportado;
  - booleano com limiar;
  - perfil de escolha;
  - usage e diagnóstico;
  - endpoint não-loopback recusado.
- **Integração** com `[Trait("Category", "LocalModels")]`, fora da seleção offline do README (acrescentar `&Category!=LocalModels`).
  - **OpenJev:** para cada um dos três modelos, sobe o `llama-server` em uma porta livre de loopback (`-ngl 999 -c 2048 --alias …`), espera `/health`, roda e mata o processo mesmo em falha. Os casos são os dois exemplos da demo (fila de suporte → "Account access support"; e-mail → "Phishing") e um booleano óbvio. Todos passam pelo caminho completo `ProviderFlowCompletion` → `openjev`, não só pelo cliente HTTP.
    - Em todos os modelos: todas as opções presentes, probabilidades finitas, soma 1 ± 1e-6, escolha = argmax.
    - Escolha semanticamente correta: obrigatória para MiniCPM5 2B e Qwen3.5 4B. No Qwen3 0.6B, que a própria demo avisa ser menos preciso, só registrar na saída do teste.
  - **Laya:** cria/reaproveita o venv, instala a versão fixada, sobe `laya-serve` com CUDA em porta de loopback e espera ficar pronto. Roda os mesmos casos via provedor `laya` para `english`, `multilingual` e `typed-decisions`, com as validações estritas existentes do `SystemOneClient`.
  - Se faltar GPU/CUDA ou a rede falhar no download, o teste **falha** com mensagem clara. Ele não é pulado silenciosamente.
- **Script:** `tools/start-openjev.ps1 [-Model minicpm5-2b]` baixa (via manifesto) e sobe o `llama-server` em loopback, imprimindo endpoint e alias.

### Documentação

Uma seção curta em `docs/` (ou no README de Inference) diz como configurar `openjev`/`laya` local, como rodar `dotnet test --filter Category=LocalModels` e onde fica o cache.

## Restrições

- .NET 10, xUnit v3, `TreatWarningsAsErrors`. Seguir o estilo estrito de validação de `SystemOneClient`.
- Nada de worktrees. Commits pequenos e sequenciais por assunto: adaptador, registro do provedor, manifesto/infra de teste, testes OpenJev, testes Laya, script/docs.
- Temporários de investigação ficam em `C:\temp`.

## Decisões minhas (não confirmadas pela Helena)

- Rodar via `llama-server` nativo, não LM Studio nem uma lib .NET embutida. É a API exata usada pelo OpenJev e aproveita a CUDA.
- Rótulos por `/tokenize`, não por ids fixos.
- Booleano mapeado para duas opções.
- Cache persistente em `%LOCALAPPDATA%\Cortex\local-models`.
- Rigor semântico só nos modelos de 2B e 4B.
- Os três checkpoints do Laya nos testes.

## Esclarecimentos da revisão (decisões minhas)

- **Observação:** `openjev` sintetiza um `SystemOneResult` no formato de respostas do Jev. `choice` traz `probabilities` e `confidence`; booleano traz `noul` = P(A) = P(Yes). `ProbabilityPolicy` = `"OpenJevLabelSoftmax"`. Assim ledger, parser e `ObservedShadowRun` reaproveitam o caminho existente. Os logits brutos vão num campo adicional de observação só do `openjev`, sem mudar o contrato dos outros.
- **Limiar:** o mesmo campo de limiar de `jev`/`laya`, para que a troca seja drop-in. Nos testes e no script vale 0.5. Mais de 20 opções gera erro explícito. As chamadas por campo são sequenciais.
- **logit_bias e logprobs:** o softmax sobre os rótulos não muda com um viés uniforme. Mesmo assim, o teste de integração verifica isso empiricamente na versão fixada do llama.cpp: compara a distribuição com e sem `logit_bias` quando todos os rótulos aparecem no top 20. Se divergir, é um achado a reportar, não algo a ajustar em silêncio.
- **Falha semântica:** se o MiniCPM5 2B ou o Qwen3.5 4B errar um caso de demo, isso é um achado a reportar à Helena. Os casos não são trocados para o teste passar.
- **Laya:** um único `laya-serve` com `LAYA_PRELOAD=1`, roteando por `model` (o model card descreve um `Router` que escolhe o checkpoint por requisição). Os três checkpoints somam cerca de 2 GB e cabem na 4090.
  - A primeira tarefa do Laya é uma verificação rápida: instalar `laya[serve]` no Python 3.11/Windows e confirmar CUDA, roteamento e que `typed-decisions` responde no mesmo formato validado por `LayaFourDecimals`.
  - Se algo disso não se confirmar, parar e reportar antes de alterar o cliente.

## Fica com o builder

- Os nomes exatos de classes e arquivos.
- Escolher o asset CUDA certo da release do llama.cpp e obter os hashes.
- Como mudar a porta do `laya-serve`: pelo README do GitHub, ou usar uvicorn direto se não houver opção.
- Encontrar todos os pontos que enumeram provedores.
