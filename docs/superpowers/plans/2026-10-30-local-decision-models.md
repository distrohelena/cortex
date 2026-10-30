# Modelos locais de decisão tipada (OpenJev e Laya): plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** criar o provedor `openjev`, que reproduz o método do OpenJev contra um `llama-server` local com qualquer GGUF, e ter testes de integração reais que baixam e executam o OpenJev (3 modelos) e o Laya (3 checkpoints) nesta máquina.

**Architecture:**
- `OpenJevClient` faz uma chamada `/v1/chat/completions` por pergunta, com o prompt, a gramática e o `logit_bias` do OpenJev, e aplica softmax sobre os logprobs dos rótulos.
- `OpenJevFlowCompletion` converte o mesmo subconjunto de esquema do SystemOne (extraído para um helper compartilhado) e devolve a mesma saída.
- A infraestrutura de teste lê um manifesto de versões fixadas, baixa com verificação de hash para um cache persistente e sobe/encerra os servidores.

**Tech Stack:** .NET 10, xUnit v3, llama.cpp `llama-server` (Windows CUDA 12), Python 3.11 + `laya[serve]`, PowerShell 5.1.

**Spec:** `docs/superpowers/specs/2026-10-30-local-decision-models-design.md`

## Global Constraints

- `TreatWarningsAsErrors`. O estilo de validação estrita é o de `src/Cortex.Inference/SystemOne/SystemOneClient.cs`: erro explícito, sem normalizar nem adivinhar.
- Endpoints `openjev` e `laya`: absolutos, HTTP(S), loopback, sem userinfo/query/fragment.
- Opções por campo `openjev`: de 2 a 20 (rótulos `A`…`T`). Fora disso, `InvalidDataException`.
- `ProbabilityPolicy` do `openjev` = `"OpenJevLabelSoftmax"`.
- Prompt exato do OpenJev:
  - system: `Make the requested decision from the supplied state. Follow the output format exactly.`
  - user: `State:\n{state}\n\nQuestion:\n{question}\n\nAllowed options:\n{A. x\nB. y…}\n\nReply with exactly one option letter from: A, B, ….`
- Parâmetros de requisição:
  - `max_tokens:1`, `temperature:1`, `top_k:0`, `top_p:1`
  - `logprobs:true`, `top_logprobs:20`
  - `logit_bias` `{ "<id>": 100 }` por rótulo
  - `grammar` `root ::= "A" | "B" | …`
  - `cache_prompt:false`, `chat_template_kwargs:{"enable_thinking":false}`
