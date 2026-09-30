# Host-bound memory read: plano de implementação local

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Sem subagentes, commits ou operação nesta autorização.

**Goal:** oferecer leitura de memória vinculada a ownership verificado pelo host, sem mudar IDs, semântica Graphiti ou o comportamento aprovado da Filomena.

**Architecture:** manter `GraphitiMemoryReader` e o parser/provenance existentes. Uma composição opt-in valida binding e query, envolve `IGraphitiReadClient` para admitir cada chamada e verifica revogação antes de devolver conteúdo. O runtime recebe uma factory pelo contexto capturado, não um segundo contexto de execução nem um serviço global implícito.

**Tech Stack:** C#/.NET 10, assemblies Core e Memory existentes, xUnit v3; clientes controlados em memória, sem HTTP, MCP live ou banco.

**Spec:** `docs/superpowers/specs/2026-09-28-memory-ownership-boundary-design.md`, subordinada à especificação de evolução de 2026-09-27. Complementa, não substitui, o plano do workbench de 2026-09-26.

**Status:** plano proposto para revisão junto da especificação de ownership; nenhuma tarefa implementada nesta entrega. A autorização geral de continuidade não aprova silenciosamente ownership de dados reais ou uma nova política de autenticação.

## Global Constraints

- Brain, prompts, FSM, Grounding/Context Fidelity, Boundary compiler e detectores permanecem intocados.
- Nenhuma chamada Graphiti/SpiceDB/modelo, ingestão, serviço, migration, backfill, credential lookup ou dado privado nesta fatia.
- Namespace físico e `agent:filomena` atuais são preservados; sem UUID novo para dados legados.
- Scope de isolamento vem do host; `ProjectId` do flow e namespace da query não concedem authority.
- Ausência/ambiguidade/unresolved/revoked falham fechados; erro de recall não vira lista vazia.
- Revogação ordena admissões locais; não desfaz I/O já admitido. Sem lock durante await.
- Legado permanece por composição explícita; escolher factory bound nunca pode cair silenciosamente no reader legado.
- Nenhuma dependência do PostgreSQL Evaluation é introduzida no runtime.
- Testes somente após inspeção de fixtures/targets; usar `--no-restore`, sem download automático.
- Alterações existentes em main são preservadas; sem nova worktree ou commit.

## Decisões pequenas e limites

1. Manter authority de leitura em `Filomena.Memory`, sem extrair um motor de policies da inferência. Reutilizar o valor opaco de projeto fornecido pelo host, não um grant de inferência. Custo: dois registries locais pequenos até haver evidência para extração comum.
2. Reutilizar `SessionCompletionContext` como identidade capturada. O nome é histórico; não renomear contratos/factories de inferência nesta fatia.
3. A extensão do runtime só seleciona o reader. `ConversationNamespace`, `MemoryIdentity`, montagem da query, `MemoryResultPolicy` e registro de Boundaries não mudam.
4. O namespace exclusivo é verificado dentro do catálogo fornecido à authority para um backend. Seu owner é a tupla `(IsolationProjectId, ChannelId, AccountId, NativeConversationId, AgentUuid)`; outro owner para o mesmo namespace é conflito, inclusive dentro do mesmo projeto. Bindings de revisões diferentes podem referir o mesmo owner. Não alegar que esse catálogo prova exclusividade entre processos/backends ou ownership real sem inventário externo.
5. Leitura ligada a uma sessão atual não é ainda um facade genérico de memória documental/Dota. Esses domínios não são forçados a `SessionKey` ou `MemoryItem`.

## Evidência atual e mapa de acesso

