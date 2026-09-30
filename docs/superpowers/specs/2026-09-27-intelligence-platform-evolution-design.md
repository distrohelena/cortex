# Evolução incremental da Filomena para plataforma de inteligência

**Status:** proposta para revisão; não aprovada para implementação.  
**Data:** 2026-09-27.  
**Escopo desta entrega:** auditoria estática do checkout e especificação de evolução.  
**Base Git:** `main`, HEAD `1f090b6609e56a28871c28128e5b1e83a6282730`, com alterações locais preexistentes.

## Nota de reconciliação do checkout — 2026-09-28

As seções 1–17 preservam a auditoria original de 2026-09-27. Seus estados
“não iniciada”, “256 testes” e “primeira fatia recomendada” são históricos,
não a descrição integral do checkout atual. Esta nota qualifica esses estados;
não aprova implementação, migração ou ativação adicional.

Foi realizada nova inspeção estática do Git, referências entre assemblies e
símbolos abaixo. O HEAD permanece o informado acima, mas há numerosas mudanças
rastreadas e arquivos não rastreados. Nenhuma alteração preexistente foi revertida.
Nesta reconciliação não foram executados builds, testes, serviços, inferências,
consultas a banco, migrations ou commits. A skill codebase-memory continuou
inacessível; a leitura usou fontes e buscas locais, sem índice atualizado confirmado.

| Fronteira | Evidência atual inspecionada | Consequência para a proposta |
|---|---|---|
| Inferência autorizada | `src/Filomena.Core/Inference/HostInferenceAuthority.cs`: `Grant`, `Bind`, `Admit`, `Revoke`; `HostBoundSessionCompletionFactory.Create` exige sessão e revisão exatas | A primeira composição host-bound já existe. Preservar e revisar sua adoção, não implementar uma segunda authority. A admissão precede I/O; revogação não desfaz chamada admitida |
| Contexto capturado | `Flows/Sessions/SessionCompletionContext.cs` contém sessão, execução, batch e revisão, explicitamente sem conceder autoridade | Reutilizar esse contexto; não criar contexto concorrente para a próxima fatia |
| Evidência de inferência | `HostInferenceAuthorityTests`, `HostBoundSessionCompletionTests` e `HostBoundFlowCompletionTests` cobrem scope, revogação, binding e decisão sem persona | Testes presentes; resultados de `docs/host-bound-inference.md` são históricos, não reexecutados aqui. Roots operacionais não são considerados migrados por existência do decorator |
| Oficina executável por composição confiável | `EvaluationApplication.InvokeAsync` admite `experiment.run` com access/endpoints fornecidos pelo host; `Evaluation.Cli/Program.cs` constrói dispatcher sem binding operacional | Biblioteca composta não equivale a API autenticada nem CLI operacional provisionada. Não inserir credenciais/config por conveniência |
| Corpus e histórico | `PersistedEvaluationTools` contém `artifact.import`, `case.import`, `issues.history` e `experiment.budget.approve`; `IssueHistoryTests` distingue duplicatas, conflito e fonte indisponível | Evoluir a oficina existente. Histórico de desenhos compatíveis não é ranking universal nem promoção automática |
| Persistência da oficina | `MigrationCatalog.Pending` lista migrations 001–009 e verifica continuidade/checksum | Inventário de migrations 001–004 abaixo é histórico. SQL presente não comprova aplicação ou garantias transacionais aceitas |
| Identidade legada | `FilomenaIdentity.WhatsAppConversationNamespace` ignora `accountId`; `AgentUuid` incorpora `agent:filomena`; `GraphitiMemoryReader` verifica essa identidade | Lacuna de generalização permanece. Não demonstra vazamento live, mas exige ownership explícito antes de novos consumidores; não renomear namespaces/UUIDs |
| Dependências | Core sem referência a adapters; Memory/Llm/OpenClaw referenciam Core; Evaluation referencia Core/Llm; App compõe os adapters | Monólito modular continua suficiente. Não fazer operação depender do PostgreSQL de avaliação |

O checkpoint em `docs/platform-migration-progress.md` registra **628 testes
offline** e aceitação PostgreSQL pendente. Essa contagem é evidência documental
anterior, não um resultado obtido agora nem certificação do diff completo atual.
O documento `docs/host-bound-inference.md` também registra falhas da suíte ampliada;
não se deve resumir o checkout como “tudo verde”.

### Sequência recomendada a partir do estado encontrado

1. Preservar a inferência host-bound já implementada e os contratos Filomena.
   O trabalho seguinte não é recriar a primeira fatia da seção 15.
2. Revisar a proposta existente de ownership de memória em
   [memory-ownership-boundary-design](2026-09-28-memory-ownership-boundary-design.md).
   Seu plano está em `../plans/2026-09-28-host-bound-memory-read.md`; sua existência
   não significa aprovação ou implementação. A menor extensão proposta é leitura
   host-bound sobre identidade legada verificada, sem mudar dados ou parser.
3. Preservar a aceitação PostgreSQL do plano da oficina de 2026-09-26 como gate
   separado. Testes offline não substituem concorrência, crash, restore e roles
   em banco real autorizado. Não iniciar esse ambiente nesta etapa.
4. Somente depois escolher uma tarefa útil de Damma ou Dota. Não há escolha
   automática do consumidor, nem necessidade de criar agora os três domínios,
   SDK público, scheduler universal ou orçamento comercial.

Para a próxima fatia de memória, os testes propostos devem negar wrong scope,
conta/sessão/revisão incorretas, namespace sem ownership, revogação entre chamadas
exact/semantic e divulgação após revogação. Devem preservar empty versus outage,
proveniência e compilação de Boundary. Isso comprova o boundary local, não as
permissões implantadas de Graphiti/SpiceDB nem isolamento de todos os stores.

As decisões materiais remanescentes continuam sendo ownership legado verificável,
primeiro consumidor e sua tarefa/dados autorizados, além da janela de aceitação
de banco. Não é necessário resolver simulação de vidas, coach V4 ou investigação
autônoma completa para revisar a proposta. Nenhuma mudança de código de produto,
brain, schema ou operação foi feita nesta reconciliação.

## 1. Resultado executivo

O repositório já contém um runtime conversacional substancial, adapters de inferência reutilizáveis, memória com validação de proveniência, entrega controlada e uma oficina de avaliação em implementação. Não é necessário começar novamente, mover todos os assemblies ou criar um framework universal.

A principal lacuna para os três produtos não é geração de texto: é a ausência de uma fronteira comum de **escopo e autoridade em tempo de execução**. Existem contratos diferentes para sessão, ambiente de flow e avaliação, mas eles não comprovam isolamento multi-produto de ponta a ponta.

Achados concretos:

1. `SessionKey` contém runtime, personagem, canal, conta e conversa; não representa um projeto de isolamento autenticado. `SessionFlowProject.ProjectId` identifica configuração do flow. `EvaluationScope` usa workspace/projeto para persistência da oficina. Não são IDs intercambiáveis.
2. `IFlowCompletion.CompleteAsync` já permite inferência sem persona ou memória, mas recebe apenas instrução, inputs e configuração de modelo. Não recebe autorização, escopo ou uma concessão de orçamento. Os composition roots assumem callers confiáveis.
3. `FilomenaIdentity.WhatsAppConversationNamespace` ignora explicitamente `accountId`; `AgentUuid` incorpora `agent:filomena`. `GraphitiMemoryReader` também reconhece essa identidade fixa. Isso não demonstra vazamento operacional, mas impede tratar a identidade legada como contrato genérico multi-produto/multi-conta.
4. O workbench tem escopo composto, FKs, hashes e ledger por experimento em código. Não tem orçamento hierárquico de projeto/período/investigação/filhas. A aceitação real PostgreSQL continua pendente nos checkpoints.
5. Existem endpoints locais de playground e bridge, não uma API pública multi-principal estabilizada. Loopback, Origin e um header de playground não equivalem a autenticação/autorização por projeto.
6. A fila de reinterpretação é explicitamente process-local. Não deve ser reutilizada como scheduler durável de investigações.
7. Damma e HelenUI já têm repositórios locais. Damma possui crawler, API, frontend e modelos documentais; HelenUI possui reconhecimento e contratos de ações. Não são implementações dos novos requisitos completas, mas também não são greenfield.