- Cache de downloads: `%LOCALAPPDATA%\Cortex\local-models\`, ou `CORTEX_LOCAL_MODELS_DIR`. Subpastas:
  - `llama.cpp\` (pasta única, substituída quando a versão muda)
  - `models\`
  - `laya-venv\`
- Download: vai para `*.partial`, tem o SHA-256 verificado e só então é renomeado. Hash divergente apaga o parcial e falha.
- Testes reais: `[Trait("Category", "LocalModels")]`. Faltar GPU, rede ou servidor é **falha** com mensagem clara, nunca skip.
- Temporários de investigação ficam em `C:\temp`. Nada de worktrees. Um commit por tarefa.

## Review Focus

1. **GGUF cujo rótulo vira mais de um token, ou um token diferente de `"A"`.** Esperado: erro explícito antes da chamada de chat, nunca probabilidades erradas. Teste na Task 1.
2. **Estado maior que o contexto (`-c 2048`).** O `llama-server` responde 400. Esperado: `HttpRequestException` com `failureCategory` e corpo capturado só no diagnóstico, sem crash nem retry. Teste na Task 1.
3. **Teste interrompido ou falhando no meio.** Esperado: o processo `llama-server`/`laya-serve` é encerrado (árvore inteira) e a porta é liberada. Teste na Task 3.
4. **Download corrompido ou interrompido.** Esperado: hash recusado, `.partial` removido, e a próxima execução baixa de novo. Teste na Task 3.
5. **`model` da resposta diferente do alias pedido**, quando o servidor sobe outro GGUF. Esperado: `InvalidDataException`, sem resultado. Teste na Task 1.

---

### Task 1: Adaptador `openjev`

**Files:**
- Create: `src/Cortex.Inference/TypedDecisionSchema.cs`. Extrai de `SystemOneFlowCompletion.Questions` a conversão esquema → perguntas.
- Modify: `src/Cortex.Inference/SystemOne/SystemOneFlowCompletion.cs`. Passa a usar o helper; o comportamento não muda.
- Create: `src/Cortex.Inference/OpenJev/OpenJevClient.cs`
- Create: `src/Cortex.Inference/OpenJev/OpenJevFlowCompletion.cs`
- Test: `tests/Cortex.Inference.Tests/OpenJevClientTests.cs`, `tests/Cortex.Inference.Tests/OpenJevFlowCompletionTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace Cortex.Inference;
  internal static class TypedDecisionSchema
  {
      // Same JSON "questions" shape SystemOne sends today (instructions.field/description/task[/choiceGuidance],
      // type choice|noul, criteria). Throws the existing "System 1 flow outputs must be…" InvalidDataException.
      internal static JsonElement Questions(FlowCompletionRequest request,
          IReadOnlyDictionary<string, SystemOneChoiceQuestion> profiles, int maximumOptions);
  }

  namespace Cortex.Inference.OpenJev;
  public sealed record OpenJevOption(string Label, string Text, double Logprob, double Probability);
  public sealed record OpenJevDecision(string Model, IReadOnlyList<OpenJevOption> Options, int InputTokens, int OutputTokens, TimeSpan Elapsed);
  public sealed class OpenJevClient(HttpClient http, Uri baseEndpoint)   // baseEndpoint e.g. http://127.0.0.1:8080/
  {
      public Task<OpenJevDecision> DecideAsync(string model, string state, string question, IReadOnlyList<string> options,
          CompletionDiagnostics? diagnostics, CancellationToken token, bool applyLabelBias = true);
  }
  public sealed class OpenJevFlowCompletion(OpenJevClient client, double booleanThreshold,
      Action<string, SystemOneResult>? observe = null,
      IReadOnlyDictionary<string, SystemOneChoiceQuestion>? choiceQuestions = null,
      Action<string, string, OpenJevDecision>? observeDecision = null) : IFlowCompletion   // (nodeId, field, decision)
  ```
- Requests:
  - `POST {base}tokenize` with `{"content":"A","add_special":false}` → `{"tokens":[id]}`. Run once per model per client, for `A`…`T`, and cache the result.
  - `POST {base}v1/chat/completions`.
- Mapping in `OpenJevFlowCompletion`, per field in schema order, sequential:
  - `state`: `request.Inputs` when it is a JSON string; otherwise compact JSON.
  - `question`: `request.System + "\n\nField: " + field + "\n" + description`, plus `"\n\n" + choiceGuidance` when a profile exists.
  - choice options: the key, or `"{key}: {description}"` when the profile's description differs from the key. Labels follow enum order.
  - boolean options: `["true","false"]`, with `true` when P(A) ≥ `booleanThreshold`.
  - `SystemOneResult.Answers` uses Jev's shape:
    - choice: `{"type":"choice","choice":key,"probabilities":{key:p},"confidence":pMax}`
    - boolean: `{"type":"noul","noul":pTrue}`
  - Usage is summed over fields. `ProbabilityPolicy = "OpenJevLabelSoftmax"`.
- `request.Provider` must be `"openjev"`, `request.Model` must be non-empty, and the threshold must be in (0,1). Otherwise `InvalidDataException`.

- [ ] **Step 1: Escrever os testes que falham.** Usar o `Handler` de `tests/Shared/SystemOneTestSupport.cs` ou um handler com fila de respostas por URI.
  - `Sends_openjev_prompt_grammar_bias_and_sampling_exactly`: confere o corpo do chat contra as Global Constraints, incluindo `logit_bias` com os ids devolvidos pelo `/tokenize`.
  - `Softmax_over_label_logprobs_matches_reference`: logprobs A=-0.1, B=-2.3, C=-4.0 dão probabilidades iguais ao softmax de referência (tolerância 1e-12), com soma 1.
  - `Label_matched_by_single_byte_when_token_text_differs`: entrada com `token:" A"` e `bytes:[65]` é aceita.
  - `Missing_label_logprob_is_rejected`, `Non_finite_logprob_is_rejected`, `Model_mismatch_is_rejected` (Review Focus 5): todos dão `InvalidDataException`.
  - `Multi_token_label_is_rejected_before_chat` (Review Focus 1): `/tokenize` devolve dois tokens para `"B"`, o chat é chamado 0 vezes e sai `InvalidDataException`.
  - `Http_400_reports_provider_failure_without_retry` (Review Focus 2): sai `HttpRequestException` com `Data["failureCategory"]`, 1 chamada, e o corpo só aparece com captura habilitada.
  - `Non_loopback_endpoint_is_rejected`, `More_than_twenty_options_is_rejected`, `Tokenize_is_called_once_per_model`.
  - Flow:
    - `Maps_enum_and_boolean_fields_with_threshold`: o booleano com P(true)=0.6 dá `true` com limiar 0.5 e `false` com 0.7.
    - `Choice_profile_descriptions_become_option_text`.
    - `Observation_uses_jev_answer_shape_and_policy`.
    - `Unsupported_schema_is_rejected_without_calls`: texto livre, campo opcional, `score`.
- [ ] **Step 2: Rodar e ver falhar.** Comando: `dotnet test tests/Cortex.Inference.Tests --filter "FullyQualifiedName~OpenJev"`. Esperado: erro de compilação (tipos inexistentes).
- [ ] **Step 3: Extrair `TypedDecisionSchema.Questions`.** O `SystemOneFlowCompletion` passa a chamá-lo com `maximumOptions: 255`. Depois implementar `OpenJevClient` e `OpenJevFlowCompletion` conforme as Interfaces:
  - aplicar `ProtectSensitiveValue`/`CaptureResponse`/`ReportUsage`/`ReportFailure` como em `SystemOneClient`;
  - `ReportUsage` com provider `"openjev"`;
  - marcar `failureCategory = "read_only_model_response_rejected"` nas falhas de resposta.
- [ ] **Step 4: Rodar tudo.** Comando: `dotnet test tests/Cortex.Inference.Tests`. Esperado: PASS, inclusive os testes existentes de `SystemOneFlowCompletionTests`, que provam que a extração não mudou nada.
- [ ] **Step 5: Commit.** `feat(inference): add openjev local typed-decision provider`.

### Task 2: Registrar `openjev` no Cortex

**Files:**
- Modify:
  - `src/Cortex.Evaluation/Contracts/ManifestValidator.cs:130`
  - `src/Cortex.Evaluation/Execution/EvaluationProviderFactory.cs:29-35,70-80`
  - `src/Cortex.Evaluation/Execution/EvaluationHttpHandler.cs:20-29`
  - `src/Cortex.Evaluation/Execution/AttemptLedger.cs:40`
  - `src/Cortex.Evaluation/Comparison/AttemptPriceProjection.cs:22`
  - `src/Cortex.Evaluation/Execution/ProviderObservationParser.cs:21`
  - `src/Cortex.Evaluation/Execution/InstructionProfilePreparation.cs:17`
  - `src/Cortex.Core/ObservedShadowRun.cs:394`
  - qualquer outro ponto achado por `git grep -n '"laya"' src`
- Test: `tests/Cortex.Evaluation.Tests/ManifestTests.cs`, `tests/Cortex.Evaluation.Tests/ProviderObservationTests.cs` e um teste novo em `tests/Cortex.Evaluation.Tests/` para o factory/handler.

**Interfaces:**
- Consumes: `OpenJevClient`, `OpenJevFlowCompletion` (Task 1).
- Valores:
  - Descritor: `["openjev"] = new(EmptySet, false, true, true, true, EmptySet)`. O modelo é livre, porque é o alias do GGUF.
  - Factory: endpoint loopback HTTP(S) com caminho base terminado em `/`. Cria `new OpenJevFlowCompletion(new OpenJevClient(endpoint.Http, endpoint.Endpoint), threshold!.Value, choiceQuestions: …)`.
  - `EvaluationHttpHandler` para `openjev`: aceita POST para `{base}v1/chat/completions` **e** `{base}tokenize`, e os dois entram no journal.
  - Parser: `openjev` é lido como `lmstudio` (chat completions). A resposta do tokenize dá `usage_missing`, que é aceitável.
  - Custo: provedor permitido nas price terms, como `lmstudio`.
  - `InstructionProfilePreparation` e `ObservedShadowRun`: `openjev` é tratado como `jev`/`laya` (só decisão).

- [ ] **Step 1: Escrever os testes que falham.**
  - `[InlineData("openjev", "minicpm5-2b")]` no teste de manifesto typed-only existente.
  - `[InlineData("openjev")]` no `ProviderObservationTests`.
  - `Openjev_endpoint_must_be_loopback`.
  - `Openjev_handler_journals_tokenize_and_chat_and_rejects_other_paths`.
  - `Openjev_profile_creates_typed_completion`.
- [ ] **Step 2: Rodar e ver falhar.** Comando: `dotnet test tests/Cortex.Evaluation.Tests --filter "FullyQualifiedName~openjev|FullyQualifiedName~Openjev"`. Esperado: FAIL.
- [ ] **Step 3: Aplicar os registros listados.**
- [ ] **Step 4: Rodar a seleção offline completa.** Comando: `dotnet test Cortex.slnx --filter "Category!=Postgres&Category!=LocalSocket&Category!=LocalModels&FullyQualifiedName!~DatabaseLifecycleTests"`. Esperado: PASS.
- [ ] **Step 5: Commit.** `feat(evaluation): accept the openjev provider`.

### Task 3: Manifesto, cache e processos locais

**Files:**
- Create: `config/local-models.json`
- Create: `tests/Shared/LocalModels/LocalModelManifest.cs`, `LocalModelCache.cs`, `LocalServerProcess.cs`. Linkar no `Cortex.Inference.Tests.csproj` como já é feito com `SystemOneTestSupport.cs`.
- Create: `tools/start-openjev.ps1`
- Test: `tests/Cortex.Inference.Tests/LocalModelCacheTests.cs`, offline (sem trait).

**Interfaces:**
- Manifesto:
  ```json
  {"llamaCpp":{"tag":"b…","assets":[{"url":"…bin-win-cuda-12…-x64.zip","sha256":"…"},{"url":"…cudart-llama-bin-win-cuda-12…-x64.zip","sha256":"…"}]},
   "gguf":{"qwen3-0.6b":{"repo":"Qwen/Qwen3-0.6B-GGUF","revision":"23749fefcc72300e3a2ad315e1317431b06b590a","file":"Qwen3-0.6B-Q8_0.gguf","sha256":"…"},
           "minicpm5-2b":{"repo":"openbmb/MiniCPM5-2B-GGUF","revision":"2079a22f3beaa4e306449978533478fe0522f4b3","file":"MiniCPM5-2B-Q4_K_M.gguf","sha256":"…"},
           "qwen3.5-4b":{"repo":"bartowski/Qwen_Qwen3.5-4B-GGUF","revision":"4168f45a16a1290d65a4ec0fa312ae917a4c15d6","file":"Qwen_Qwen3.5-4B-Q4_K_M.gguf","sha256":"…"}},
   "laya":{"package":"laya[serve]==…","modelRevision":"…"}}
  ```
  - `llamaCpp.tag`: a release mais recente do `ggml-org/llama.cpp` que tenha os dois assets Windows CUDA 12 (a sua 4090 roda CUDA 12).
  - SHA-256 dos GGUFs: o `lfs.oid` da API do HF (`/api/models/{repo}/tree/{revision}`).
  - SHA-256 dos zips: calcular ao baixar uma vez e conferir com o `digest` da API de releases do GitHub, se ela fornecer.
  - Versão do `laya`: a mais recente no PyPI. Revisão dos pesos: o `sha` atual de `convaiinnovations/laya` no HF.
- C#:
  ```csharp
  internal sealed record LocalModelManifest(…) { internal static LocalModelManifest Load(); }   // finds repo root by walking up to Cortex.slnx
  internal sealed class LocalModelCache(string root)   // root = CORTEX_LOCAL_MODELS_DIR ?? %LOCALAPPDATA%\Cortex\local-models
  {
      internal Task<string> EnsureFileAsync(Uri source, string sha256, string relativePath, CancellationToken token);
      internal Task<string> EnsureLlamaServerAsync(LocalModelManifest manifest, CancellationToken token);   // returns llama-server.exe; llama.cpp\ holds a .tag stamp; mismatch → wipe and re-extract
      internal Task<string> EnsureGgufAsync(LocalModelManifest manifest, string modelKey, CancellationToken token);
      internal Task<string> EnsureLayaAsync(LocalModelManifest manifest, CancellationToken token);   // venv + pinned pip install; returns python.exe
  }
  internal sealed class LocalServerProcess : IAsyncDisposable
  {
      internal static Task<LocalServerProcess> StartAsync(string fileName, IReadOnlyList<string> arguments,
          IReadOnlyDictionary<string, string> environment, Uri readiness, TimeSpan timeout, CancellationToken token);
      internal Uri BaseUri { get; }
      internal static int FreeLoopbackPort();
      // DisposeAsync: Kill(entireProcessTree: true) and wait for exit. On a readiness failure,
      // the stdout/stderr tail goes into the exception message.
  }
  ```
- `llama-server` arguments: `-m <gguf> --alias <key> --host 127.0.0.1 --port <p> -ngl 999 -c 2048`. Readiness: `GET /health` returns 200.
- `tools/start-openjev.ps1 [-Model minicpm5-2b] [-Port 8080]`: reads the same manifest, does the same download+hash into the same cache, starts `llama-server` in the foreground and prints `Endpoint: http://127.0.0.1:<p>/  Model: <key>`.