| Caminho existente | Tratamento neste plano |
|---|---|
| `SessionFlowRuntime.Memory.cs:QueryMemoryAsync` | único ponto de seleção do reader para execução normal e preview |
| `SessionFlowRuntime.Execution.cs:RunSessionAsync` callback | preservado; chega ao ponto acima com `FlowBatch` capturado |
| `SessionResponsePreview.cs` | também chama `QueryMemoryAsync`; precisa da mesma negação/binding |
| `SessionParticipationReview.cs:RunParticipationReviewAsync` | proíbe memória explicitamente; preservar zero recall, não ampliar o review |
| `GraphitiMemoryReader.QueryAsync` | exact: uma neighborhood por pessoa; hybrid pode chamar semantic depois; não basta envolver apenas o método externo |
| `GraphitiReadClient.CallAsync` | conexão lazy e semáforo; pode haver espera após admissão. Nenhuma promessa de abort remoto instantâneo |
| `GraphitiParticipantReader`, `ShadowGraphitiProbe` | caminhos de preflight/identidade fora da nova factory; inventariar, não declarar cobertos nem ativá-los |
| `ManualShadowCommand`, `NativeShadowCommand`, `LocalPromptPlayground` | roots operacionais legados não migrados/ativados nesta fatia |

`tests/Filomena.Memory.Tests` não existe. `GraphitiMemoryTests` está em Llm.Tests, que referencia App. Os novos testes ficarão em Core.Tests com referência explícita a Memory, evitando puxar App. Isso altera dependência de **testes**, não Core → Memory.

## Review Focus

- Duas contas com mesmo native conversation ID: rejeitar binding trocado sem derivar ownership apenas do namespace. Tarefas 1 e 3.
- Revogação entre exact e semantic e durante o retorno: nenhuma segunda chamada admitida; conteúdo não é devolvido após revogação. Tarefa 2.
- Arrays/listas do caller alterados durante await: a query/manifesto utilizado deve permanecer o snapshot validado. Tarefas 1 e 2.
- Preview ou construtor antigo contornando a factory: bound não faz fallback; review continua sem memória. Tarefa 3.
- Catálogo incompleto, identidade ausente ou grant de outra authority: nenhum acesso; não assumir isolamento completo do backend. Tarefas 1 e 4.

## Tarefa 1: catálogo verificado e concessão opaca

**Files:** criar `src/Filomena.Memory/HostMemoryContracts.cs`, `HostMemoryAuthority.cs`; criar `tests/Filomena.Core.Tests/Memory/HostMemoryAuthorityTests.cs`; acrescentar referência a Memory no csproj Core.Tests.

**Interfaces propostas:**
- `MemoryOwnershipBinding`: versão 1, `BindingId`, `Revision`, `IsolationProjectId`, `SessionKey`, namespace físico, agent UUID, ownership `Verified|Unresolved`, referência opaca de evidência, modos permitidos e limites positivos de pessoas/itens/caracteres/texto da query. Sem defaults de ownership nem flags concedidas pelo modelo.
- `HostMemoryGrant`: classe com construtor interno, identidade por instância, sem DTO serializável.
- `HostMemoryAuthority(IReadOnlyList<MemoryOwnershipBinding> bindings, IGraphitiReadClient client)` copia catálogo; rejeita identidade incompleta, duplicidade de binding/revision, conflito de ownership exclusivo do namespace e enum/version inválidos.
- `Grant(string isolationProjectId, string principalRef, Guid bindingId, string bindingRevision): HostMemoryGrant`; só binding Verified e match exato. `Revoke(HostMemoryGrant grant): void` é monotônico.
- `Bind(HostMemoryGrant grant, string isolationProjectId, SessionKey session): IMemoryReader`; a implementação da query fica na tarefa 2. Nenhum reader bruto é retornado.

- [ ] Escrever testes `UnknownOwnershipCannotGrant`, `ForeignAuthorityGrantDenied`, `WrongProjectDenied`, `WrongAccountDenied`, `WrongConversationDenied`, `DuplicateNamespaceOwnershipRejected`, `CallerCannotMutateCatalogAfterGrant`, `RevokedGrantCannotBind`.
- [ ] Assertions: exceção de negação com código estável, contador do cliente = 0; contraprova Verified/scope/session corretos concede binding. A mesma principal em scopes A/B não compartilha grant.
- [ ] Rodar somente `FullyQualifiedName~HostMemoryAuthorityTests`; observar RED antes do comportamento novo.
- [ ] Implementar registry com lock curto e snapshot defensivo, sem I/O. Revogar grant não remove a evidência de binding nem atribui namespace a outro dono automaticamente.
- [ ] Reexecutar: GREEN. Registrar comandos e quaisquer assets offline ausentes; não substituir teste por nova operação live.