**Recomendação:** manter um monólito modular, preservar o fluxo Filomena e o workbench, e começar por uma composição de inferência scoped/host-owned que funcione tanto para uma decisão estruturada sem chat quanto para o flow existente, com adapters controlados nos primeiros testes. Não publicar endpoints, migrar memória ou ativar produtos nessa primeira fatia.

## 2. Método, limites e estado do checkout

### 2.1 O que foi feito nesta auditoria

- Localizado o checkout real em `C:\dev\filomena` e a baseline em `C:\Users\Helena\Downloads\filomena-system-overview.md`.
- Inspecionados Git, referências de projetos .NET, composition roots, contratos, SQL de migrations, trechos de acesso/efeitos, testes e documentos relacionados abaixo.
- Lidos os dez documentos exigidos no briefing, incluindo os checkpoints cronológicos de memória, reinterpretação e avaliação.
- Localizados e consultados README, símbolos e contratos específicos de `C:\dev\damma` e `C:\dev\helenui`, sem abrir acervos, credenciais ou serviços.
- Usados `Get-Content`, `rg`, listagem de arquivos e comandos Git de leitura. `safe.directory` foi informado apenas no comando Git, sem alterar configuração global.

O skill `codebase-memory` não pôde ser lido por restrição de acesso; o levantamento foi feito por busca e leitura de fonte. Não foi lançada revisão independente nem subagentes.

### 2.2 O que não foi feito

Nenhum build, teste, benchmark, consulta a banco, inferência, Graphiti, Docker, deploy, inspeção de conversas privadas, automação de interface ou mudança operacional. Nenhuma nova contagem de testes é reivindicada.

O projeto App possui um target MSBuild `BuildFlowGraph` que executa Node antes do build. A suíte Evaluation contém testes opt-in com PostgreSQL e `DatabaseLifecycleTests` que executa Docker. Não foi considerado seguro nem necessário rodar a solução indiscriminadamente para esta auditoria documental.

Não foi auditada integralmente a instalação upstream do OpenClaw nem cada linha do vendor Graphiti. Garantias dessas dependências permanecem limitadas aos contratos, adapters, testes e relatórios locais inspecionados. Não há nova prova de saúde, dados, permissões ou configuração da instalação ativa.

### 2.3 Trabalho existente preservado

O Git tem mudanças rastreadas em App/Core/Llm/OpenClaw, prompts/projetos experimentais, frontend e testes; também há fontes, testes, documentos e `runtime/evaluation/` não rastreados. O HEAD sozinho não descreve este checkout. A auditoria considera os arquivos presentes e identifica essa limitação de reprodutibilidade.

Não foram revertidas, formatadas, movidas, commitadas ou incorporadas essas mudanças. `workspace/DREAMS.md`, configurações pessoais e segredos não foram lidos nem alterados. O novo documento é a única edição pretendida nesta etapa.

### 2.4 Escala de evidência

| Nível | Significado nesta especificação |
|---|---|
| Fonte | Implementação/símbolo inspecionado; não prova execução correta |
| Teste existente | Teste localizado e contrato examinado; não executado nesta auditoria |
| Checkpoint histórico | Resultado registrado em documento anterior; não revalidado agora |
| Aceitação pendente | Precisa de ambiente apropriado e execução futura autorizada |
| Proposta | Não implementado nem autorizado por este documento |

O checkpoint mais recente da oficina registra **256 testes offline**, build da CLI verde, testes PostgreSQL/Docker não executados e bloqueios separados da solução. A especificação e o plano originais ainda contêm status e checkboxes anteriores à implementação. O código, os commits recentes e `docs/evaluation-offline-status.md` qualificam esses cabeçalhos; não se deve apagar o histórico ou declarar tudo concluído.

## 3. Catálogo de evidências

Os códigos E abaixo são referências para a matriz e para as conclusões. Caminhos são relativos à raiz do checkout, exceto quando indicado. Testes citados são evidências disponíveis, não resultados desta rodada.