- [ ] **Step 1: Escrever os testes que falham** em `LocalModelCacheTests`, com um servidor HTTP local (`HttpListener` em porta livre) servindo bytes conhecidos.
  - `Valid_download_is_hash_checked_and_renamed`.
  - `Hash_mismatch_deletes_partial_and_throws` (Review Focus 4).
  - `Existing_valid_file_is_not_downloaded_again`.
  - `Interrupted_partial_is_replaced_on_next_run`.
  - `Disposing_server_kills_process_tree_and_frees_port` (Review Focus 3): usar `pwsh`/`powershell -Command Start-Sleep 600` como processo, descartar e conferir `HasExited` e a porta religável.
- [ ] **Step 2: Rodar e ver falhar.** Comando: `dotnet test tests/Cortex.Inference.Tests --filter "FullyQualifiedName~LocalModelCache"`. Esperado: erro de compilação.
- [ ] **Step 3: Implementar os tipos e preencher `config/local-models.json` com versões e hashes reais.**
- [ ] **Step 4: Rodar os testes e depois o script.**
  - `dotnet test tests/Cortex.Inference.Tests --filter "FullyQualifiedName~LocalModelCache"`: esperado PASS.
  - `powershell -File tools/start-openjev.ps1 -Model qwen3-0.6b`: esperado o servidor imprimir o endpoint. Conferir com `curl http://127.0.0.1:<p>/health` e encerrar.
