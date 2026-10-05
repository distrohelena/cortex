# Temporal Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for the recommended inline execution, or superpowers:subagent-driven-development only if the user chooses delegation. Execute task-by-task; steps use checkbox (`- [ ]`) syntax. No independent review is authorized by this plan.

**Goal:** Migrar a administracao das sessoes e execucoes do Cortex para Temporal e entregar o node `humanFeedback` com recuperacao apos restart, preservando semantica e autoridade dos efeitos.

**Architecture:** `Cortex.Core` fornece kernel incremental, contratos neutros e persistencia privada. `Cortex.Temporal` fornece coordenador por sessao, Workflow filho por batch e Activities que executam um node operacional. O host Filo integra o backend, apresenta feedback e seleciona a instalacao autorizada; o cutover real ocorre depois da verificacao isolada.

**Tech Stack:** .NET 10, C#, System.Text.Json, `Temporalio` 1.20.0, xUnit v3 nas versoes ja usadas no Cortex; testes frontend com `node --test`.

**Spec:** [Desenho aprovado](../specs/2026-10-05-temporal-runtime-design.md).

## Global Constraints

- `Cortex.Core` nao referencia `Temporalio`; o editor `flow-graph` continua generico.
- `humanFeedback`, block version 1: pergunta obrigatoria; input `context`: String opcional; output `feedback`: String; controle `run`, saidas `then` e `timeout`.
- Timeout humano e opcional, inteiro positivo em segundos; quando configurado, `timeout` precisa estar conectado. Cancelamento termina a execucao.
- IDs de execucao, batch, pedido e passo permanecem estaveis em retry/restart; nao dependem de Temporal RunId ou numero da tentativa.
- No maximo um batch operacional ativo por sessao; outras sessoes podem progredir enquanto uma espera feedback.
- Default de backlog: 256 batches, faixa de 1 a 4096; buffer aberto reserva vaga; duplicata ja admitida nao consome vaga nem watermark novo.
- Historico preserva os limites existentes: 10000 mensagens e 1000000 caracteres. Nao mudar configuracoes de modelos, limites ou envio.
- Store de producao inicial e privado, duravel, local a instalacao, com exclusao mutua. Worker substituto acessa o mesmo store; nao prometer failover entre maquinas.
- Temporal carrega refs/digests e controle; conversa, prompt, resposta e texto de feedback permanecem no store privado.
- Artefatos idempotentes/passos puros: ate cinco tentativas, backoff de 1 a 30 segundos. Inferencia paga/envio: uma tentativa por default; timeouts positivos e finitos vindos do host.
- Resultados indeterminados nao autorizam reenviar. Bindings e autorizacao atual sao revalidados nas Activities; feedback nao concede autoridade.
- Cutover importa somente contexto ocioso e reconciliado; nao reexecuta batches antigos nem altera cursor/epoca sem evidencia.
- Preservar alteracoes existentes de Personas, `Cortex.slnx`, artefatos congelados e trabalho alheio. Commits usam somente os caminhos da tarefa.
- Execucao recomendada: inline no checkout atual `C:/dev/cortex`. O plano inclui alteracoes especificadas em `C:/dev/filo`; sua permissao de filesystem deve ser resolvida antes dessas escritas. Nao iniciar servicos/conversas reais para verificar codigo.
- Nenhum subagente ou revisao independente e iniciado por esta aprovacao. Delegacao depende da escolha do usuario; revisao independente requer autorizacao separada, conforme suas instrucoes.

## Review Focus

1. Resposta de Activity perdida depois de um envio confirmado: recuperar recibo, historico e transicao sem reenviar. Testes: tarefas 3, 10 e 13.
2. Atualizacao de projeto durante buffer/feedback aberto: conservar revisao capturada e rejeitar resposta ao pedido errado. Testes: tarefas 5, 8 e 9.
3. Falha entre registro de admissao e resposta ao cliente: repetir sem perder input nem duplicar batch, mesmo apos Continue-As-New. Testes: tarefas 2 e 9.
4. Token de limite valido em RAM, mas sem evidencia duravel ou ja expirado na retomada: rejeitar antes de inferencia/envio. Testes: tarefas 3 e 13.
5. Backlog cheio com buffer reservado e timeout simultaneo a feedback: resolver uma vez, fechar buffer sem perder sua vaga, nao consumir cursor de input rejeitado. Testes: tarefas 8 e 9.

## Organizacao e ordem

As tarefas sao dependentes: contratos -> store/evidencias -> kernel -> driver ->
Workflows -> sessao -> consumidores. Um unico plano conserva os nomes dessas
interfaces e a verificacao de paridade; host/cutover ficam em etapas distintas,
sem declarar a migracao completa depois de apenas adicionar o SDK.

| Unidade | Arquivos principais e responsabilidade |
| --- | --- |
| Contratos | `src/Cortex.Core/Flows/Durable/DurableContracts.cs`, `DurableIdentity.cs`, `ExecutionSnapshotCodec.cs` |
| Store | `Flows/Durable/IExecutionArtifactStore.cs`, `FileExecutionArtifactStore.cs`, `DurableSessionLedger.cs` |
| Evidencia | `Boundaries/BoundaryExecutionEvidence.cs`, `Outbound/OutboundExecutionEvidence.cs` |
| Kernel | `Flows/Execution/FlowExecutionKernel.cs`, `FlowExecutionState.cs`, `FlowExecutionServices.cs`; adaptar `TypedFlowRunner.cs` |
| Feedback | `Flows/HumanFeedbackNode.cs`, `Flows/Durable/FeedbackContracts.cs`; compiladores/catalogo |
| Driver/backend | `Flows/Durable/FlowExecutionDriver.cs`, `ExecutionHostBinding.cs`, `Sessions/ISessionExecutionBackend.cs`, `LocalSessionExecutionBackend.cs` |
| Temporal | novo `src/Cortex.Temporal`, separado em `Workflows`, `Activities`, `Client`, `Hosting` |
| Testes | `tests/Cortex.Core.Tests/Durable`, novo `tests/Cortex.Temporal.Tests`, `tests/architecture/temporal-module.test.mjs` |
| Host | `C:/dev/filo/src/Filomena.App/Temporal`, endpoints e controles do Playground |
| Cutover | `Flows/Durable/SessionCutover.cs`, `docs/temporal-runtime.md`, `docs/temporal-cutover.md` |

Pastas abreviadas da tabela sao relativas a `src/Cortex.Core`. As listas de cada
tarefa abaixo definem seus caminhos completos e ownership.

## Tipos compartilhados entre tarefas

A tarefa 1 implementa estes DTOs publicos no namespace
`Cortex.Core.Flows.Durable`; o restante usa-os sem redefinir identidades.