| ID | Localização e símbolos | Testes/documentação e limite |
|---|---|---|
| E01 | `src/*/*.csproj`, `Filomena.slnx` | Referências reais entre assemblies; não build atual |
| E02 | `src/Filomena.App/Program.cs`; `NativeShadowCommand.RunCoreAsync`; `NativeLiveCommand`; `NativeConversationHostCommand`; `ManualShadowCommand` | `NativeShadowCommandTests`, `NativeLiveCommandTests`, `NativeConversationHostCompositionTests`; roots diferentes, não uma API única |
| E03 | `src/Filomena.Core/Flows/Sessions/SessionFlowContracts.cs`: `SessionKey`, `SessionFlowProject`, `SessionTraceEvent`; `SessionFlowRuntime.UpdateProject` | `tests/Filomena.Core.Tests/Flows/SessionFlowRuntimeTests.cs`: `ProjectPublicationRequiresIncreasingVersionAndUniqueRevision`, `ThroughCurrentBatchHistoryStopsAtThatBatchAdmissionWatermark` |
| E04 | `src/Filomena.Core/Flows/FlowBindings.cs`, `FlowCompiler`, `FlowBindingValidator`, `NodeCatalog` | `FlowCompilerTests`, `FlowBindingValidatorTests`, `FlowExamplesTests`; contrato estático em `docs/filomena-flow-contracts.md`, não autorização runtime |
| E05 | `src/Filomena.Core/Flows/TypedFlowRunner.cs`: `FlowCompletionRequest`, `IFlowCompletion`, `RunCoreAsync`; `InstructionContext.Compose` | `TypedFlowSessionNodeTests`, `InstructionContextTests`; runner ainda valida `FlowDomain.Message` na entrada |
| E06 | `src/Filomena.Llm/ProviderFlowCompletion.cs`; `OpenAi/OpenAiFlowCompletion.cs`; `LmStudio/FlowModelCompletion.cs`, `LmStudioClient.cs` | `LmStudioClientTests`, `tests/Filomena.Evaluation.Tests/FlowTargetTests.cs`; seleção explícita, não auto-router |
| E07 | `src/Filomena.Llm/SystemOne/SystemOneClient.cs`, `SystemOneFlowCompletion`, `SystemOneParticipationCompletion` | `SystemOneClientTests`, `SystemOneFlowCompletionTests`, `SystemOneSummaryCompositionTests`, `SystemOneWireReplayTests` |
| E08 | `src/Filomena.Core/Identity/FilomenaIdentity.cs`; `Memory/MemoryIdentity.cs`; `Flows/Sessions/SessionFlowRuntime.Memory.cs` | `MemoryDomainTests`, `MemoryRuntimeTests`; namespace legado e identidade de agente são específicos |
| E09 | `src/Filomena.Memory/GraphitiReadClient.cs`, `GraphitiResponseValidation.cs`, `GraphitiMemoryReader.cs` | `GraphitiMemoryTests`: `Live_success_shape_preserves_identity_namespace_subject_episodes_and_source`, `Missing_provenance_is_not_a_foreign_namespace`, `Hybrid_backend_error_cannot_return_partial_exact_memories` |
| E10 | `plugins/openclaw-memory-graphiti/schema.zed`, `authorization.ts`, `spicedb.ts` | `authorization.test.ts`; relações person/agent/group/memory_fragment, não projeto multi-produto completo |
| E11 | `src/Filomena.App/NativeShadowCommand.cs`, `NativeMemoryHost`, `NativeMemoryOutbox`; plugin `native-ingest*.ts`, `native-memory-recovery.ts`, `native-memory-history.ts` | `NativeMemoryCompositionTests`, `native-ingest-composition.test.ts`, `native-memory-recovery.test.ts`; `docs/native-memory-ingestion.md` registra cobertura parcial |
| E12 | `src/Filomena.Core/Outbound/ShadowOutboundHost.cs`, `OutboundJournal.cs`; `TypedFlowRunner` e `SessionFlowRuntime.Execution.cs` | `LiveOutboundHostTests`: `ShadowModeWithBoundTransportNeverDispatches`, `IndeterminateIsNeverRedispatchedAndOnlyExplicitReconciliationUnblocksTheSession`, `HistoricalShadowRecordsAreNeverPromotedToRealSends`; `ShadowRuntimeTests` |
| E13 | `src/Filomena.Core/ShadowInboundJournal.cs`, `ObservedShadowRun`, `ObservedLiveRun`; `src/Filomena.OpenClaw/Observation/NativeObservationReader*.cs` | `ShadowInboxTests`, `ObservedShadowRunTests`, `NativeObservationReaderTests`, `NativeReplayPlanTests`; upstream nativo não integralmente reauditado |
| E14 | `src/Filomena.Core/Boundaries/BoundaryAdmission.cs`, `BoundaryInterpreter.cs`, `BoundaryExecutionRegistry.cs`, `ComposedBoundaryEnforcement.cs`, `MultiDetectorComposition.cs` | `BoundaryCompilationTests`, `BoundaryRuntimeTests`, `BoundaryCoverageV1Tests`; cobertura sintética e live vazia distintas |
| E15 | `SessionParticipationReview.cs`, `SessionReviewedResponse.cs`, `PendingParticipationReviews.cs`; `src/Filomena.App/ContextInterpretationHost.cs` | `SessionParticipationReviewTests.Direct_rule_is_preserved_without_presence_generation_or_transition`; `ContextInterpretationHostTests`; `docs/irony-context-replay-v1.md` |
| E16 | `src/Filomena.Core/LiveSessionContextStore.cs`, `LiveContextMigration.cs`; `src/Filomena.App/NativeLiveReadCursorStore.cs` | `LiveContextMigrationTests`, `NativeLiveReadCursorTests`; checkpoints privados com hashes, não banco genérico de jobs |
| E17 | `src/Filomena.Evaluation/Contracts/EvaluationAccess.cs`, `EvaluationContracts.cs`, `RevisionHasher.cs`, `ManifestValidator.cs` | `AccessTests`, `RevisionTests`, `ManifestTests`; `FromHost` é fábrica de confiança local, não autenticação externa |
| E18 | `src/Filomena.Evaluation/Storage/PostgresEvaluationStore.cs`, `MigrationRunner.cs`, `MigrationCatalog.cs`, migrations `001`–`004` | `StoreTests`, `MigrationCatalogTests`; testes reais de banco pendentes |
| E19 | `src/Filomena.Evaluation/Execution/AttemptLedger.cs`, `AttemptAdmission.cs`, `BudgetReservation.cs`, `ExperimentCoordinator.cs`, `CompletionTrialExecutor.cs` | `AttemptLedgerPostgresTests`, `ExperimentCoordinatorTests`, `FailedTrialTests`, `CompletionTrialAcceptanceTests`; concorrência/durabilidade não certificadas nesta auditoria |
| E20 | `BoundEvaluationProviders`, `EvaluationProviderFactory`, `EvaluationHttpHandler`, `ProviderObservationParser`, `FlowCompletionEvaluationTarget` | `BoundEvaluationProvidersTests`, `ProviderObservationTests`, `CompletionTrialMaterialTests`; inferência separada de conversa já existe para avaliação |
| E21 | `src/Filomena.Evaluation/Tools/OfflineEvaluationTools.cs`, `PersistedEvaluationTools.cs`; `Comparison/ExperimentComparer.cs`, `CostProjection.cs`; CLI `Program.cs` | `PersistedToolTests`, `ComparisonTests`, `CostTests`; CLI aceita somente `offline`, sem promover/deployar |
| E22 | `src/Filomena.App/LocalPromptPlayground.cs`; `Playground/SessionFlowEndpoints.cs`, `TypedFlowEndpoints.cs`; `InboundBridgeHttpServer.cs` | `LocalPromptPlaygroundTests`, `SessionFlowIntegrationTests`; defesa local não é boundary multi-tenant |
| E23 | `C:\dev\damma\README.md`; `crawler/prisma/schema.prisma` | `Document`, `DocumentRawPayload`, `DocumentExtraction`, `EditionPage`, `CrawlRun`, `HistoricalCrawlRun`, `CrawlAttempt`; existência verificada, operação e investigação semântica não auditadas |
| E24 | `C:\dev\helenui\README.md`; `plugins/recognition-cli/src/RecognitionCli/Program.cs`; `plugins/navigator-service/src/NavigatorService/Contracts/PerformActionRequest.cs` | `RecognitionCliApplication.RunAsync`; `PerformActionRequest(ScopeId, ActionId, HoldMs)`; nenhuma execução ou garantia de integração Dota |

## 4. Arquitetura atual e fluxos críticos

### 4.1 Dependências reais

```text
Filomena.App ----------> Core, Llm, Memory, OpenClaw
Filomena.Llm ----------> Core
Filomena.Memory -------> Core + ModelContextProtocol
Filomena.OpenClaw -----> Core + ModelContextProtocol
Filomena.Evaluation ---> Core, Llm + Npgsql
Evaluation.Cli -------> Evaluation

Plugin Graphiti (TypeScript) -> Graphiti/FalkorDB + SpiceDB
SpiceDB --------------------> PostgreSQL operacional
Evaluation -----------------> PostgreSQL próprio, quando provisionado
```

Todos os sete projetos .NET inspecionados miram .NET 10. O Core não depende dos adapters. Evaluation não referencia App, Memory ou OpenClaw: essa separação deve permanecer. Ausência de referência impede alguns acoplamentos, mas não substitui revisão de efeitos nos composition roots. [E01]

### 4.2 Mais de uma geração de contrato

`FlowCompiler` e `FlowWorkspaceDefinition` descrevem releases portáteis com ingress/characters, espaços e grants estáticos. `TypedFlowCompiler`/`TypedFlowRunner` e `SessionFlowProjectCompiler`/`SessionFlowRuntime` executam o grafo de sessão atual. Há ainda o caminho anterior `ConversationTurnManager`/`FilomenaAgent` e código local não rastreado em `App/FlowRuntime`.

Não substituir um contrato pelo outro apenas porque ambos se chamam flow. A primeira fatia usa o seam `IFlowCompletion` do runtime real, não uma nova DSL nem uma conversão global dos artifacts. [E02–E05]

### 4.3 Conversa operacional

`Program` seleciona roots explícitos (`--native-shadow`, `--native-live`, `--native-conversation-host`, manual, playground) antes do bootstrap anterior. O host nativo faz preflight, binding, leitura e mapeamento; `Observed*Run` conecta journal e runtime; o runtime fixa projeto/contexto do batch, executa participação, memória, geração/enforcement e entrega.