## Tarefa 2: consulta bound e admissão por chamada física

**Files:** criar `src/Filomena.Memory/HostBoundMemoryReader.cs`; completar authority; criar `tests/Filomena.Core.Tests/Memory/HostBoundMemoryReaderTests.cs`. Não modificar o parser existente.

**Interfaces:** `HostMemoryAuthority.Bind` retorna `IMemoryReader.QueryAsync(MemoryQuery, CancellationToken)`. Wrapper interno implementa os dois métodos existentes de `IGraphitiReadClient`; não publica novos tools Graphiti.

- [ ] Escrever testes contra `GraphitiMemoryReader` real e cliente controlado: `ExactCallsExpectedParticipantOnce`, `HybridRevocationPreventsSemanticCall`, `RevocationBeforeReturnWithholdsResult`, `WrongNamespaceAgentOrCanonicalPersonDenied`, `MutablePersonsCannotChangeInFlightQuery`, `ValidEmptyRemainsEmpty`, `OutageRemainsFailure`, `ForeignFactAndMissingProvenanceFail`, `QueryLimitsExceededBeforeClient`.
- [ ] Reutilizar shapes completos de fixtures de `GraphitiMemoryTests`/`GraphitiContractDiagnosticTests`, sem copiar dados live. Contadores verificam chamadas/argumentos reais; TCS controla interleaving, sem sleeps.
- [ ] Rodar `FullyQualifiedName~HostBoundMemoryReaderTests`; RED.
- [ ] Copiar persons/categories antes do await; validar `MemoryResultPolicy.ValidateQuery`, namespace, agent UUID, identidade canônica e limites/modes do binding. Payload não escolhe endpoint ou grant.
- [ ] Criar por query um `GraphitiMemoryReader` sobre o cliente guarded, mantendo regras de provenance/category/compiler. Revalidar grant antes de cada neighborhood/search e antes de devolver resultado. Não capturar erro como `[]`.
- [ ] O wrapper não é dono do lifetime do cliente compartilhado fornecido pelo host: não fechar a conexão ao terminar uma query. Falha de um binding não altera grants de outra sessão.
- [ ] Definir ponto de admissão sob o mesmo lock de revogação. Uma chamada já admitida pode terminar/consumir recursos; revogação bloqueia a próxima e a divulgação posterior. Nenhum lock permanece durante I/O ou callback de cliente.
- [ ] GREEN com exact/semantic/hybrid separados. Diagnósticos não incluem conteúdo, namespace privado ou stable IDs desnecessários; nenhuma estimativa de custo é publicada como consumo medido.

## Tarefa 3: factory pelo batch capturado, sem nova engine

**Files:** criar `src/Filomena.Core/Flows/Sessions/SessionMemoryReaderFactory.cs`, `src/Filomena.Memory/HostBoundSessionMemoryFactory.cs`; modificar `SessionFlowRuntime.cs` e `SessionFlowRuntime.Memory.cs`; criar `tests/Filomena.Core.Tests/Memory/HostBoundSessionMemoryTests.cs`.

**Interfaces:**
- `ISessionMemoryReaderFactory.Create(SessionCompletionContext context): IMemoryReader`.
- `HostSessionMemoryBinding`: SessionKey, flow ProjectId/Version/Revision, isolation project ID, authority e grant. Factory exige match completo, não a revisão administrativa mais nova.
- Acrescentar parâmetro opcional final `ISessionMemoryReaderFactory? memoryReaderFactory = null` aos construtores existentes do runtime, encaminhado entre overloads. `memoryReader` e factory simultâneos são erro explícito; reader legado vira factory constante quando nenhuma bound for fornecida.