```csharp
public sealed record ArtifactScope(string InstallationId, SessionKey Session,
    string ExecutionId, string ProjectRevision);
public sealed record ArtifactRef(int Version, ArtifactScope Scope, string Key, string Sha256);
public sealed record StepKey(ArtifactScope Scope, string NodeId, long Ordinal);
public enum ExecutionStatus { Ready, WaitingForHuman, Completed, Failed, Cancelled, ReconciliationRequired }
public enum StepEffect { Pure, Artifact, Inference, Memory, Boundary, Send, Feedback }
public sealed record FeedbackDescriptor(string RequestId, ArtifactRef RequestRef,
    string RequestDigest, string NodeId, long Ordinal, int? TimeoutSeconds);
public sealed record ExecutionDescriptor(ArtifactScope Scope, ArtifactRef SnapshotRef,
    string? NodeId, long Ordinal, ExecutionStatus Status, StepEffect Effect,
    FeedbackDescriptor? Feedback, ArtifactRef? OutcomeRef, string? Diagnostic);
public sealed record ExecutionStart(ArtifactScope Scope, string BatchId, ArtifactRef ProjectRef,
    ArtifactRef BatchRef, string StateId, long StateVersion, SessionEntryKind EntryKind);
public sealed record StepCommand(StepKey Key, ArtifactRef SnapshotRef);
public sealed record ArtifactPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record PageRequest(string? Cursor = null, int Limit = 100);
public sealed record FeedbackSubmission(string OperationId, ArtifactScope Scope,
    string RequestId, string RequestDigest, ArtifactRef ResponseRef, string AuthenticatedActorRef);
public sealed record SessionImportRequest(string CutoverId, string InstallationId,
    SessionSnapshot Snapshot, IReadOnlyList<SessionHistoryEntry> History,
    IReadOnlyList<string> SeenProviderIds, string SourceEpoch, long SourceCursor,
    string JournalFingerprint);
```

O snapshot privado V1 contem scope, BatchId, ProjectRef, BatchRef, StateId,
StateVersion, EntryKind, cursor/ordinal, status, steps, valores por PortRef,
outputs por node/port, mapa de referencias, evidencias de memoria/limites/envio,
SendAttempted, ConfirmedSend e TargetStateId. Cada valor grava schema JSON e
value; reconstituir tipo com `FlowType.ParseJsonSchema` e verificar
`FlowDomain.Check`. O codec nao serializa `TypedFlow`, adaptadores ou delegates.

## Tarefa 1: Identidades, snapshots e contratos duraveis

**Files:** criar os quatro arquivos de contratos/store-interface da tabela;
criar `tests/Cortex.Core.Tests/Durable/DurableIdentityTests.cs` e
`ExecutionSnapshotCodecTests.cs`; adicionar friend assembly
`Cortex.Core.Tests` em `src/Cortex.Core/Cortex.Core.csproj` para testes internos.

**Interfaces:** produzir os DTOs acima e `DurableIdentity.SessionWorkflowId(string installationId, SessionKey session)`,
`ExecutionWorkflowId(ArtifactScope scope, string batchId)`, `OperationId(StepKey key)` e
`FeedbackRequestId(StepKey key)`, todos retornando string.
Tambem `BatchId(string installationId, SessionKey session, string projectRevision, long batchOrdinal)`
e `ExecutionId(string installationId, SessionKey session, string batchId, string purpose)`;
purpose distingue normal/review/preview, com ordinal duravel para cada execucao.
Codec interno:
`JsonElement Encode(ExecutionSnapshotV1 state)` e
`ExecutionSnapshotV1 Decode(JsonElement value, ArtifactScope expectedScope)`.
`IExecutionArtifactStore`: `ValueTask<ArtifactRef> PutOnceAsync(ArtifactScope scope, string key, JsonElement payload, CancellationToken token)`;
`ValueTask<JsonElement> ReadAsync(ArtifactRef reference, CancellationToken token)`;
`ValueTask<ArtifactRef?> FindAsync(ArtifactScope scope, string key, CancellationToken token)`.

- [x] Escrever `IdsIncludeEverySessionDimension` (mudar qualquer um dos cinco campos ou instalacao muda o ID), `OrdinalAndRevisionChangeOperationId` e `TypeAndValueRoundTrip` (enum, nullable, arrays, dominio e referencias preservados). `WrongScopeAndUnknownSnapshotVersionFail` deve rejeitar ambos, sem deserializacao permissiva.
- [x] Rodar `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter "FullyQualifiedName~DurableIdentityTests|FullyQualifiedName~ExecutionSnapshotCodecTests"`; esperar falha pelas APIs ausentes.
- [x] Implementar canonizacao com campos explicitamente ordenados, SHA-256 e versao de protocolo 1. Validar campos vazios/controle, chaves, ordinais negativos, limite de pagina de 1 a 1000 e scopes divergentes. Clonar JsonElements na fronteira.
- [x] Repetir o comando; esperar todos os casos passando e byte roundtrip estavel para o mesmo snapshot.
- [x] Commit delimitado: `feat(core): define durable execution identities and snapshots`.

## Tarefa 2: Store privado e ledger de sessao

**Files:** criar `src/Cortex.Core/Flows/Durable/FileExecutionArtifactStore.cs`,
`DurableSessionLedger.cs`, `DurableSessionLedgerContracts.cs`;
criar `tests/Cortex.Core.Tests/Durable/FileArtifactStoreTests.cs`, `SessionLedgerTests.cs`.

**Interfaces:** consumir tarefa 1. `FileExecutionArtifactStore(string directory, string installationId)` implementa store e `IAsyncDisposable`.
`ISessionLedger` em contracts: `ValueTask<LedgerAdmission> AdmitAsync(LedgerAdmissionRequest request, CancellationToken token)`;
`ValueTask<LedgerAdmission?> FindAdmissionAsync(string installationId, SessionKey session, string inputKey, CancellationToken token)`;
`ValueTask<ArtifactPage<ArtifactRef>> ReadPageAsync(string installationId, SessionKey session, string collection, PageRequest page, CancellationToken token)`;
`ValueTask RecordOnceAsync(LedgerRecord record, CancellationToken token)`.
`LedgerAdmissionRequest` grava instalacao/sessao, inputKey, inputRef, digest, batchId, executionId e proposedWatermark; `LedgerAdmission` devolve status/watermark/identidades anteriores. `LedgerRecord` usa collection, operationId e ref. Collections permitidas: history, outcomes, traces, feedback, admissions.