- [ ] **Step 5: Commit.** `test: add pinned local model cache and server harness`.

### Task 4: Integração real do OpenJev

**Files:**
- Test: `tests/Cortex.Inference.Tests/LocalModels/OpenJevLocalModelTests.cs`

**Interfaces:**
- Consumes: Task 1 (`ProviderFlowCompletion` com `["openjev"] = OpenJevFlowCompletion`) e Task 3 (cache, processo).
- Casos, com o texto exato da demo:
  - `account`. State: `A customer says a password reset succeeded, but every login attempt still returns ‘account locked’. Two unlock emails were requested and neither arrived.` Pergunta (`request.System`): `Which queue should handle this request?`. Enum: `["Account access support","Billing support","Close as resolved"]`. Esperado: `Account access support`.
  - `email`. State: `An email claims to be from the payroll team and says the recipient’s salary payment will be suspended today. It comes from payroll-review@outlook.com and links to a non-company sign-in page asking for a password and verification code.` Pergunta: `How should this email be classified?`. Enum: `["Legitimate","Spam","Phishing"]`. Esperado: `Phishing`.
  - `boolean`. State: o mesmo do `email`. Campo booleano `asks_for_credentials` com descrição `The message asks the recipient to enter a password or verification code.`. Limiar 0.5. Esperado: `true`.