`NativeShadowCommand` contém staging de memória **antes** de enrichment/participação e avanço do cursor quando `memoryHost` está configurado. A presença desse trecho não habilita writer no Shadow nem comprova backlog completo. [E02, E11, E13]

O gate de envolvimento direto é regra do projeto v3, não autorização geral do core. `SessionParticipationReviewTests` exercita preservação da rota sem segunda participação. Guardar isso como caracterização; não retreinar ou retocar os prompts durante a migração. [E15]

### 4.4 Memória, enforcement e delivery

`GraphitiReadClient` oferece apenas neighborhood/search; `GraphitiResponseValidation` e `MemoryResultPolicy` validam shape, namespace, sujeito e temporalidade. A interface do reader recebe namespace, não um principal autenticado por operação. A correção da proveniência não demonstra, por si só, autorização de leitura de todos os dados do backend. A futura facade precisa validar binding antes do acesso e respeitar a política vigente no backend. [E08–E10]

`BoundaryExecutionRegistry` liga memórias/limites à execução; o runner e a composição de detectores preservam a checagem real. `ShadowOutboundHost` começa Disabled, verifica provenance/rota/capabilities e registra reservas/resultados. Shadow não chama transport; resultados incertos permanecem reconciliáveis. `SendGuard` continuará sendo fronteira de entrega de conversa, não o mecanismo universal para OCR, replay ou documento. [E12, E14]

### 4.5 Oficina

`FlowCompletionEvaluationTarget` já executa um completion pinado sem Session Runtime, Graphiti ou sender. `BoundEvaluationProviders` conecta clientes dedicados ao ledger; `ExperimentCoordinator` faz claims, completion e cancelamento; `PersistedEvaluationTools` oferece import/read/plan/cancel por authority host-owned. A CLI pública local continua offline.

Os limites são por experimento e chamada, com reserva conservadora. Não existe nessa implementação uma concessão raiz comum a várias investigações/jobs, limite por período ou revalidação centralizada de grants revogados. Não estender o vocabulário no relatório como se esses recursos já estivessem prontos. [E17–E21]

## 5. Armazenamento, ownership e fronteiras de acesso

| Store/estado atual | Identidade, constraints e consumidores | Lacuna e tratamento proposto |
|---|---|---|
| Graphiti/FalkorDB | `group_id`, UUIDs determinísticos, episódios, proveniência; reader .NET e plugin | Namespace legado não inclui IsolationProjectId; não reusar globalmente nem rehash silencioso [E08–E11] |
| SpiceDB/PostgreSQL | `person`, `agent`, `group`, `memory_fragment`; access/contribute/view/delete | `view = involves + shared_by + source_group->access` pode permitir acesso por relação sem projeto; não certifica isolamento futuro apenas por grupo. Auditar/planejar qualificação dos resources e grants antes de novos consumidores [E10] |
| Native observation | Journal upstream, epoch/sequence/ID, reader HTTP | Preservar owner e contratos; não transformar snapshot em nova execução nem migrar journal neste slice [E13] |
| Inbox/outbound locais | JSONL privados, reservas, receipts e replay; hashes/lineage | Escopo depende da composição e diretórios host-owned; não aceitar caminho do request [E12, E13, E16] |
| Contexto/cursor live | JSON versionado, checksum, `AuthorityRef`, projeto e fingerprints dos journals | Associar explicitamente ao novo escopo externamente; sem restaurar checkpoint antigo sobre efeitos mais recentes [E16] |
| Outbox/receipts/recovery memória | Arquivos .NET e SQLite plugin; binding account/conversation, IDs, watermark de confirmação | Preservar indeterminate, não confundir queued/persisted/extracted; cobertura de histórico retido é parcial [E11] |
| Runtime e reinterpretação | Dicionários por `SessionKey` e controller process-local | Um runtime aceita uma identidade de projeto; não há scheduler durável de domínio. Isolar instâncias/bindings [E03, E15] |
| Playground | `SessionFlowHarnessStore` com IDs e dicionário de harnesses; arquivos de projeto locais | API de desenvolvimento sem multi-principal; não publicar como API dos produtos [E22] |
| Evaluation PostgreSQL | schema `evaluation`, PK/FKs compostas workspace/project, hashes, JSONB/bytea, triggers e índices de unicidade | Read/write por scope no application store; migrations não têm RLS por tenant. Role compartilhada não protege contra caller com conexão SQL direta; manter DB inacessível aos agentes [E18] |
| Credenciais/endpoints | callbacks `Func<string?>`, variáveis/config do host, aliases em perfis | Não existe BYOK público com ownership/rotação/revogação por projeto comprovados; não ler nem migrar secrets nesta etapa [E06, E20] |
| Damma existente | PostgreSQL/Redis do crawler, documentos/payloads/extractions/pages e archive staging/vault | Preservar stores existentes; inventário de ownership completo ainda requerido no repositório Damma, sem reutilizar DB do workbench [E23] |

Migrations Evaluation: `001` cria evidência/revisões/avaliações/auditoria e triggers de imutabilidade; `002` adiciona reservas/leases/outcomes e recusa inferir accounting de attempts antigos; `003` adiciona schedule e deixa registros antigos sem agenda não elegíveis; `004` associa output artifact ao trial. Essas decisões permanecem. SQL presente não comprova grants efetivos, DDL restrito, restore, locking ou concorrência. [E18, E19]

Não foi encontrado um cache global de inferência a promover. Foram encontrados estado de sessão e harnesses em memória. Qualquer cache novo deve incluir scope, configuração, artifact/input hash e restrição de visibilidade; cache hit não é nova amostra experimental nem autorização persistente. Exportações precisam da mesma checagem de artefato por ID, não apenas filtro de listagem.

## 6. Glossário e mapeamento

| Conceito proposto | Código atual e distinção |
|---|---|
| Produto | Atores, Dota Coach, Damma; não são três tenants fixos |
| Aplicação | Composição de um produto; Filomena é uma identidade/aplicação de Atores |
| Projeto .NET | Unidade de build/assembly; não identidade de dados |
| Projeto de flow | `SessionFlowProject.ProjectId` e revisão; configuração executável |
| Projeto de isolamento | Novo vínculo host-owned de ownership; mapear explicitamente para `EvaluationScope`, bindings e stores; não inferir do flow |
| Workspace | Dimensão já existente na oficina; preservar IDs, sem introduzir nova árvore de organizações |
| Principal | Identidade de operação do host, como `EvaluationAccess.Principal`; pode ser serviço. Não é `MemoryPerson` |
| Pessoa/ator | Participante representado na conversa; identidade canônica, sem authority implícita |
| Agent | Composição executável e permissões associadas; não produto nem classe onisciente; `FilomenaAgent` antigo não vira definição universal |
| Persona | Personalidade/apresentação opcional; `InstructionContext` na conversa, não exigida para inferência avulsa |
| Conversation/session | `SessionKey`, history e estado conversacional; investigação/replay não cria JID fictício |
| Run/step/attempt | `SessionFlowOutcome.ExecutionId`, node trace e attempts do workbench são contratos distintos; criar correlação explícita, não renomear todos os IDs |
| Memory/context/domain state | `IMemoryReader`/Graphiti; inputs selecionados; estado especializado Dota/Damma/Atores. Não converter tudo em `MemoryItem` |
| Observation/event | Observação com fonte/tempo versus acontecimento registrado; nenhum concede permissão |
| Capability/tool | Grant de operação versus implementação invocável; o modelo apenas propõe tool calls |
| Provider/model/deployment | Adapter/protocolo, identidade de modelo, endpoint+credential+capabilities host-owned; aliases não garantem pesos remotos imutáveis |
| Inference/routing policy | Execução versus seleção versionada de implementação; `ProviderFlowCompletion` é dispatch explícito, não inteligência de roteamento |
| Evidence/artifact | Fonte/resultado com hash, ownership e proveniência; `EvidenceArtifactRef` existe na oficina, não é serviço global de documentos |
| Budget/usage/cost | Concessão, consumo e valoração; `ExperimentBudgets`, `ProviderObservationParser`, `CostProjection`; hierarquia ainda ausente |
| Trace/audit/evaluation | Diagnóstico runtime, registro de authority/efeitos, avaliação de qualidade; compartilhar correlação, não necessariamente store |