- [x] Escrever `ReopenReturnsIdenticalArtifact`, `SameKeyDifferentPayloadFails`, `WrongScopeAndDigestFail`, `TraversalAndForeignInstallationFail`, `SecondWriterCannotOpen`, `IncompleteTemporaryWriteDoesNotReplaceCommittedArtifact`. Para ledger: `LostAdmissionResponseReplaysOriginalReceipt`, `ConflictingInputIdFails`, `DuplicateDoesNotIncrementWatermark`, `StablePaginationHasNoDuplicateOrMissingItem` com 205 refs em paginas 100/100/5.
- [x] Rodar `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter "FullyQualifiedName~FileArtifactStoreTests|FullyQualifiedName~SessionLedgerTests"`; esperar falha das APIs novas.
- [x] Implementar gravacao atomica por arquivo temporario, flush duravel e rename; chaves viram nomes por hash, nunca caminhos fornecidos pelo caller. Registro de admissao e seu indice sao uma operacao recuperavel idempotente. Truncamento/corrupcao committed falha explicitamente. Preservar artefatos de execucoes abertas e indices de dedupe; exclusao so via politica explicita do host.
- [x] Repetir testes, incluindo fechar/reabrir objetos do store e subprocesso concorrente para exclusao. Esperar recuperacao sem dependencia de dicionario anterior.
- [x] Commit: `feat(core): persist scoped execution artifacts and session receipts`.

## Tarefa 3: Evidencia confiavel recuperavel

**Files:** criar `src/Cortex.Core/Boundaries/BoundaryExecutionEvidence.cs`,
`src/Cortex.Core/Outbound/OutboundExecutionEvidence.cs`; modificar
`BoundaryExecutionRegistry.cs`, `Outbound/ShadowOutboundHost.cs`, `Outbound/OutboundJournal.cs`;
testar em `tests/Cortex.Core.Tests/Durable/TrustedEvidenceTests.cs`, `OutboundEvidenceRecoveryTests.cs`.

**Interfaces:** APIs internas `BoundaryExecutionEvidence CaptureEvidence()` e
`BoundaryExecutionRegistry RestoreEvidence(string conversationNamespace, BoundaryExecutionEvidence evidence, DateTimeOffset now)`;
`OutboundExecutionEvidence CaptureEvidence(IReadOnlyDictionary<PortRef, TypedValue> values)` e
`OutboundExecution RestoreExecution(ExecutionHostBinding binding, OutboundExecutionEvidence evidence, IReadOnlyDictionary<PortRef, TypedValue> values)`.
Criar `ExecutionHostBinding` em `Flows/Durable/ExecutionHostBinding.cs`: sessao/revisao,
`ISessionFlowCompletionFactory`, `IMemoryReader`, `ShadowOutboundHost?`, `TimeProvider`,
timeouts de node por StepEffect e callbacks `SessionInstructionOutput`/`SessionNodeVisit` existentes.
Adicionar callback host-only `Func<FeedbackSubmission, CancellationToken, Task> AuthorizeFeedbackAsync`:
ausencia rejeita; verifica atestado privado da resposta e direitos atuais. ActorRef recebido
nao constitui prova de autenticacao.
Binding e objeto somente do host, nunca serializado. Evidencia contem snapshots/visuais e handles originais, namespace e fatos de geracao/enforcement/recibo; nao concede autoridade por si.

- [x] Escrever `MemoryAndBoundaryHandlesSurviveRegistryRecreation`, `ForgedVisualForeignNamespaceAndExpiredSourceFail`, `RevokedHostDoesNotRegainSendingAuthority`. `ReceiptWithoutNodeCheckpointRecoversSameProviderId` deve produzir um unico PendingSpeechCommit; `IndeterminateAttemptNeverCallsTransportAgain` deve manter calls=1 depois de recriar host e tentar recuperar.
- [x] Rodar `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter "FullyQualifiedName~TrustedEvidenceTests|FullyQualifiedName~OutboundEvidenceRecoveryTests"`; confirmar falhas ligadas a evidencia ausente.
- [x] Implementar captura/restauracao apenas no caminho interno confiavel ligado ao store das tarefas 1/2. Revalidar digest, scope, original visual, sujeito canonico e validade. Restaurar journal a partir dos estados existentes, preservando tentativa/ExecutionId e status indeterminado.
- [x] Rodar o filtro e `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter "FullyQualifiedName~Boundaries|FullyQualifiedName~Outbound"`; esperar paridade dos controles existentes, sem transporte real.
- [x] Commit: `feat(core): restore trusted execution evidence without reissuing effects`.

## Tarefa 4: Kernel incremental compartilhado com o runner local

**Files:** criar `src/Cortex.Core/Flows/Execution/FlowExecutionState.cs`,
`FlowExecutionKernel.cs`, `FlowExecutionServices.cs`; modificar `Flows/TypedFlowRunner.cs`;
criar `tests/Cortex.Core.Tests/Durable/IncrementalKernelTests.cs`, `RunnerParityTests.cs`.

**Interfaces:** kernel interno `FlowExecutionState Start(TypedFlow flow, JsonElement message, ExecutionStart identity, IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>> runtimeOutputs)`;
`Task<KernelStepResult> AdvanceAsync(TypedFlow flow, FlowExecutionState state, FlowExecutionServices services, CancellationToken token)`.
`KernelStepResult` identifica completed operational node, exit, next state, StepEffect e trace events; inclui termino/erro/transicao sem criar nova identidade.
`FlowExecutionServices` usa binding da tarefa 3 e registries restaurados. Conversoes state/snapshot V1 preservam todos os campos definidos na tarefa 1.

- [x] Escrever `OneAdvanceExecutesAtMostOneOperationalNode`, `PromptThenBranchResumePreservesOutputs`, `RestartKeepsDomainReferencesAndConfirmedSend`, `OptionalInputsAndTextStructuredCorrectionAreUnchanged`.
  `RunnerAndIncrementalDriverProduceSameOutcome` cobre dm/group, accumulate/history, memory, interpretBoundaries, prompt normal/enforcement, property/count, branch/switch, send e exit; comparar outputs, exits, target state e numero de chamadas.
- [x] Rodar `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter "FullyQualifiedName~IncrementalKernelTests|FullyQualifiedName~RunnerParityTests"`; observar falha por nao existir advance independente.
- [x] Extrair sem reescrever as regras do runner. `TypedFlowRunner.RunAsync`/`RunSessionAsync` dirigem o mesmo kernel ate o fim. Preserve correctionRetry de validacao existente: a politica de uma tentativa Temporal nao remove a correcao configurada dentro do node. Um Advance puro percorre no maximo 64 projections/branch/switch e retorna ANTES do proximo efeito Inference/Memory/Boundary/Send/Feedback; o descritor identifica esse efeito para escolher a policy correta. Cada node ainda gera trace proprio. Testar `PureAdvanceStopsBeforePaidEffect`: completion calls=0 ate um Advance separado de Inference.
- [x] Rodar testes novos e `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter "FullyQualifiedName~Flows|FullyQualifiedName~Outbound|FullyQualifiedName~Boundaries"`; esperar paridade e nenhuma mudanca nas fixtures congeladas.
- [x] Commit: `refactor(core): share an incremental execution kernel with the local runner`.

## Tarefa 5: Contrato e compilacao do node de feedback humano

**Files:** criar `src/Cortex.Core/Flows/HumanFeedbackNode.cs`,
`Flows/Durable/FeedbackContracts.cs`; modificar `Flows/NodeCatalog.cs`, `TypedFlowCompiler.cs`,
`FlowValidator.cs`, `Sessions/SessionFlowProjectCompiler.cs`, kernel da tarefa 4;
testar `tests/Cortex.Core.Tests/Durable/HumanFeedbackNodeTests.cs`.