- [ ] **Step 1: Escrever** `[Theory][Trait("Category","LocalModels")]` com `[InlineData("qwen3-0.6b", false)]`, `[InlineData("minicpm5-2b", true)]` e `[InlineData("qwen3.5-4b", true)]` (o segundo argumento é `requireCorrect`). Para cada modelo, garantir o `llama-server` e o GGUF, subir o servidor, rodar os três casos por `ProviderFlowCompletion`, capturar `observeDecision` e verificar:
  - todas as opções presentes;
  - probabilidades finitas, com soma 1 ± 1e-6;
  - escolha = argmax;
  - quando `requireCorrect`, a escolha é a esperada; senão, só escrever a escolha em `TestContext.Current.TestOutputHelper`.
- [ ] **Step 2: Escrever `Label_bias_does_not_change_label_distribution`** (MiniCPM5 2B, caso `email`). Chamar `OpenJevClient.DecideAsync` com `applyLabelBias: true` e `false`. Se sem bias algum rótulo faltar no top 20, o teste falha com mensagem que diz isso. Senão, as probabilidades batem com tolerância 1e-4. Divergência é um achado a reportar à Helena; não ajustar.
- [ ] **Step 3: Rodar.** Comando: `dotnet test tests/Cortex.Inference.Tests --filter "Category=LocalModels&FullyQualifiedName~OpenJev"`. Esperado: PASS. Na primeira execução baixa cerca de 5,2 GB. Se um caso semântico falhar no 2B ou no 4B, **parar e reportar**, sem trocar o caso.
- [ ] **Step 4: Commit.** `test(inference): run openjev models locally end to end`.