## 7. Matriz de evolução

Cada linha distingue existência e validação. “Teste existente” não significa execução nesta rodada.

| Requisito/conceito | Localização/evidência | Existência e validação disponível | Ação | Mudança mínima e risco/teste necessário |
|---|---|---|---|---|
| Flow Filomena e direct rule | E03, E05, E15; projeto v3 | Código + testes + milestone histórico | Preservar | Mesma pin, outputs/control paths; testar direct nos três estados sem chamada a Presença |
| Boundary e regeneration | E14 | Código/testes; live vazio não cobre detector | Preservar | Caracterizar compiler→detectors→recheck; final Block nunca entrega |
| Shadow/Enabled/uncertain | E12 | Código/testes e relatórios históricos | Preservar | Zero sender no Shadow; não promover receipts; cancel/uncertain sem retry cego |
| Admission/replay/reply | E13 | Adapters/testes; upstream não revalidado | Preservar | Regressões de ID/ordem/self/quote/legacy isolation; sem refactor nativo |
| Ingestão independente | E11 | Código, testes e operação histórica parcial | Preservar | Silence ainda stages elegível; sem iniciar writer na avaliação |
| Ownership multi-project | E03, E08, E17 | Parcial e fragmentado | Introduzir | Binding confiável mínimo; request ID não autoriza; testes negativos de mesmo ator/conversa em scopes diferentes |
| Revogação vigente | E12, E17, E19 | Guards específicos; sem contrato comum | Introduzir | Revalidar grant antes de efeito e após await relevante; pin não congela permissão |
| Memória multi-agent/produto | E08–E10 | Identidade Filomena hardcoded | Refatorar depois da fronteira inicial | Resolver binding explícito; compatibilidade legado sem mudar UUID; negar ownership ambíguo |
| Inferência sem chat | E05, E20 | Completion e alvo de avaliação existentes | Refatorar composição | Wrapper host-bound; não chamar TypedFlowRunner com mensagem falsa; teste sem session/persona/memory |
| Schema/capabilities | E05–E07, E20 | Validação específica, sem catálogo universal | Preservar/estender mínimo | Capability declarada por deployment; mismatch antes do provider; output inválido não completed |
| Regras/engines não LLM | E05, E24 | Nodes puros; OCR externo localizado | Preservar boundary | Código de domínio executa normalmente; não disfarçar OCR/parser de completion |
| Tool calling genérico | E06, MCP específico | Tools MCP específicas, não loop genérico de agent | Adiar | Proposta/host authorize/execute/verify, com política; sem tool registry universal agora |
| Streaming | E05, E06 | `Task<string>`; sem stream público de tokens | Adiar | Resultado final bufferizado na primeira fatia; tokens e eventos separados; nenhum tool call parcial |
| BYOK | E06, E20 | Credential callbacks; sem serviço BYOK completo | Adiar API; preservar refs | Host resolve alias por scope, sem URL/path livre; testar credential A inacessível em B |
| Fallback | E06, E07 | Dispatch explícito; summary separado não é fallback | Preservar ausência automática | Proibir local→cloud implícito; futuro fallback precisa política e nova reserva |
| Budgets de execução | E19, E20 | Ledger por experimento, aceitação DB pendente | Preservar | Concluir aceitação antes de extrair; não fazer runtime depender de Evaluation |
| Projeto/período/tipo/filhas | E19 | Não implementado | Introduzir em fase posterior | Grant raiz imutável, child não renova teto; concorrência real com reserva agregada |
| Uso/custo conhecidos | E20, E21 | Parsers/projeção fora do retorno de completion | Refatorar envelope | Usage nullable e fonte; não inventar tokens locais nem preço de alias |
| Artifact/retrieval por ID | E18, E22 | Scoped no store oficina; playground local | Estender boundary | DemandScope/read antes de lookup; negar relação/export cross-scope, sem RLS fictício |
| Jobs e retomada | E15, E16, E19 | Checkpoints específicos, claims de trials; não scheduler genérico | Adiar generalização | Persistir só onde promete resume; revalidar permissão e manter unknown outcomes |
| API/SDK | E02, E21, E22 | CLI offline/endpoints de dev e específicos | Adiar público | Contrato interno primeiro; sem publicar playground nem cinco serviços |
| Atores por experiências | E03, E08 | Persona/contexto; motor biográfico não encontrado | Introduzir domínio depois | Fonte/contato/ficção/interpretação distintos; teste sem spoiler/conhecimento indevido |
| Dota análise retrospectiva | Busca em src/plugins, E24 | Integração de domínio não encontrada na Filomena | Introduzir domínio depois | Parser/patch/player-view→evidências; não LLM inventando estatística |
| Dota V4/atuação | E24 | HelenUI externo, integração ausente | Adiar | Read/act são grants distintos; sem captura, inputs ou evasão |
| Damma investigativa | E23 | Crawler/documentos existem; nova investigação não comprovada | Adaptar domínio depois | Reusar originais/extractions; modelar derivação/correção/independência sem achatar em memória social |
| Autoajuste | E17–E21 | Oficina parcial, sem promoção | Preservar/adiar autonomia | Hipótese→experimento→decisão; ativação explícita; não duplicar workbench |

## 8. Arquitetura alvo recomendada

### 8.1 Alternativas

| Alternativa | Benefício | Custo/risco | Decisão proposta |
|---|---|---|---|
| Adaptar monólito e composition roots atuais | Preserva execução e permite testar um boundary útil | Exige disciplina contra chamadas diretas fora da facade | Recomendada |
| Extrair imediatamente novos assemblies/repos/serviços | Separação física mais visível | Migração ampla, contratos prematuros, duplicação e risco em dados | Adiar até dependência concreta justificar |
| Generalizar Session Runtime para todo trabalho | Reutiliza scheduler aparente | Obriga replay/investigação a fingir chat, mistura FSM social e efeitos de domínio | Rejeitada |

### 8.2 Responsabilidades

```text
Produtos: Atores / Dota Coach / Damma
        |
        | tarefas, inputs e referências autorizadas
        v
Composição host-owned: principal, scope, grants, bindings, perfil pinado
        |
        +--> componentes determinísticos e engines do domínio
        +--> inferência: adapters atuais + validação + accounting aplicável
        +--> contexto/memória/artefatos: acesso scoped
        +--> efeitos específicos: proposta, autorização, submissão, receipt

Filomena: SessionFlowRuntime + participação/persona + Boundary + SendGuard
Workbench: avalia os mesmos seams; não ativa nem hospeda a conversa
```

Essas são responsabilidades, não exigências de novas classes/assemblies. Reusar os pontos de composição existentes; extrair apenas o que pelo menos dois caminhos realmente precisarem.

### 8.3 Escopo mínimo