- [ ] Escrever `LegacyConstructorsRemainCompatible`, `BoundFactoryNeverFallsBack`, `QueuedBatchKeepsR1AfterPublishR2`, `ConcurrentSessionsDoNotExchangeReaders`, `RevocationWhileQueuedPreventsRecall`, `ResponsePreviewUsesBoundFactory`, `ParticipationReviewStillCannotRecall`.
- [ ] Para R1/R2, usar batching real e fake clock existente: admitir R1, publicar R2 antes da execução; primeira query carrega R1, próxima R2. Não testar só strings montadas à mão.
- [ ] RED com filtro da nova classe.
- [ ] Extrair somente helper privado de criação de `SessionCompletionContext(FlowBatch)` e reutilizá-lo em completion/memory. Selecionar reader dentro de `QueryMemoryAsync`; sem ambient context, global reader cache ou novo scheduler.
- [ ] GREEN e regressões `MemoryRuntimeTests`, `MemoryPolicyTests`, `MemoryDomainTests`, `HostBoundSessionCompletionTests`. Verificar overloads existentes sem ambiguidade.

## Tarefa 4: caracterização integrada e mapa de cobertura

**Files:** completar testes das tarefas 1–3; criar `docs/host-bound-memory-read.md`; atualizar status da especificação/plano/progresso sem apagar checkpoints.

- [ ] Usar flow v3 real e MemoryItem controlado passando pelo compiler real. Comparar antes/depois: direct route, não-direto, recall vazio, Boundary Block/regeneration/Allow, Shadow sem sender e sem commit de fala/transição. Não ajustar prompts ou memória para fazer passar.
- [ ] Reenumerar `IGraphitiReadClient`, `GraphitiMemoryReader`, `QueryMemoryAsync` e composition roots. Documentar cada caminho incluído/excluído; um raw path não migrado não pode ser anunciado como protegido.
- [ ] Confirmar negações de projeto/conta/sessão, grant revogado e ausência de fallback, incluindo acesso por participant UUID no cliente guarded. Nenhum cache novo nesta fatia.
- [ ] Build de Core e Memory com `--no-restore`; testes allowlist abaixo após revisão de side effects; whitespace scoped/diff check; brain artifact SHA antes/depois. Autorreview somente, sem sessão independente automática.
- [ ] Registrar resultado local e limitações. Sem commit/ativação. Se assets faltarem, preservar testes preparados e pedir somente a dependência necessária, sem restaurar online implicitamente.

## Comandos previstos, ainda não executados

```powershell
dotnet test tests/Filomena.Core.Tests -c NativeMemory --no-restore --filter 'FullyQualifiedName~HostMemoryAuthorityTests|FullyQualifiedName~HostBoundMemoryReaderTests|FullyQualifiedName~HostBoundSessionMemoryTests' --verbosity quiet
dotnet test tests/Filomena.Core.Tests -c NativeMemory --no-restore --filter 'FullyQualifiedName~MemoryRuntimeTests|FullyQualifiedName~MemoryPolicyTests|FullyQualifiedName~MemoryDomainTests|FullyQualifiedName~HostBoundSessionCompletionTests' --verbosity quiet
dotnet build src/Filomena.Core -c NativeMemory --no-restore --verbosity quiet
dotnet build src/Filomena.Memory -c NativeMemory --no-restore --verbosity quiet
```

Core.Tests já tem alterações alheias. Falha de compilação preexistente deve ser registrada por símbolo; não consertar App/brain ou rodar Llm.Tests inteira como atalho. O novo ProjectReference pode exigir atualizar assets: isso não autoriza restore pela rede.

## Não coberto e gates restantes da fase 3

Inventário privado de ownership; grants reais SpiceDB; ingestão/outbox/checkpoints; acesso por relações/ID em outros adapters; caches/exports/jobs duráveis; onboarding multi-personagem; sharing cross-project; backend real. A leitura bound local não conclui esses itens.

Desativação local é voltar à composição anterior somente por decisão do host, nunca fallback automático. Não há alteração de dados para reverter nesta fatia. Futuro corte operacional exige validação própria; não implica rollback de memória já escrita.

**Handoff:** revisar este plano junto da proposta de ownership antes de implementar. Método preservado: execução sequencial pelo agente principal. A aceitação PostgreSQL e a escolha do primeiro consumidor Damma/Dota seguem pendências separadas; não repetir essas perguntas nem presumir respostas.