### Task 5: Integração real do Laya

**Files:**
- Test: `tests/Cortex.Inference.Tests/LocalModels/LayaLocalModelTests.cs`
- Modify: `tests/Shared/LocalModels/LocalModelCache.cs`, se a verificação abaixo exigir.

**Interfaces:**
- Consumes: Task 3 (`EnsureLayaAsync`, `LocalServerProcess`), `SystemOneFlowCompletion` existente com provider `laya` e `SystemOneProbabilityPolicy.LayaFourDecimals`.

- [ ] **Step 1: Verificação (sem teste ainda).**
  - Rodar `EnsureLayaAsync` e confirmar `torch.cuda.is_available()` no venv.
  - Ler o README do GitHub do Laya para saber como mudar a porta. Se não houver opção, rodar o app ASGI via `uvicorn` na porta livre.
  - Subir com `LAYA_DEVICE=cuda LAYA_PRELOAD=1` e mandar um `curl` com o caso `email` para `english`, `multilingual` e `typed-decisions`.
  - Confirmar que as três respostas passam no `SystemOneClient` estrito.
  - Se qualquer coisa não se confirmar (Windows, CUDA, roteamento por `model`, formato do `typed-decisions`), **parar e reportar à Helena** antes de mudar o cliente.
- [ ] **Step 2: Escrever o `[Theory][Trait("Category","LocalModels")]`** para os modelos `english`, `multilingual` e `typed-decisions`, com os mesmos três casos da Task 4 pelo provider `laya`, limiar 0.5. Verificar: resultado aceito pela validação estrita, escolhas válidas e usage presente. A correção semântica só é registrada no output, porque a spec não exige.
- [ ] **Step 3: Rodar.** Comando: `dotnet test tests/Cortex.Inference.Tests --filter "Category=LocalModels&FullyQualifiedName~Laya"`. Esperado: PASS.
- [ ] **Step 4: Commit.** `test(inference): run laya checkpoints locally end to end`.

### Task 6: Documentação

**Files:**
- Create: `docs/local-decision-models.md`. Cobre:
  - configurar `openjev` (endpoint base, alias = model, limiar) e `laya`;
  - `tools/start-openjev.ps1`;
  - `dotnet test --filter Category=LocalModels`;
  - local do cache e `CORTEX_LOCAL_MODELS_DIR`;
  - o resultado observado da Task 4 (escolhas e probabilidades por modelo).
- Modify: `README.md`, na seleção offline: acrescentar `&Category!=LocalModels` e um link para o doc novo.

- [ ] **Step 1: Escrever o doc e ajustar o README.**
- [ ] **Step 2: Rodar a seleção offline do README.** Esperado: PASS, sem nenhum teste `LocalModels` executado.
- [ ] **Step 3: Commit.** `docs: describe local openjev and laya decision models`.