Proposta V1: um **projeto de isolamento opaco** representa ownership de uma aplicação/instância autorizada. Produto é classificação, deployment é instalação, pessoa é participante e personagem é identidade; nenhum é tenant por si só. Não criar automaticamente um tenant global para todos os Atores, outro para Dota e outro para Damma.

Não adicionar hierarquia de organizações. Manter `EvaluationScope(WorkspaceId, ProjectId)` existente e criar um mapeamento explícito host-owned para o projeto operacional quando necessário. O `SessionFlowProject.ProjectId` continua sendo ID do artifact do flow. A mesma pessoa em duas aplicações não ganha compartilhamento automático.

Primeira política conservadora: aplicações de produtos distintos têm ownership distinto. Várias personagens por projeto ou um projeto por personagem/cliente continuam decisões de produto posteriores; a fatia não depende de escolher uma tenancy comercial agora.

### 8.4 Autoridade

Uma chamada interna recebe uma concessão criada pela composição confiável, não um DTO que o cliente pode preencher. Ela vincula principal, scope, operações e bindings permitidos. Uma revisão fixa comportamento; a autoridade deve ser consultada novamente antes do efeito. A concessão não é um token criptográfico inventado nem um sistema paralelo de login.

`EvaluationAccess.FromHost` é precedente de forma, não uma solução de autenticação/revogação universal. `SpiceDB` continua responsável pelas relações que já controla. ACLs/config do host continuam responsáveis pelo processo local. Uma futura API deverá autenticar principal e resolver grants, sem confiar em `project_id`, role ou credential alias isoladamente.

Código confiável em processo pode acessar APIs públicas e conexões; não prometer isolamento contra plugin arbitrário com execução no mesmo processo ou administrador do host. A primeira versão restringe composição/export de dependências e testa as fronteiras externas. Execução de plugins não confiáveis em sandbox seria outro escopo.

## 9. Contratos mínimos propostos, sem API pública nova

### 9.1 Inferência autorizada

Reutilizar `IFlowCompletion` como adapter interno. Uma composição bound aplica grant + perfil/deployment autorizado e recebe uma requisição pinada. Inputs podem ser JSON estruturado sem persona, sessão, memória ou `FlowDomain.Message`.

Conceitualmente a operação recebe referências autorizadas de perfil/schema e input, correlação run/step/attempt, cancelamento e deadline de host. O principal, scope, endpoint e segredo são resolvidos fora do payload não confiável. Os nomes/assinaturas finais ficam para o plano aprovado.

Resultado deve distinguir texto, objeto validado e falha; conter refs de configuração, provider/model efetivo quando disponível, duração e observações de uso/custo com origem. O retorno `Task<string>` dos adapters pode permanecer sob um wrapper compatível para o flow. Uma falha de validação não vira sucesso vazio; partial output não equivale a completion válido.

A instrumentação comum não deve exigir `ExperimentId` na conversa. Accounting da oficina continua no adapter Evaluation. Não copiar o ledger para um segundo banco apenas para acrescentar scope à inferência.

### 9.2 Agent, memory, tools e context

Os nomes `filomena.agent`, `.memory`, `.tools`, `.context` são intenções futuras. Não criar cinco serviços/interfaces vazias.

- Agent: composição de tarefas e capabilities; o Session Runtime atual é uma implementação conversacional.
- Memory: facade scoped sobre readers/writers autorizados; novo domínio não recebe reader bruto com namespace arbitrário.
- Context: seleção com proveniência e limites, não autoridade nem armazenamento universal.
- Tools: operação proposta validada pelo host, com schema, binding, budget e confirmação quando aplicável.

Referências a documentos/mídia levam identificador, versão/hash, origem, tempo, sequência e schema quando relevantes. A dereferência verifica scope antes de leitura. Não trafegar frames/documentos inteiros como base64 por conveniência nem usar `MemoryItem` para inventário de jogo ou conclusão documental.

### 9.3 Erros, streaming e efeitos

Categorias propostas: `unauthorized`, `scope_mismatch`, `binding_unavailable`, `capability_mismatch`, `schema_invalid`, `cancelled_before_submission`, `deadline_exceeded`, `provider_failure`, `indeterminate`, `budget_exhausted`, `reconciliation_required`. Mapear erros existentes sem apagar seus diagnósticos; não expor secrets/payloads remotos.

Configuração/schema incompatível falha antes de dispatch. Cancelamento depois de submission não apaga consumo. Retry/fallback é tentativa nova, depende de política e budget e não ocorre automaticamente em resultado incerto.

Primeira fatia é síncrona/assíncrona por Task, com saída final bufferizada. Streaming de tokens, stream de observações e eventos de execução são contratos diferentes. Saída sujeita a enforcement não é transmitida antes de aprovação; tool call parcial nunca executa. Planejar streaming em fatia separada, sem alterar SendGuard para acomodá-lo.

## 10. Isolamento e migração da memória

Não corrigir `agent:filomena` por substituição textual global. UUIDs, episódios, namespaces e relações atuais são dados persistidos; seus hashes não podem mudar silenciosamente.

Proposta de transição futura:

1. Inventariar bindings host-owned, namespaces físicos, contas, identidade de agente, diretórios/journals, grants SpiceDB, consumidores e artifacts; registrar refs sem publicar IDs pessoais.
2. Produzir um manifesto verificável de ownership legado. Dados ambíguos ficam indisponíveis para novos consumidores, não atribuídos a um projeto público/default.
3. Acrescentar resolução de binding por scope. A Filomena existente pode manter exatamente seus IDs por um binding legado explícito. Novo produto não pode selecionar esse binding pelo nome da pessoa.
4. Colocar checagens em lookup por ID, retrieval, relação, export, cache, job e escrita. Validar também `memory_fragment.view`, não apenas o `group_id` usado na busca.
5. Se realmente for necessário copiar/reindexar dados, usar revisão/migration aditiva, checkpoint, hashes/contagens, proveniência e validação de grants. Não fazer dual-write sem owner e dedupe definidos.
6. Cortar um consumidor por vez, sem dispatch antigo e novo simultâneos. Desativar a nova composição antes de restaurar artifact/config; dados novos e efeitos já ocorridos exigem reconciliação/roll-forward, não simples rollback de arquivo.

Nenhuma migration SQL/SpiceDB/Graphiti é proposta como execução imediata. Não selecionar banco novo: cada domínio preserva seu store até uma necessidade concreta demonstrar o contrário.

## 11. Inteligência híbrida e perfis

`SystemOneClient` implementa o protocolo tipado; o completion de flow aceita objetos fechados com enums/booleans, não geração livre. `SystemOneParticipationCompletion` separa decisão e resumo. Overrides `choiceQuestions` podem substituir instruções por campo e precisam entrar na comparação efetiva. [E07]

`ProviderFlowCompletion` escolhe o provider explícito, com default local quando ausente; isso é compatibilidade do código atual, não política ótima. OpenAI usa Responses e retorna texto completo; LM Studio usa o cliente local; a oficina limita endpoints por alias host-owned. Não inventar preços, janela de contexto ou suporte a tools/streaming a partir do nome do modelo. [E06, E20]

Na evolução, um perfil pinado declara tarefa, contrato, modelo/deployment, parâmetros, capacidade e política de dados. Ausência de capacidade implica rejeição. Uma regra ou engine quantitativa pode resolver uma tarefa sem modelo. Um fallback local→cloud precisa de autorização de dados e nova contabilização; não é automático.

O workbench existente compara candidatos sob gates de qualidade e depois custo/latência. Não estabelecer Jev, Luna, Gemma ou Laya como vencedor sem comparação executada. Não introduzir router inteligente ou promoção automática.