**Interfaces:** `HumanFeedbackSettings(string Question, int? TimeoutSeconds)`;
`HumanFeedbackSettings Read(JsonElement config)` e `NodeDefinitionResolution Resolve(JsonElement config)`.
Configuracao wire: `settings.question`, `settings.timeoutSeconds`; no catalogo V1, resolver suporta o config normalizado desse mesmo contrato.
`FeedbackRequestV1(StepKey Key, string RequestId, string Question, string? Context, string RequestDigest)`;
`FeedbackSubmission` consome o DTO da tarefa 1;
`FeedbackResolution(FeedbackDescriptor Request, string Status, ArtifactRef? ResponseRef)`;
`FeedbackReceipt(string OperationId, string RequestId, string Status, string? Diagnostic)`.
Status de resolucao: answered, timeout, cancelled; demais status sao rejeicoes do comando.

- [x] Escrever `FeedbackCompilesWithOptionalTextContext`, `QuestionAndTimeoutAreValidated`, `ConfiguredTimeoutRequiresControlEdge`, `TimeoutPathCannotConsumeUnavailableFeedback`, `FeedbackPausesBeforeDownstreamPrompt`. Verificar que question vazio, timeout 0/fracionario, input Number e referencia ao feedback no caminho timeout sao rejeitados.
- [x] Rodar `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter FullyQualifiedName~HumanFeedbackNodeTests`; esperar kind ainda nao suportado.
- [x] Registrar kind e portas. Manter controle `run` do catalogo; grafo tipado V3 continua usando o target `exec` existente. O kernel devolve pedido, sem chamar uma pessoa. Harness local sem handler devolve diagnostico `human-feedback-backend-required`, sem avancar para then.
- [x] Repetir filtro e testes de compilador/validator existentes. Confirmar fingerprint do pedido inclui pergunta, contexto e StepKey; mudanca de revisao ou texto muda o digest.
- [x] Commit: `feat(core): compile human feedback as an explicit execution wait`.

## Tarefa 6: Driver duravel e contrato assincrono de backend

**Files:** criar `src/Cortex.Core/Flows/Durable/FlowExecutionDriver.cs`,
`IExecutionHostBindings.cs`, `src/Cortex.Core/Flows/Sessions/ISessionExecutionBackend.cs`,
`LocalSessionExecutionBackend.cs`, `SessionBackendContracts.cs`;
testar `tests/Cortex.Core.Tests/Durable/DurableDriverTests.cs`, `LocalBackendCompatibilityTests.cs`.

**Interfaces:** `IExecutionHostBindings.ResolveAsync(ArtifactScope scope, CancellationToken token): ValueTask<ExecutionHostBinding>`;
`IFlowExecutionDriver` e seu `FlowExecutionDriver`:
`Task<ExecutionDescriptor> InitializeAsync(ExecutionStart start, CancellationToken token)`;
`Task<ExecutionDescriptor> AdvanceAsync(StepCommand command, CancellationToken token)`;
`Task<ExecutionDescriptor?> RecoverAsync(StepKey key, CancellationToken token)`;
`Task ValidateFeedbackAsync(FeedbackSubmission submission, CancellationToken token)`;
`Task<ExecutionDescriptor> ResolveFeedbackAsync(StepCommand command, FeedbackResolution resolution, CancellationToken token)`.

`ISessionExecutionBackend : IAsyncDisposable`: `Task<SessionFlowProjectSnapshot> PublishAsync(SessionFlowProject project, CancellationToken token)`;
`Task<SessionAdmission> ReceiveAsync(SessionKey session, SessionMessageEnvelope message, CancellationToken token)`;
`Task<SessionSnapshot?> ReadSessionAsync(SessionKey session, CancellationToken token)`;
`Task<ArtifactPage<SessionHistoryEntry>> ReadHistoryAsync(SessionKey session, PageRequest page, CancellationToken token)`;
outcomes/traces usam o mesmo padrao com SessionFlowOutcome/SessionTraceEvent;
`Task WaitForIdleAsync(SessionKey session, CancellationToken token)`;
`Task CancelExecutionAsync(SessionKey session, string executionId, CancellationToken token)`;
`Task<ArtifactPage<FeedbackDescriptor>> ReadFeedbackAsync(SessionKey session, PageRequest page, CancellationToken token)`;
`Task<FeedbackReceipt> SubmitFeedbackAsync(FeedbackSubmission submission, CancellationToken token)`.
`Task<SessionSnapshot> ImportIdleAsync(SessionImportRequest request, CancellationToken token)`;
local usa RestoreContext existente e rejeita contexto ocupado/projeto divergente.

- [x] Escrever `CommittedStepRetryReturnsSameSnapshotWithoutCompletionCall`, `WrongCursorOrdinalAndScopeAreRejected`, `HostBindingRecheckedForEveryNewStep`, `ResponseCommitLostRecoversDescriptor` e `PrivatePayloadsAreAbsentFromDescriptors`. Backend local: pagina history/outcomes/traces preserva ordem e total; wrappers assincronos nao usam sync-over-async.
- [x] Rodar `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter "FullyQualifiedName~DurableDriverTests|FullyQualifiedName~LocalBackendCompatibilityTests"`; esperar APIs ausentes.
- [x] Implementar drive de uma operacao com snapshot antes/depois e resultado PutOnce por StepKey. Recuperar resultado existente antes de resolver binding ou efetuar novo I/O. Validar feedback com scope/ref e publicar feedback somente answered; cancelled e terminal. Backend local adapta APIs existentes, mantendo suas assinaturas publicas originais para compatibilidade.
- [x] Repetir testes; simular resultado gravado e processo/driver reconstruido. Esperar nenhuma Task de longa duracao no driver para aguardar pessoa.
- [x] Commit: `feat(core): expose durable node driving and async session backends`.

## Tarefa 7: Modulo Temporal, Activities e configuracao do worker

**Files:** criar `src/Cortex.Temporal/Cortex.Temporal.csproj`,
`Activities/ExecutionActivities.cs`, `Activities/SessionActivities.cs`,
`Hosting/TemporalRuntimeOptions.cs`, `Hosting/TemporalRuntimeWorker.cs`, `Hosting/TemporalActivityPolicy.cs`;
criar `tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj`, `ActivityPolicyTests.cs`, `ExecutionActivityTests.cs`;
adicionar projetos a `Cortex.slnx`; criar `tests/architecture/temporal-module.test.mjs`.