## 12. Domínios dos três produtos

### 12.1 Atores

Preservar Filomena atual como aplicação e não redefinir sua personalidade. O domínio futuro de experiências mantém uma sequência aprovada com tempo, idade/acesso coerentes, fonte e revisões. PRNG com seed escolhe oportunidades subordinadas a regras e preferências anteriores; não sorteia personalidade a cada etapa.

Contato distingue desconhecer, ouvir falar, recomendação, experiência parcial, conclusão, abandono e revisita. Processamento efetivo de material autorizado referencia fonte, trecho/progresso e versão; sinopse gerada pelo modelo não prova contato. Leitura sequencial não recebe desfecho antes da hora. Legenda não comprova detalhes visuais ausentes.

Separar fonte, impressão situada, inferência e reconstrução fictícia. Registrar consequências úteis e seu fundamento, com níveis de detalhe/budget, sem biografia gigante sem efeito comportamental. Preservar eventos/resultados aprovados: seed não reproduz respostas remotas.

Repertório é política de acesso/expressão e avaliação, não apagamento dos pesos. Testes futuros com obra desconhecida, contato parcial e revisão posterior devem detectar spoilers e conhecimento sem suporte. Relações e ficção nunca entram implicitamente no corpus factual Damma. Percentual fixo/emergente da personalidade e tenancy por personagem permanecem abertos; não bloqueiam a primeira fatia técnica.

### 12.2 Dota Coach

Não foi localizada integração Dota nas fontes Filomena pesquisadas. Primeira entrega de domínio deve ser análise retrospectiva, não bot jogador: parser, patch, estado temporal, eventos/features e métricas determinísticas com evidências. Janela de 10/30/60 segundos é política de análise, não agenda obrigatória de LLM. Uma decisão prolongada não vira vários erros.

Guardar visão disponível à jogadora naquele instante separada da reconstrução onisciente posterior. Inventário dos dois times distingue observação, intenção comunicada, hipótese, previsão e recomendação, com timestamp/origem/staleness. Não confundir espera/posicionamento com inutilidade, nem inferir intenção com certeza.

Padrões geram hipóteses revisáveis e exercícios; a política de adaptar versus contestar estilo fica no produto. A plataforma recebe inputs/refs de estado autorizado; não conhece heróis/lanes/builds.

HelenUI foi localizado: designer, reconhecimento por screenshot/OCR e `PerformActionRequest` no navigator-service. Isso comprova contratos existentes, não integração Dota, autorização de atuação ou adequação competitiva. V4 separa observe→update state→decide→propose→authorize→execute→verify; selecionar um alvo já pode ser efeito. Nenhuma captura/input/treinamento/anti-cheat é implementado agora. Ritmos distintos e processamento local permanecem possíveis sem runtime distribuído novo.

### 12.3 Damma

O crawler existente já modela `Document`, payload bruto, `DocumentExtraction`, `EditionPage` com SHA-256 e runs/attempts/cobertura histórica. Reutilizar esse domínio e suas fontes, não importar todo o acervo como fatos sociais Graphiti. A auditoria externa foi limitada; não comprova a autorização atual de toda a API Damma.

Evolução de domínio: cada afirmação/conclusão referencia documento, passagem/página, extração/OCR versionados e transformações. Correção de extração identifica derivados afetados e preserva a revisão. Separar data de publicação/evento, entidades candidatas e cobertura do corpus.

Modelar fonte comum/republicação, corroboração independente, contradição, correção, hipótese e inconclusão. Não contar republicações como confirmações independentes; ausência no corpus não prova inexistência. Falsidade verificável não prova intenção de enganar; incentivos/proximidade não provam coordenação. Não criar ranking universal de mentira ou orientação política.

Investigações podem explorar filhas sob escopo/budget concedidos. Publicação externa, acesso restrito e aumento de budget são permissões separadas. Políticas de triagem, verificação pontual, cruzamento e aprofundamento terão valores explícitos aprovados posteriormente, não defaults inventados nesta especificação.

## 13. Orçamento, execução durável e observabilidade

Preservar a oficina e concluir sua aceitação antes de reutilizar garantias transacionais. `AttemptLedger` bloqueia experimento/trial e grava reserva/tentativa; `BudgetReservation` limita calls/tokens/custo. Isso não implementa budget por projeto/período/tipo ou raiz de investigação. [E19]

Contrato futuro necessário para autonomia Damma: uma concessão raiz identificável vincula projeto/período, tipo de investigação e limite da investigação. Filhas/subagentes herdam a raiz e consomem a mesma disponibilidade; novos IDs não criam crédito. Reserva por efeito verifica todos os tetos aplicáveis atomicamente, com ordem de locks definida e testes concorrentes. Concessão independente adicional só pelo host autorizado.

Contabilizar inferência, OCR, APIs, documentos/etapas, processamento local, duração e concorrência conforme a tarefa. Unidade e custo monetário são distintos: local/BYOK não é custo zero. Custo observado, estimado, desconhecido e unidade não precificada permanecem identificáveis.

Política proposta: modo financeiro estrito recusa efeito sem bound conservador aprovado; modo limitado por unidades sem preço pode existir apenas por grant explícito, sem alegar teto monetário. Attempts incertos retêm reservas até reconciliação. Atingir limite pausa novas etapas, preserva progresso/cobertura/dúvidas e não encerra investigação como conclusão factual.

Reutilização futura deve extrair primitives necessárias depois da aceitação, sem fazer runtime depender do DB Evaluation nem adicionar billing comercial. Não presumir teto externo perfeito, especialmente em retries/fallbacks internos de providers.

Correlação mínima: isolation scope, principal opcionalmente pessoa representada, run/parent/root, step, attempt, revisão, deployment/model observado e efeito. `SessionTraceEvent` já tem execução/batch/projeto/estado/node/duração; o trabalho é completar/mapeá-lo, não substituí-lo. Traces privados e artifacts exigem acesso e retenção; logs normais usam refs e códigos, não prompts/documentos/secrets ou raciocínio interno privado.

## 14. Roadmap de fases, não plano de implementação

| Fase | Valor observável | Dependências e gate |
|---|---|---|
| 0: revisão desta especificação | Vocabulário, boundary e escopo inicial acordados | Aprovação humana; nada operacional |
| 1: inferência scoped local | Completion não conversacional e flow atual sob a mesma regra host-owned | Testes controlados, sem endpoint público/DB/migração/modelos reais |
| 2: aceitação pendente da oficina | Evidência durável e execução contabilizada verificadas | Retomar plano 2026-09-26: PostgreSQL isolado, concorrência, falha/reconciliação, restore; provisioning separado |
| 3: ownership dos stores e memória | Novo consumidor não acessa dados/credenciais Filomena | Inventário legado, bindings, negações por ID/relação/cache/job/export; integração banco/SpiceDB autorizada |
| 4: primeiro consumidor não conversacional | Uma tarefa útil de Damma ou replay Dota, com dados autorizados e output evidenciado | Escolher produto/fatia; sem reconstruir domínio inteiro; orçamento necessário validado |
| 5: jobs e autonomia limitada | Retomada e orçamento agregado de investigações/formação | Checkpoints próprios, revogação, concorrência, crash e unidades locais; sem scheduler universal |
| 6: API estável e extensões | Consumidores externos com contratos revisados | Autenticação real, threat model, streaming seguro se necessário; SDK depois |
| Horizontes | Biografia cultural extensiva, coach online/atuação V4 | Specs próprias; não pré-requisito para fases anteriores |

A fase 1 pode ser desenvolvida offline durante a migração do servidor. A fase 2 não é descartada nem considerada concluída por mudança de visão. Budgets gerais e jobs duráveis não devem ser anunciados antes de sua aceitação. Não há deploy automático após qualquer fase.

## 15. Primeira fatia recomendada

**Nome de trabalho:** composição host-bound de inferência, compatível com o runtime conversacional.

Objetivo: provar que um input estruturado não conversacional e o flow Filomena usam o mesmo boundary de escopo/perfil/authority, sem exigir chat no primeiro nem alterar comportamento do segundo.

Reutilizar `IFlowCompletion`, `SessionFlowRuntime` com `completionFactory`, adapters `ProviderFlowCompletion`/SystemOne/OpenAI/LM Studio e os precedentes de pinning da Evaluation. Não importar `EvaluationAccess` inteiro para o Core nem criar um segundo executor de conversa. O plano detalhará o menor wrapper/contexto host-owned após aprovação.

Na fatia inicial:

- Uma concessão/binding de host seleciona scope, principal, perfil e recursos autorizados; client payload não cria grants.
- Acesso ao adapter de teste exige vínculo válido; revogação nega próximo efeito, mesmo com projeto pinado.
- Uma tarefa de classificação estruturada usa JSON sem SessionKey/persona/memória. Não chamá-la de Dota/Damma real.
- Um teste executa o flow atual com a composição bound e respostas controladas, mantendo direct rule, estado, Boundary e Shadow.
- Providers reais não são acionados. Não há API pública, novos stores, migração, budget hierárquico ou jobs.
- Não alterar hashes/IDs dos artifacts existentes. O wiring operacional permanece desligado; ativação é futura e separada.

Isso prova um boundary útil, **não isolamento completo da plataforma**. Novos consumidores live continuam bloqueados até ownership dos stores e accounting aplicável serem aceitos.

### Critérios de aceite propostos para essa fatia

1. Mesmo input e mesmo modelo em scopes A/B não permitem escolher binding/credential/artifact de A a partir de B; wrong scope é rejeitado antes do adapter.
2. `project_id`, namespace, URL ou role fabricados no payload não concedem permissão. Outputs/tool proposals também não.
3. Inferência estruturada executa com fixture sem persona, conversation, memória ou sender; schema/capability incompatível falha antes do efeito; resposta inválida não vira completed.
4. O flow v3 conserva direct nos três estados, sem Presença no caminho direto; caminhos indiretos seguem o grafo existente, sem novo tuning.
5. Shadow não chama sender nem confirma fala/transição; fixture MemoryItem percorre compiler/enforcement reais, incluindo regeneration e final Block.
6. Cancelamento antes do dispatch não chama adapter; cancellation/erro após submission permanece distinguível. Não há fallback cloud nem retry implícito.
7. Grant revogado enquanto aguarda processamento bloqueia o próximo dispatch; revision pin não serve como grant permanente.
8. Trace correlaciona scope/run/step/attempt/config sem segredo; resultados de fixtures ficam rotulados como controlados.
9. Ausência de PostgreSQL Evaluation não muda o perfil do runtime; nenhum teste liga App/Gateway/Graphiti/LM Studio ou container.

Os testes existentes E03, E07, E09, E12, E14, E15 e E20 são candidatos à regressão após inspeção dos respectivos opt-ins/fixtures. O plano deverá delimitar uma allowlist segura, não executar a solução inteira por padrão.

### Testes de fases posteriores

- Banco real: FK cross-scope, leitura direta por ID, holdout/redaction, última reserva concorrente, crash/restore, migration checksum e registros indeterminate.
- Memória: mesma pessoa em dois produtos, mesma conversa em duas contas, grants revogados, relações `involves/shared_by`, binding legado e negação de corpus ficcional em Damma.
- Jobs: filhos sob a mesma raiz, revalidação após resume, sem reset de budget por novo ID, checkpoints sem duplicar efeitos.
- Produtos: repertório e processamento parcial sem spoiler; Dota sem hindsight/patch errado; Damma com passagem/extração, fontes comuns e revisão de conclusões.
- Streaming/tools: buffer sob enforcement, tool parcial rejeitada, fallback compatível com política de dados.

Não afirmar qualidade de personagem, coach ou investigação a partir de fixtures de integração.

## 16. Compatibilidade, documentação e decisões abertas

### 16.1 Documentação canônica

Esta especificação é um complemento de plataforma aos dois eixos existentes. Corrige a interpretação do overview: **dois eixos de infraestrutura, três produtos consumidores**. O overview permanece baseline histórica; não foi reescrito.

Preservar como documentos canônicos da oficina:

- `docs/superpowers/specs/2026-09-26-evaluation-workbench-design.md`;
- `docs/superpowers/plans/2026-09-26-evaluation-workbench.md`;
- `docs/evaluation-offline-status.md` e `docs/evaluation-cli.md` para checkpoints/contrato implementado.

Após aprovação, acrescentar links de relação e notas de status nesses documentos, sem apagar resultados anteriores. O novo plano deve cobrir somente a primeira fatia e referenciar tarefas pendentes da oficina, não substituí-las. Nenhum plano detalhado novo ou commit foi criado nesta entrega.

### 16.2 Decisões materiais ainda abertas

| Decisão | Proposta/estado | Quando precisa resolver |
|---|---|---|
| Primeira fronteira e forma de execução | Aprovar a fatia host-bound acima; depois plano e escolha da execução | Antes de implementar |
| Character/client por projeto | Não decidido; sem compartilhar implicitamente entre produtos | Antes de onboarding multi-personagem/cliente real |
| Personalidade inicial versus emergente | Preservar Filomena atual; não fixar percentuais | Spec do motor de experiências |
| Primeiro domínio não conversacional | Damma já tem dados/modelos; Dota replay também é candidato, não seleção automática | Antes da fase 4 |
| Budgets por tipo/período e custo desconhecido | Mecanismo descrito, valores não definidos | Antes de autonomia/inferência real correspondente |
| Estilo de coaching | Adaptar versus contestar estilo é decisão de produto | Antes de avaliação pedagógica |
| Modelo de autenticação externa/BYOK | Host local primeiro; contrato público ainda não estabilizado | Antes de REST/MCP/SDK público |

Não é necessário responder às decisões de produto distantes para revisar esta especificação. Não há solicitação de credenciais, dados pessoais ou ativação.

### 16.3 Limitações explícitas da auditoria

Não foi estabelecido: estado live de bancos/serviços, grants implantados, coverage completa de memória, todos os callers upstream OpenClaw, maturidade operacional Damma/HelenUI, ranking atual de modelos, qualidade dos produtos futuros, nem sucesso do build/testes do checkout sujo. Não foram inspecionados acervos privados ou registros brutos para suprir essas lacunas.

Foram estabelecidos por fonte: existência dos módulos/contratos citados, separação de assemblies, acoplamentos de identidade, falta de scope/authority no request de completion, fronteiras locais do playground, persistência específica e gaps de orçamento/API. Esses achados sustentam a recomendação incremental sem exigir um redesign completo.

## 17. Encerramento desta etapa

**Entrega:** auditoria e especificação proposta, prontas para revisão.  
**Implementação da migração:** não iniciada.  
**Builds/testes nesta auditoria:** não executados.  
**Bancos, serviços, modelos, ingestão, personagem e automação de interface:** não acionados.  
**Commits/pushes:** nenhum.

Próximo passo somente após aprovação desta especificação: detalhar o plano incremental da primeira fatia, preservando as pendências reais do workbench. Aprovação da especificação não autoriza inferência paga, migrations, deploy ou ativação.