**Interfaces:** SDK `Temporalio` fixado em 1.20.0, referencia somente a Core; testes referenciam Core/Temporal.
`ExecutionActivities` encapsula driver da tarefa 6: Initialize, Advance, Recover, ValidateFeedback e ResolveFeedback preservam parametros/retornos EXCETO CancellationToken, que nao e parametro wire. Internamente usam `ActivityExecutionContext.Current.CancellationToken`.
`TemporalRuntimeOptions`: InstallationId, TargetHost, Namespace, WorkflowTaskQueue, EffectTaskQueue, ArtifactDirectory e MaxPendingBatches (256). `Validate()` rejeita campos vazios e backlog fora de 1..4096.
`WorkflowActivityPolicy(string EffectTaskQueue, IReadOnlyDictionary<StepEffect, TimeSpan> StartToCloseTimeouts)` e DTO de controle validado pelo host antes de iniciar.
`TemporalActivityPolicy.For(StepEffect effect, WorkflowActivityPolicy policy)` devolve ActivityOptions;
Pure/Artifact usam ate 5 tentativas, Inference/Memory/Boundary/Send usam 1 por default.
Initialize/Recover/ValidateFeedback/ResolveFeedback usam Artifact; a espera humana nao e Activity.
Todos os timeouts sao finitos e positivos. Workflow nunca recebe ExecutionHostBinding.
`TemporalRuntimeWorker.RunAsync(CancellationToken stopWorker)` registra Activities/Workflows e executa ate shutdown; token de stop nao cancela Workflows.

- [x] Escrever `CoreHasNoTemporalReference`, `OptionsRequireExplicitNamespaceQueuesAndStore`, `PaidAndSendEffectsHaveOneAttempt`, `ArtifactEffectsUseFiveAttemptsWithOneToThirtySecondsBackoff`, `ActivityPassesHostScopeAndCancellation`. Esperar que policies expressem o limite configurado pelo host, sem endpoints Cloud implicitos.
- [x] Rodar `dotnet test tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj --filter "FullyQualifiedName~ActivityPolicyTests|FullyQualifiedName~ExecutionActivityTests"`; inicialmente projeto/APIs inexistentes falham. Preparar restore do SDK somente nesta etapa de implementacao.
- [x] Criar os projetos net10.0, warnings-as-errors e mesmas versoes xUnit do Core. Implementar bridge pequeno: sem logar payload privado e sem capturar servicos no Workflow. Ao adicionar solution entries, conservar o diff preexistente de Personas.
- [x] Repetir filtro e `node --test tests/architecture/temporal-module.test.mjs`; esperar regras de dependencia e policy passando.
- [x] Commit apenas dos arquivos da tarefa: `feat(temporal): add host-bound activities and worker configuration`; nao incluir as alteracoes anteriores da solution no commit.

## Tarefa 8: Workflow de execucao e feedback humano duravel

**Files:** criar `src/Cortex.Temporal/Workflows/FlowExecutionWorkflowV1.cs`,
`Workflows/ExecutionWorkflowContracts.cs`, `Client/TemporalFeedbackClient.cs`;
testar `tests/Cortex.Temporal.Tests/FeedbackWorkflowTests.cs`, `FeedbackRestartTests.cs`;
criar fixture `TemporalTestEnvironment.cs` com servidor isolado.

**Interfaces:** `[WorkflowRun] Task<ExecutionDescriptor> RunAsync(ExecutionWorkflowInput input)`;
input contem ExecutionStart e metadados de politica de Activities, sem dados privados;
`[WorkflowUpdate] Task<FeedbackReceipt> SubmitFeedbackAsync(FeedbackSubmission submission)`;
`[WorkflowQuery] ExecutionDescriptor? Status()`;
`[WorkflowUpdate] Task CancelAsync(string operationId)`.
`TemporalFeedbackClient.SubmitAsync(FeedbackSubmission submission, CancellationToken token): Task<FeedbackReceipt>` obtÃ©m handle pelo ID estavel da execucao e usa UpdateId=OperationId.

- [x] Escrever `WaitingHasNoLongRunningActivity`, `WorkerRestartThenFeedbackKeepsExecutionIdAndPreviousOutputs`, `DuplicateFeedbackReturnsOriginalReceipt`, `ConflictingLateWrongScopeAndWrongDigestFail`, `TimeoutAndFeedbackResolveOnlyOnce`, `CancelDoesNotTakeThenOrTimeout`, `UnauthorizedFeedbackLeavesRequestPending`. Antes da resposta, downstream prompt calls=0; depois, upstream calls=1 e downstream calls=1; uma sessao de controle conclui durante a espera.
- [x] Rodar `dotnet test tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj --filter "FullyQualifiedName~FeedbackWorkflowTests|FullyQualifiedName~FeedbackRestartTests"`; confirmar falta do Workflow. Preparar binario do test server em cache permitido; falha de disponibilidade e reportada, nao convertida em teste skip bem-sucedido.
- [x] Implementar main loop de descritores; para feedback usar `Workflow.WaitConditionAsync` e timer do SDK. Update validator puro verifica shape/identidade/digest/idempotencia. Handler primeiro chama ValidateFeedback Activity sem marcar resolucao; apos await, revalida pedido ativo/digest/timeout e aceita atomicamente a primeira resolucao valida. Texto vem por ref persistida pelo host. Persistir resultado via ResolveFeedback Activity, que revalida autorizacao atual antes de publicar. Consultas sao puras. Shutdown do worker nao chama CancelAsync.
- [x] Rodar time skipping para timeout e servidor persistente com dois workers sucessivos para restart; servidor permanece vivo entre workers. Confirmar que a resposta com timeout do cliente pode ser consultada/repetida com a mesma chave.
- [x] Commit: `feat(temporal): wait durably for authenticated human feedback`.

## Tarefa 9: Coordenador duravel de sessao, batching e rotacao

**Files:** criar `src/Cortex.Core/Flows/Sessions/SessionBatchPolicy.cs`,
`DurableSessionState.cs`; criar `src/Cortex.Temporal/Workflows/SessionWorkflowV1.cs`,
`Workflows/SessionWorkflowContracts.cs`, completar `Activities/SessionActivities.cs`,
criar `Client/TemporalSessionExecutionBackend.cs`;
testar `tests/Cortex.Temporal.Tests/SessionWorkflowTests.cs`, `SessionRotationTests.cs`, `AdmissionCrashTests.cs`.

**Interfaces:** policy pura `SessionBatchPolicy.Admit(DurableSessionState state, AdmissionDescriptor input, DateTimeOffset now): SessionPolicyDecision` e
`CloseBuffer(DurableSessionState state, string bufferId, SessionBatchCloseReason reason, DateTimeOffset now): SessionPolicyDecision`.
Estado guarda installation/session, publishedProjectRef, currentState/version/enteredAt,
watermark, refs de buffers/fila, execucao ativa, configuracao de backlog e
NextBatchOrdinal/ordinais de review e preview, preservados na rotacao.
`SessionWorkflowV1.RunAsync(SessionWorkflowInput input): Task`;
Update `ReceiveAsync(AdmissionDescriptor input): Task<SessionAdmission>`;
Update `PublishAsync(ProjectPublicationDescriptor input): Task<SessionFlowProjectSnapshot>`;
Query `Snapshot(): SessionSnapshot`; Update `CancelExecutionAsync(string executionId): Task`.
Update `ImportIdleAsync(ArtifactRef importRef): Task<SessionSnapshot>` le manifest privado
via Activity; rejeita admissoes, filhos ou buffers existentes e usa CutoverId idempotente.
AdmissionDescriptor contem inputKey/ref/digest, EntryKind, Direction e Timestamp;
ProjectPublicationDescriptor contem ProjectId/Version/Revision/ProjectRef. Messages/projetos completos sao gravados pelo cliente no store antes do Update.
`TemporalSessionExecutionBackend` implementa tarefa 6 e consulta/pagina ledger para historico/resultados/traces.
ImportIdleAsync persiste SessionImportRequest privadamente antes do Update por referencia.

- [x] Escrever `MaxOneChildPerSessionOtherSessionsProgress`, `Backlog256Rejects257WithoutWatermarkChange`, `OpenBufferReservationCanCloseAtCapacity`, `QuietMaxWaitAndCountCloseExactlyOnce`, `OpenBufferKeepsRevisionAndEntryAfterStateChange`, `DuplicateAfterLostReceiptAndRotationKeepsOriginalBatch`, `StaleSendBatchRepinsOrBlocksBeforeEffect`, `HistoryFrontierIncludesConfirmedPredecessorOnly`.
  Rotacao: `ContinueAsNewCarriesWatermarkQueueAndDedupe`, `NoRotationWithActiveChildBufferOrHandler`; wrong EntryKind rejeitado.
- [x] Rodar `dotnet test tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj --filter "FullyQualifiedName~SessionWorkflowTests|FullyQualifiedName~SessionRotationTests|FullyQualifiedName~AdmissionCrashTests"`; esperar falta de coordenacao.
- [x] Extrair as regras de batching/estado dos arquivos Sessions existentes para policy testavel. Serialize admissoes/publicacoes com primitive determinista do SDK; validar dedupe no ledger antes de consumir vaga e confirmar o Update apenas apos seu registro. Nunca considerar aceito somente porque uma ref foi gravada. Criar filho por batch com ID estavel, sem overlap e com cancelamento de dominio explicito.
- [x] Implementar timers SDK e Continue-As-New no main loop em ponto seguro, depois dos handlers. Backpressure nao descarta inputs; pagina traces/outcomes em collections privadas. Executar o filtro e os cenarios equivalentes de `SessionFlowRuntimeTests`/`SessionSliceAcceptanceTests` contra ambos os backends.
- [x] Commit: `feat(temporal): coordinate durable session admission batching and state`.

## Tarefa 10: Inferencia, memoria e envio com recuperacao de efeitos

**Files:** completar `Flows/Durable/ExecutionHostBinding.cs`, `FlowExecutionDriver.cs`,
`IExecutionHostBindings.cs`, `Outbound/OutboundExecutionEvidence.cs`, `Outbound/ShadowOutboundHost.cs`;
criar `src/Cortex.Temporal/Hosting/HostBoundExecutionBindings.cs`,
testar `tests/Cortex.Temporal.Tests/HostBoundEffectTests.cs`, `OutboundCrashRecoveryTests.cs`.

**Interfaces:** `HostBoundExecutionBindings` implementa resolver da tarefa 6,
reutilizando HostBoundSessionCompletionFactory e binding de memoria/transporte;
`ExecutionActivities.RecoverAsync` consulta snapshot e journals por StepKey antes de novo efeito.
`CommitOutcomeAsync(ArtifactRef outcomeRef): Task` em SessionActivities registra history/outcome/transition uma vez, por ExecutionId + ProviderMessageId + StateVersion esperada.

- [x] Escrever `RevokedInferenceGrantAndForeignMemoryScopeFail`, `RecoveredModelOutputIsNotRegenerated`, `ModelCompletedWithoutDurableResultIsExplicitFailure`, `CrashAfterReservationIsIndeterminate`, `CrashAfterReceiptBeforeCheckpointDoesNotResend`, `HistoryCommitResponseLostDoesNotDuplicateSpeechOrTransition`, `KillSwitchAndChangedRouteRemainEffectiveAfterFeedback`.
- [x] Rodar `dotnet test tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj --filter "FullyQualifiedName~HostBoundEffectTests|FullyQualifiedName~OutboundCrashRecoveryTests"`; esperar falta dos caminhos de recuperacao.
- [x] Implementar recuperacao pelo journal e gravacao idempotente de outcome. Workflow captura Activity failure e agenda Recover; se nao ha resultado recuperavel, manter Failed/ReconciliationRequired conforme evidencia. Nao usar retries de Workflow que criem nova identidade/reautorizem o mesmo envio.
- [x] Repetir testes com store/journal reabertos entre cada fronteira de crash e transporte fake. Esperar Calls=1 nos casos de envio confirmado/indeterminado; numero de mensagens no history e de transicoes continua 1 no caso confirmado.
- [x] Commit: `feat(temporal): recover host-bound effects without duplicate dispatch`.

## Tarefa 11: Paridade de revisoes, previews e handoffs

**Files:** criar `src/Cortex.Core/Flows/Sessions/SessionReviewBackendContracts.cs`,
`src/Cortex.Temporal/Workflows/SessionReviewWorkflowV1.cs`, `Client/TemporalSessionReviews.cs`,
`src/Cortex.Core/DurableObservedRun.cs`; modificar caminhos compartilhados em
`Flows/Sessions/SessionParticipationReview.cs`, `SessionResponsePreview.cs`,
`SessionReviewedResponse.cs`, `PendingParticipationReviews.cs`, `ShadowBootstrap.cs` somente para extrair/adaptar regras;
testar `tests/Cortex.Temporal.Tests/SessionReviewParityTests.cs`, `DurableHandoffTests.cs`.

**Interfaces:** `ISessionReviewBackend`:
`Task<SessionParticipationReviewResult> ReviewAsync(SessionKey session, string originalMessageId, ParticipationReviewDefinition definition, CancellationToken token)`;
`Task<SessionResponsePreviewResult> PreviewAsync(SessionKey session, ArtifactRef reviewRef, CancellationToken token)`;
`Task<SessionReviewedResponseResult> ContinueAsync(SessionKey session, ArtifactRef reviewRef, CancellationToken token)`.
ReviewRef e emitida pelo backend, scope-bound e registra respostas completas privately; nao reconstruir CompletedPromptResponses de um report publico.
`DurableObservedRun` recebe ISessionExecutionBackend, ISessionReviewBackend,
ShadowSessionBinding e journal existente. `AcceptAsync(CanonicalShadowInbound input, CancellationToken token): Task<ShadowInboundDecision>`,
`DrainAsync(CancellationToken token): Task`, leituras assincronas delegadas e `DisposeAsync()` fecha o cliente, sem cancelar Workflows implicitamente.

- [x] Escrever `ReviewCutAndPreviewMatchLocalBackend`, `ReviewedPromptResponsesAreReusedAfterRestart`, `StaleReviewOrConfirmedSpeechRejectContinuation`, `InitialInterpretationUsesCapturedHistoryPrefix`, `ShadowCeilingNeverDispatches`, `UnresolvedInboxDoesNotAutoReplayAsNewWork`, `ClientDisposeLeavesPendingFeedbackOpen`.
- [x] Rodar `dotnet test tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj --filter "FullyQualifiedName~SessionReviewParityTests|FullyQualifiedName~DurableHandoffTests"`; confirmar faltas de paridade.
- [x] Reutilizar cut/interpreter e diagnosticos existentes; executar efeitos via driver e refs privadas. Preservar snapshot de revisao, orÃ§amento/horizonte e proibir preview de conceder envio. Adicionar facade async nova para consumidores Temporal, conservando facade local legada durante a migracao.
- [x] Rodar filtro e testes existentes de Observed/Shadow/LiveHandoff/PendingParticipationReviews. Esperar paridade semanticamente comparada sem exigir timings identicos.
- [x] Commit: `feat(temporal): preserve review preview and handoff semantics`.

## Tarefa 12: Consumidor Filo e UI de feedback

**Files (ownership exclusivo Filo):** criar `C:/dev/filo/src/Filomena.App/Temporal/TemporalRuntimeHosting.cs`,
`TemporalHostBindings.cs`, `Playground/HumanFeedbackEndpoints.cs`, `Playground/LocalFeedbackAuthorization.cs`, `Playground/human-feedback.mjs`;
modificar `Filomena.App.csproj`, `LocalPromptPlayground.cs`, `NativeShadowCommand.cs`,
`ManualShadowCommand.cs`, `ContextInterpretationHost.cs`,
`Playground/SessionFlowEndpoints.cs`, `flow-contract.mjs`, `flow-model.mjs`,
`flow-typed-panel.mjs`, `flow.js`, `flow.html`, `flow.css`;
criar testes `tests/Filomena.App.Tests/TemporalHostTests.cs`,
`tests/Filomena.Llm.Tests/HumanFeedbackIntegrationTests.cs`, `tests/frontend/human-feedback.test.mjs` nesse repositorio.

**Interfaces:** injecao da tarefa 6 e DurableObservedRun da tarefa 11.
Rotas sob `/api/flow/typed/sessions/{id}`: `GET /feedback` paginado,
`GET /feedback/{requestId}` e `POST /feedback/{requestId}` com operationId,
requestDigest e texto. Endpoint deriva actor/session da autenticacao do host,
grava ResponseRef e envia FeedbackSubmission; nao aceita autoridade do body.
`ILocalFeedbackAuthorization.AuthorizeAsync(HttpContext context, SessionKey session, CancellationToken token): Task<AuthenticatedFeedbackActor>`;
`AuthenticatedFeedbackActor(string ActorRef)` identifica o operador derivado pelo servidor.
O Playground atual usa loopback sem identidade administrativa: implementar login local
por nonce imprevisivel gerado a cada launch, entregue pelo launcher/console autorizado,
trocado por cookie HttpOnly/SameSite Strict. Mutacoes exigem same-origin; cookie e nonce
nao vao para logs, traces ou Temporal. Identidade confiavel do operador vem da configuracao
do host, nunca do body. Reinicio exige novo login e conserva pedidos duraveis.
TemporalHostBindings verifica atestado privado de ResponseRef com ActorRef/OperationId/digest
e injeta AuthorizeFeedbackAsync. Hosts com autenticacao existente podem substituir o adapter.
Estado/UI mostra waitingForHuman, pergunta/contexto e campo de resposta; pergunta
e timeout ficam no painel do node. Explicit stop de execucao chama cancelamento;
desmontar cliente/worker nao cancela pedido.

- [x] Antes de escrever fora do workspace, resolver permissao para os caminhos listados; respeitar AGENTS aplicavel e preservar o status desse outro repo. Nao copiar journals/conversas para fixtures ou usar a configuracao de producao.
- [x] Escrever testes `FeedbackRouteRejectsForeignSessionOrActor`, `MissingCookieAndCrossOriginAreRejected`, `AuthorizedLocalOperatorCanAnswer`, `PendingRequestSurvivesHarnessReconnect`, `NodePortsAndTimeoutMatchCore`, `FeedbackTextRendersAsTextNotHtml`, `RequestRetryDoesNotResubmitConflict`. UI mostra pedido apos refresh e permite enviar feedback sem esperar um drain concluir; 401 sem login, 409 para conflito, 400 para resposta invalida, 404 para pedido fora de escopo.
- [x] Rodar `dotnet test C:/dev/filo/tests/Filomena.App.Tests/Filomena.App.Tests.csproj --filter FullyQualifiedName~TemporalHostTests`, `dotnet test C:/dev/filo/tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --filter FullyQualifiedName~HumanFeedbackIntegrationTests` e `node --test C:/dev/filo/tests/frontend/human-feedback.test.mjs`; esperar faltas de backend/rotas/node.
- [x] Adicionar referencia a Cortex.Temporal via CortexRoot. Configuracao escolhe exatamente um backend por sessao; opcao Temporal exige endpoint/namespace/store/queues. Alterar Playground/handoffs para async; conservar harness local opt-in de testes. Usar UI/sistema de estilos existentes e skill impeccable na execucao desta tarefa. Mapear/embutir o novo asset no csproj.
- [x] Repetir filtros e testes frontend de flow-session/contract/model. Verificacao HTTP/DOM concluida com servidor isolado, fixtures fake e feedback depois de restart; verificacao visual pendente porque nao ha navegador conectado (cua.listBrowsers retornou []); nenhum provider real e necessario. Confirmar rejeicao sem identidade autenticada e que o operador autorizado consegue concluir o pedido.
- [x] Commit delimitado no repositorio Filo: `feat(app): use Temporal sessions and expose human feedback nodes`.

## Tarefa 13: Recuperacao completa, replay e paridade

**Files:** criar `tests/Cortex.Temporal.Tests/TemporalAcceptanceTests.cs`,
`ReplayCompatibilityTests.cs`, `PayloadPrivacyTests.cs`, `RecoveryFaultMatrixTests.cs`,
fixtures sinteticas em `tests/Cortex.Temporal.Tests/Fixtures`; atualizar documentacao de execucao em `docs/temporal-runtime.md`.

**Interfaces:** APIs das tarefas 6-12; testes usam binders/transporte/completion
fake com contadores duraveis e store isolado. Historicos exportados para replay
contem somente metadados e refs sinteticas, sem dados reais ou credenciais.

- [x] Escrever `PauseStopWorkerRestartAnswerComplete` com grafo prompt -> feedback -> prompt -> exit; assert upstream=1, downstream=1, mesma ExecutionId/revisao e transicao=1. Matrix cobre crash antes/depois de reserva, dispatch, receipt, snapshot e history commit. `TemporalHistoryContainsNoPrivatePayload` procura marcadores de pergunta, contexto, feedback, prompt e conversa completos e exige ausencia.
- [x] Rodar `dotnet test tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj --filter "FullyQualifiedName~TemporalAcceptanceTests|FullyQualifiedName~ReplayCompatibilityTests|FullyQualifiedName~PayloadPrivacyTests|FullyQualifiedName~RecoveryFaultMatrixTests"`; primeiro demonstrar falha para cada caso novo ainda nao satisfeito.
- [x] Corrigir lacunas dos componentes donos, sem afrouxar invariantes. Exportar/replay historico V1 com WorkflowReplayer do SDK; manter um historico golden sintetico para release posterior. Documentar tempo de espera duravel, parada de worker versus cancelamento, dependencia do store local e limites do retry.
- [x] Rodar `dotnet build Cortex.slnx`, `dotnet test Cortex.slnx --filter "Category!=Postgres&Category!=LocalSocket&FullyQualifiedName!~DatabaseLifecycleTests"`, `node --test tests/architecture/*.test.mjs packages/flow-graph/tests/*.test.mjs`; testes Temporal disponiveis executam e nao sao silenciosamente omitidos. Registrar separadamente indisponibilidade de servico/binario e skips live existentes; nao alegar recuperacao somente por suite de kernel passar.
- [x] Repetir suites Filo offline e frontend afetadas usando os comandos do seu README. Registrar evidencia por backend e verificador de ausencia de dados privados; conferir `git diff --check` nos dois repositorios.
- [x] Commit dos testes/docs e correcoes por ownership: `test(temporal): prove replay restart and effect recovery parity`.

## Tarefa 14: Importacao ociosa e runbook de cutover

**Files:** criar `src/Cortex.Core/Flows/Durable/SessionCutover.cs`,
`tests/Cortex.Core.Tests/Durable/SessionCutoverTests.cs`, `docs/temporal-cutover.md`;
criar `C:/dev/filo/src/Filomena.App/Temporal/TemporalCutoverCommand.cs` e registrar em `C:/dev/filo/src/Filomena.App/Program.cs`;
testar `C:/dev/filo/tests/Filomena.App.Tests/TemporalCutoverTests.cs`.

**Interfaces:** `SessionCutoverManifest` grava source backend, installation/session,
ProjectId/Version/Revision, CurrentState/StateVersion, watermark, source epoch/cursor,
historyRef, dedupeRef, journal fingerprint e identificador estavel de cutover.
`SessionCutover.Validate(SessionCutoverManifest manifest, SessionSnapshot source,
bool hasUnresolvedInbox, bool hasUnresolvedOutbound): void`;
`ImportAsync(SessionCutoverManifest manifest, IExecutionArtifactStore artifacts, ISessionExecutionBackend target, CancellationToken token): Task<SessionSnapshot>` carrega history/dedupe privados e chama ImportIdleAsync da tarefa 6.
Command suporta `--dry-run` obrigatorio por default; execucao real exige configuracao
concreta e autorizacao operacional. Importacao e idempotente pela identidade do
cutover e recusa destino com estado divergente.

- [ ] Escrever `PendingOrUnreconciledSourceCannotImport`, `ImportPreservesStateHistoryWatermarkAndDedupe`, `SameManifestRetryDoesNotRecreateWork`, `ChangedJournalFingerprintOrDestinationStateRejectsImport`, `RollbackAfterNewReceiptRequiresReconciliation`.
- [ ] Rodar `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter FullyQualifiedName~SessionCutoverTests` e o teste Filo indicado; esperar importacao ausente.
- [ ] Implementar apenas contexto ocioso, sem continuacao de Task nem replay de inputs ja concluidos. Runbook: identificar instalacao/endpoint/namespace/queues, pausar admissao no dono atual, drenar/reconciliar, capturar manifest, dry-run, importar, conferir scopes/cursors/recibos, trocar dono e retomar. Preservar backup privado e marcador de cutover; nunca dois backends ativos para os mesmos efeitos.
- [ ] Executar dry-run e importacao somente com fixtures sinteticas em ambiente isolado; esperar snapshot e journal fingerprints identicos. Registrar o que falta para cutover real, incluindo nome do processo dono e estado da origem; nao inferir esses valores de notas historicas.
- [ ] Commit: `feat(temporal): support reconciled idle-session cutover`.
- [ ] Depois de todas as evidencias, apresentar o cutover real concreto para autorizacao operacional. A migracao de codigo pode estar pronta antes disso; sessoes reais so estao migradas quando o marcador, dono unico e verificacao posterior existem.

## Fontes de implementacao

- [Temporalio 1.20.0 no NuGet](https://www.nuget.org/packages/Temporalio/1.20.0): versao fixada consultada durante a preparacao; conferir APIs no assembly restaurado, sem atualizar a versao silenciosamente.
- [Message passing .NET](https://docs.temporal.io/develop/dotnet/workflows/message-passing): Updates, validadores e condicoes de espera.
- [Testing .NET](https://docs.temporal.io/develop/dotnet/best-practices/testing-suite): servidor local, time skipping e replay. Preparacao pode baixar binarios; isso nao e uma execucao offline pura.
- [Continue-As-New](https://docs.temporal.io/develop/dotnet/workflows/continue-as-new) e [Versioning](https://docs.temporal.io/develop/dotnet/workflows/versioning): rotacao segura e compatibilidade de historico.

## Cobertura e estado do plano

| Requisito da especificacao | Tarefas |
| --- | --- |
| Kernel, tipos e semantica dos nodes | 1, 4, 5, 6 |
| Store privado, evidencia e payloads por referencia | 2, 3, 6, 13 |
| Temporal, pai/filho, politicas de retry | 7, 8, 9, 10 |
| Feedback duravel, autenticado, cancelamento e timeout | 5, 6, 8, 12, 13 |
| Admissao, dedupe, batching, backlog e rotacao | 2, 9, 13 |
| Inference/memory/outbound, autoridade e crash recovery | 3, 6, 10, 13 |
| Revisao, preview, interpretacao e handoffs | 11, 12, 13 |
| Modulos, consumidor, UI e compatibilidade | 7, 12, 13 |
| Importacao, corte operacional e rollback | 14 |

- [x] Especificacao aprovada pelo usuario.
- [x] Plano escrito com interfaces, arquivos, testes e evidencias por etapa.
- [x] Auto-revisao de cobertura, consistencia das interfaces e paths; corrigidas fronteira de retry puro, assinatura wire de Activity, importacao e autenticacao antes de resolver feedback.
- [x] Usuario aprovou o plano e execucao inline em 2026-10-05 (vai).
- [x] Tarefas de implementacao executadas e verificadas (QA visual indisponivel registrada na tarefa 12; corte real pendente).
- [ ] Cutover operacional autorizado e verificado.
