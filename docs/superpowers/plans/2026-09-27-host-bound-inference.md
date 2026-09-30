# Inferência host-bound: plano da primeira fatia

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Implementação depende de aprovação deste plano e escolha da execução. Subagentes e revisão independente exigem autorização própria; não iniciá-los automaticamente.

**Goal:** Provar inferência estruturada sem chat e compatibilidade do flow conversacional sob uma mesma fronteira de escopo, perfil e autoridade do host, somente com adapters controlados.

**Architecture:** Acrescentar uma composição interna sobre `IFlowCompletion`, mantendo os adapters e o executor atuais. O host vincula escopo e concessão a perfis imutáveis; cada chamada verifica autoridade vigente antes de invocar o adapter. Uma extensão compatível da fábrica de completion transmite o contexto real do batch, sem colocar autoridade no projeto ou no prompt.

**Tech Stack:** C# / .NET 10, System.Text.Json, xUnit v3 e TimeProvider; sem novos pacotes, banco, servidor, provider remoto ou assembly.

**Spec:** [Evolução incremental da plataforma](../specs/2026-09-27-intelligence-platform-evolution-design.md), especialmente §§8–9 e 15. Especificação aprovada para detalhamento por “pode ir”; plano e execução sequencial aprovados por “pode ser”.

**Checkpoint 2026-09-27:** primeira fatia implementada localmente, sem ativação/commit. [Contrato, evidências e desvios](../../host-bound-inference.md). Os checkboxes abaixo preservam o plano original; o registro de execução resume cada tarefa e seus equivalentes implementados:

- Tarefa 1: contratos/cópias e baseline; concluída.
- Tarefas 2/3: authority, decorator, negações/revogação, observação conservadora; concluídas.
- Tarefa 4: contexto de batch e binding enumerado, inclusive review/preview; concluída.
- Tarefa 5: caracterização v3 e regressões focadas verdes; concluída no escopo. A suite ampliada continua com 19 falhas fora desta fatia, explicitamente documentadas. Nenhuma aceitação de solução inteira/live.

## Global Constraints

- Preservar brain, prompts, artifacts, modelos, FSM, direct rule, memória, Boundary e SendGuard. Não traduzir nem normalizar artifacts existentes.
- Sem mudança operacional, inferência real, Graphiti, PostgreSQL, Docker, listener, endpoint público, ingestão, sender ou automação de interface.
- Sem commits/pushes nesta autorização. Checkpoints de diff substituem passos de commit; autorização de implementação não implica autorização operacional.
- Preservar o checkout sujo em `main`. Sem reset, stash automático, limpeza ou worktree novo sem necessidade/autorização. A execução registra baseline antes de editar.
- Não referenciar Evaluation a partir de Core/runtime. Não criar outro contexto de conversa, router, scheduler, ledger ou framework de autenticação.
- Identificadores de configuração não autorizam acesso. Autoridade nunca vem de inputs, outputs ou ferramentas propostas pelo modelo.
- Não anunciar isolamento completo de memória, APIs ou produtos com base nesta fatia de inferência.
- Custo/tokens desconhecidos permanecem desconhecidos; início do adapter não comprova submissão remota. Cancelamento não comprova ausência de consumo.
- Testes/builds só depois de inspecionar projetos, hooks e fixtures envolvidos. Ausência de dependências offline bloqueia esse comando, não autoriza restore/rede.
- Nenhuma mudança de schema/dados. Desativação é retirar a nova composição futura; os roots operacionais não serão ligados a ela nesta fatia.

## Review Focus

1. Uma sessão com batch antigo recebe publicação nova enquanto aguarda: usar a revisão do batch, não a mais recente. Tarefa 4.
2. Revogação ocorre durante espera ou validação: nova chamada deve ser negada antes do adapter; chamada já admitida não é magicamente desfeita. Tarefas 2/3.
3. Caller reutiliza concessão de outro host/projeto ou modifica coleção/JsonElement após binding: negar troca e preservar cópia imutável. Tarefas 1/2.
4. Erro ou cancelamento após iniciar o adapter carrega texto privado: trace somente códigos e estado conservador, sem payload e sem retry automático. Tarefa 3.
5. Correção de formato, regeneração e chamadas concorrentes compartilham NodeId: IDs de tentativa distintos, política atual preservada, sem transformar schema inválido em sucesso ou duplicar chamada. Tarefas 3/5.

## 1. Baseline e relação com o trabalho existente

Inspeção para este plano: `C:\dev\filomena`, HEAD da auditoria `1f090b6609e56a28871c28128e5b1e83a6282730`, muitos arquivos modificados/não rastreados preexistentes. Nenhum teste ou build foi executado ao escrever este plano.

Pontos reais:

- `src/Filomena.Core/Flows/TypedFlowRunner.cs`: `FlowCompletionRequest`, `IFlowCompletion.CompleteAsync`, `TypedFlowRunner.RunSessionAsync`.
- `src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.cs`: overloads existentes com `IFlowCompletion` e `Func<SessionKey, IFlowCompletion>`.
- `src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.Execution.cs`: `completionFactory(batch.Session)` dentro da execução; o batch já tem ExecutionId, BatchId, Session e Project pinado.
- `src/Filomena.Core/Flows/FlowTypes.cs`: `FlowType.ParseJsonSchema`, `FlowSchemaCodec` interno; não há arquivo separado `FlowSchemaCodec.cs`.
- `src/Filomena.Core/Flows/FlowDomain.cs`: `FlowDomain.Check`; é validação do domínio existente, não implementação universal de JSON Schema.
- `tests/Filomena.Core.Tests/Flows/SessionFlowRuntimeTests.cs`, `Boundaries/BoundaryRuntimeTests.cs`, `Outbound/ShadowRuntimeTests.cs`.
- `tests/Filomena.Llm.Tests/SessionParticipationReviewTests.cs`: caracterização de direct rule; pertence à suite Llm, não à pasta Core/Flows.

O plano [Evaluation Workbench](2026-09-26-evaluation-workbench.md) permanece ativo e independente. Esta fatia não substitui suas tarefas nem conclui aceitação PostgreSQL, concorrência, restore ou migrations. `docs/evaluation-offline-status.md` é checkpoint histórico, não resultado desta execução.

## 2. Arquivos e responsabilidades propostas

Todos os arquivos novos abaixo são propostas, não implementações existentes.

| Arquivo | Ação e responsabilidade |
|---|---|
| `src/Filomena.Core/Inference/HostInferenceContracts.cs` | Criar: escopo, correlação, perfil e observação sem payload privado |
| `src/Filomena.Core/Inference/HostInferenceAuthority.cs` | Criar: concessão opaca local, binding, revogação e admissão síncrona |
| `src/Filomena.Core/Inference/HostBoundFlowCompletion.cs` | Criar: decorator IFlowCompletion; validação, chamada única e observação |
| `src/Filomena.Core/Inference/HostBoundSessionCompletionFactory.cs` | Criar: composição por contexto real de batch e binding explícito de sessão |
| `src/Filomena.Core/Flows/Sessions/SessionCompletionContext.cs` | Criar: contexto mínimo e interface de fábrica por execução |
| `src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.cs` | Modificar: overload compatível da fábrica, sem mudar os existentes |
| `src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.Execution.cs` | Modificar somente criação do completion a partir do contexto do batch |
| `tests/Filomena.Core.Tests/Inference/HostInferenceAuthorityTests.cs` | Criar: negações de escopo, mutação e revogação |
| `tests/Filomena.Core.Tests/Inference/HostBoundFlowCompletionTests.cs` | Criar: inferência avulsa, contrato, tentativas, erro/cancelamento |
| `tests/Filomena.Core.Tests/Inference/HostBoundSessionCompletionTests.cs` | Criar: compatibilidade do runtime, pinning e isolamento de composição |
| `tests/Filomena.Core.Tests/Inference/HostInferenceTestFixtures.cs` | Criar: completions em memória, relógio e barreiras TaskCompletionSource |
| `docs/host-bound-inference.md` | Criar ao concluir: contrato implementado, limites, comandos/resultados realmente executados |

Não alterar App/Program, providers reais, projetos operacionais JSON, reader/writer ou bancos. Não expor credenciais por esse contrato: o adapter é fornecido já configurado pelo host e nunca devolvido ao caller.

## 3. Contrato da fatia

Os nomes abaixo fixam a interface entre tarefas; podem receber ajustes de compilação idiomáticos, mas não mudança de autoridade/semântica sem atualizar o plano.

### Dados host-owned e ponto de admissão

- `InferenceScope(string ProjectId)`: identificador opaco não vazio, comparação ordinal; não é SessionFlowProject.ProjectId nem EvaluationScope.
- `InferenceCorrelation(string RunId, string StepId, string ConfigurationRevision)`: criada pela composição, não lida dos inputs. AttemptId é novo a cada entrada efetiva no decorator, inclusive correção de formato.
- `HostInferenceGrant`: classe selada sem constructor público; pertence à instância de authority que a emitiu. Expõe no máximo referências seguras, nunca segredo/adapter.
- `HostInferenceAuthority.Grant(InferenceScope scope, string principalRef, IReadOnlyList<HostInferenceProfile> profiles) -> HostInferenceGrant`.
- `HostInferenceAuthority.Revoke(HostInferenceGrant grant) -> void`: idempotente na mesma authority; grant estrangeiro não pode alterar a outra authority.
- `HostInferenceAuthority.Bind(HostInferenceGrant grant, InferenceScope scope, string profileId, InferenceCorrelation correlation) -> IFlowCompletion`.

`HostInferenceProfile` é configuração confiável criada pelo host, não DTO público. Contém ProfileId/Revision, o adapter IFlowCompletion, restrições explícitas de request e validação de resposta. A composição registra perfis por tarefa/node; não recebe URL, segredo ou provider novo do payload.

Restrições de request fixam NodeId, Provider, Model, ReasoningEffort, MaxOutputTokens e ResponseSchema esperados, incluindo null como valor explícito. Usar igualdade estrutural para schema, não igualdade de texto JSON. Inputs/System/Correction continuam dados dinâmicos do flow; não concedem capabilities ou alteram o adapter. Não acrescentar autoridade à serialização para o modelo.

O validador de resposta é uma função host-owned `Action<string>` do perfil. Na tarefa avulsa, usar schema fechado aprovado e `FlowType.ParseJsonSchema` + `FlowDomain.Check`, acrescentando verificação de propriedades excedentes se não coberta pelo validador existente. No flow, reutilizar os validadores existentes e manter o ponto atual de correção/validação final; não fazer um novo checker universal nem estreitar silenciosamente o dialect de schema aceito. Falha de validação não gera observação Completed. Falhas devem seguir o mecanismo existente de correção quando aplicável, com nova tentativa autorizada, não retry escondido no decorator.

Binding copia defensivamente coleções e JsonElements. Perfis são imutáveis; mudar perfil exige novo grant/binding. `Bind` valida identidade/scope/perfil, mas **não congela autorização**. Cada `CompleteAsync` valida novamente.

Admissão e revogação têm um ponto de ordenação explícito, protegido por lock curto na authority. Após preparar e validar o request, admitir a tentativa se grant continua vigente. Não manter lock durante I/O nem usar callback remoto sob lock. Revogação antes da admissão nega; depois dela impede chamadas seguintes, não promete cancelar a tentativa já admitida. Não há await entre admissão e invocação do adapter. Esse limite é documentado, não uma promessa de revogação transacional no provider externo.

Essa authority é um registro de permissões **local ao processo e à composição confiável**, não login, token de API, substituto de SpiceDB ou sandbox contra código arbitrário em processo. Os providers brutos existentes continuam internos aos roots legados não migrados.

### Observação e falha

`InferenceAttemptObservation` registra ProjectId, PrincipalRef, RunId, StepId, AttemptId, ConfigurationRevision, ProfileId/Revision, duração monotônica, estágio e código de resultado. Não registra System, Inputs, output, credenciais ou Exception.Message externo.

Estágios: `DeniedBeforeInvocation`, `AdapterInvoked`, `Completed`, `InvalidOutput`, `FailedAfterInvocation`, `CancelledAfterInvocation`. Os dois últimos não afirmam se o provider recebeu a chamada. Usage/custo ausentes continuam null/unknown; Provider/Model solicitados não são apresentados como modelo efetivamente observado. Nesta fatia não há nova coleta de usage remoto, reserva financeira ou persistência de attempts.

Sink de observação é local e não pode disparar inferência ou transporte. Falha do sink após efeito não provoca retry nem apaga o resultado; registrar indisponibilidade diagnóstica por estado separado do resultado. Sem alegar audit durável/financeiro com esse sink.

### Contexto de sessão

`SessionCompletionContext` carrega SessionKey, ExecutionId, BatchId, ProjectId, ProjectVersion e ProjectRevision do batch.

`ISessionFlowCompletionFactory.Create(SessionCompletionContext context) -> IFlowCompletion`.

Adicionar overload do constructor de `SessionFlowRuntime` com essa interface no segundo parâmetro. Os overloads antigos permanecem e adaptam a fábrica antiga ignorando campos novos. Não adicionar overload de Func com outro parâmetro de lambda: isso criaria ambiguidade para callers existentes.

`HostBoundSessionCompletionFactory` recebe uma resolução **host-owned** `Func<SessionCompletionContext, IFlowCompletion>` que verifica o binding exato de sessão/projeto/revisão e produz um completion que seleciona somente os perfis de nodes daquela revisão. Não resolve autoridade de SessionKey isolado ou do JSON do projeto. Ausência/ambiguidade nega. Não usar AsyncLocal/global mutable para correlação.

## 4. Tarefas executáveis após aprovação

### Tarefa 1 — baseline seguro e contratos imutáveis

**Arquivos:** novos Contracts/TestFixtures/AuthorityTests da tabela; ler projetos e build hooks existentes, sem alterá-los.

**Interfaces:** produz InferenceScope, InferenceCorrelation, HostInferenceProfile e observação conforme §3.

- [ ] Registrar HEAD/status e hashes dos arquivos que serão editados. Não copiar configs privadas. Confirmar que nenhum alvo já contém trabalho paralelo conflitante.
- [ ] Inspecionar `Directory.Build.*`, imports MSBuild, referências de Core/Core.Tests/Llm, fixtures e inicializadores; registrar allowlist sem serviço/rede/processo externo. Core.Tests referencia Core e Llm, não App; confirmar no momento da execução.
- [ ] Escrever testes `EmptyScopeRejected`, `ProfileCopiesMutableInputs`, `SchemaEqualityDoesNotDependOnPropertyOrder`, `RequestedModelIsNotObservedModel` e `UnknownUsageIsNotZero`.
- [ ] Executar filtro novo com `--no-restore`; falha esperada por tipos ainda ausentes. Falha ambiental não conta como RED de comportamento.
- [ ] Implementar somente contratos e cópias defensivas, sem alterar FlowCompletionRequest ou persistência.
- [ ] Executar mesmo filtro e verificar GREEN. Registrar diff próprio, sem commit.

**Aceite:** não é possível construir perfil que mude após binding por mutação de lista/schema; nenhum novo pacote ou root operacional.

### Tarefa 2 — autoridade local e revogação por tentativa

**Arquivos:** HostInferenceAuthority.cs, HostInferenceAuthorityTests.cs.

**Interfaces:** produz Grant/Revoke/Bind do §3; Bind retornará decorator da tarefa 3, com stub estritamente local durante o ciclo desta tarefa se necessário.

- [ ] Escrever `GrantFromAnotherAuthorityRejected`, `WrongScopeRejectedBeforeAdapter`, `UnknownProfileRejected`, `RevokedGrantRejectedAtBind`, `BoundCompletionCannotSurviveRevocation`, `PayloadCannotChooseCredentials`, `TwoScopesWithSamePrincipalDoNotShareGrant`.
- [ ] Escrever teste concorrente determinístico `RevocationBeforeAdmissionPreventsInvocation`, usando barreira local, sem sleeps; contraprova: tentativa admitida antes da revogação pode terminar, mas próxima é negada.
- [ ] Rodar filtro AuthorityTests e confirmar falhas pertinentes.
- [ ] Implementar registro opaco e ordenação admissão/revogação; não tratar string de projeto/role como grant. Não manter lock durante adapter/sink.
- [ ] Rodar testes e verificar contador adapter=0 em todas as negações; controle autorizado=1. Registrar diff próprio.

**Aceite:** autoridade vigente é necessária em cada tentativa, inclusive bindings antigos, sem vazar recursos entre scopes.

### Tarefa 3 — inferência não conversacional sob o mesmo boundary

**Arquivos:** HostBoundFlowCompletion.cs, HostBoundFlowCompletionTests.cs; completar wiring de Bind.

**Interfaces:** consome contratos e authority; produz IFlowCompletion compatível e observações do §3. Não altera adapters reais.

- [ ] Escrever `StructuredDecisionWithoutSessionPersonaOrMemory`: request JSON controlado, resposta `{"choice":"accept"}`, schema fechado enum accept/reject; zero sender/memory e exatamente uma chamada fake.
- [ ] Escrever `ProfileOverrideRejectedBeforeAdapter` parametrizado por model/provider/schema/effort/max tokens/NodeId; `UnapprovedSchemaRejectedBeforeAdapter`; `InvalidOutputIsNotCompleted`; `ExtraPropertyRejectedForClosedDecision`.
- [ ] Escrever `CancelledBeforeInvocationCallsZero`, `CancellationAfterInvocationRemainsUncertain`, `ExceptionDoesNotExposePayload`, `NoImplicitRetryOrFallback`, `ConcurrentAttemptsHaveDistinctIds`, `ObservationFailureDoesNotReplayAdapter`.
- [ ] Confirmar RED com filtro HostBoundFlowCompletionTests.
- [ ] Implementar validação antes de admissão, chamada única, validação do resultado e observação monotônica. Usar TimeProvider para testes; clonar JsonElement cuja vida ultrapassa JsonDocument.
- [ ] Confirmar GREEN, inclusive saída inválida nunca Completed e nenhum token/custo fabricado. Registrar diff próprio.

**Aceite:** uma tarefa estruturada funciona sem Message/SessionKey/persona; o modelo não seleciona recursos. Limitação: observar o adapter não mede automaticamente HTTP, tokens ou custo.

### Tarefa 4 — contexto correto do batch sem mudar o runtime social

**Arquivos:** SessionCompletionContext.cs, dois arquivos SessionFlowRuntime da tabela, HostBoundSessionCompletionFactory.cs, HostBoundSessionCompletionTests.cs.

**Interfaces:** produz ISessionFlowCompletionFactory.Create/context e constructor adicional; consome authority/decorator anteriores.

- [ ] Escrever `ExistingCompletionConstructorsRemainCompatible`, `FactoryReceivesCapturedBatchRevision`, `TwoConcurrentSessionsDoNotExchangeCorrelation`, `SessionBindingMismatchRejectsBeforeCompletion`.
- [ ] Reutilizar o controle de batching/test clock de SessionFlowRuntimeTests: admitir sob R1, publicar R2 antes de executar, demonstrar que a fábrica recebe R1; segundo batch recebe R2. Não testar apenas duas strings construídas manualmente.
- [ ] Escrever `GrantRevokedWhileBatchQueuedPreventsInference`, com fake blocker no predecessor; não alterar FSM/quiet window para facilitar o teste.
- [ ] Confirmar RED; implementar somente extensão de fábrica e seleção por contexto capturado, sem ler revisão administrativa atual durante execução antiga.
- [ ] Confirmar GREEN e regressões SessionFlowRuntimeTests. Verificar que construtores e chamadas existentes compilam sem ambiguidade.

**Aceite:** mesma identidade/revisão de execução do runtime chega ao boundary; sem ambient context, sem novo scheduler, sem trocar configuração do batch.

### Tarefa 5 — caracterização integrada e encerramento da fatia

**Arquivos:** HostBoundSessionCompletionTests.cs e `docs/host-bound-inference.md`; docs do plano/spec apenas para status e links.

**Interfaces:** nenhuma pública nova. Integra contratos anteriores com fixtures existentes de runtime/Boundary/Shadow.

- [ ] Antes de editar, executar a caracterização segura do flow atual usando fixtures existentes e fixar hashes dos artifacts usados; não sobrescrever artifacts sujos.
- [ ] Acrescentar `DirectRoutePreservedInAllThreeStates`: direct classificado responde sem Decidir Presença; mesmo resultado antes/depois do decorator. Usar fixtures/grafo v3 existentes, não um grafo novo que apenas imita a regra.
- [ ] Acrescentar `NonDirectStillUsesPresence`, `CorrectionAndRegenerationHaveSeparateAuthorizedAttempts`, `FinalBoundaryBlockNeverCallsSender` e `WouldSendDoesNotCommitSpeechOrTransition`.
- [ ] Fixture Boundary deve entrar como MemoryItem pelo compiler real. Não alegar cobertura live; não chamar modelos reais. Preservar os pontos atuais de output inválido/correção, sem validação antecipada que suprima o retry aprovado do runner.
- [ ] Acrescentar `RuntimeWorksWithoutEvaluationDatabase` e `DuplicateInboundDoesNotDuplicateInference`, usando runtime e fakes, sem iniciar Evaluation/App.
- [ ] Rodar a allowlist segura e build limitado abaixo; comparar baseline e resultado. Falhas preexistentes ficam separadas, mas não dispensam os testes novos e contratos tocados verdes.
- [ ] Revisar o próprio diff contra escopo, revogação, pinning, negações, raw provider access e ausência de autoridade no payload. Revisão independente só com autorização separada.
- [ ] Documentar comandos/resultados, limitações, alterações próprias e desativação; atualizar status deste plano sem marcar PostgreSQL ou plataforma inteira como concluídos. Não ativar roots operacionais.

**Aceite:** flow preservado sob a nova composição local; primeira fronteira útil comprovada, nenhuma migração operacional.

## 5. Comandos previstos e gates de execução

Os comandos abaixo **não foram executados nesta entrega**. Rodar a partir da raiz somente após o gate de segurança da tarefa 1. Se assets/packages não estiverem disponíveis offline, reportar a dependência; não restaurar automaticamente pela rede.

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~HostInferenceAuthorityTests|FullyQualifiedName~HostBoundFlowCompletionTests|FullyQualifiedName~HostBoundSessionCompletionTests"
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SessionFlowRuntimeTests|FullyQualifiedName~BoundaryRuntimeTests|FullyQualifiedName~ShadowRuntimeTests"
dotnet build src/Filomena.Core/Filomena.Core.csproj --no-restore
dotnet build src/Filomena.Llm/Filomena.Llm.csproj --no-restore
git diff --check
```

No ciclo TDD, usar somente o filtro da classe em criação. Inspecionar inicializadores globais antes de rodar uma assembly mesmo com filtro.

`SessionParticipationReviewTests` está na suite Llm; executar separadamente somente se seu projeto/targets/referências forem seguros. Se build da suite puxa App ou falha por código preexistente, registrar a limitação e executar a caracterização equivalente do grafo real na suite Core; não lançar aplicação para contornar.

Formatação: verificar apenas arquivos próprios com formatter já disponível/configurado no repositório, sem restore e sem reformatação global. Build C# já impõe warnings-as-errors; não chamar isso de aceitação de integração. `git diff --check` global pode denunciar alterações alheias: registrar origem, não corrigi-las fora de escopo. Arquivos novos também precisam de inspeção, pois ainda não entram no diff rastreado.

Não usar `dotnet test Filomena.slnx`, `dotnet run`, scripts live, Docker ou environment secrets como atalho. Testes PostgreSQL/Docker da oficina não fazem parte desta allowlist.

## 6. Cobertura, riscos e recuperação

| Requisito da primeira fatia | Tarefa / evidência esperada |
|---|---|
| Scope/principal/binding confiáveis; payload não autoriza | 1–3, negações com contador zero |
| Revogação vigente e pin não vira autoridade | 2/4, barreiras concorrentes e batch real |
| Inferência sem conversa/persona/memória | 3, schema fechado e fake único |
| Schema/capability incompatíveis; output inválido | 3/5, perfil pinado + validação já existente |
| Flow direct/indirect, Boundary, Shadow | 5, comparação com grafo e fixtures reais |
| Cancelamento, incerteza, tentativas | 3/5, estágios conservadores e ausência de retry oculto |
| Correlação sem dados privados | 3/4, isolamento concorrente + inspeção de observações |
| Workbench não é dependência runtime | 5, referências de assemblies e teste sem DB |

Sem migration/backfill nesta fatia. Não renomear namespaces/UUIDs. Não haverá dual-write, corte live ou rollback de dados. Roots existentes continuam usando composição atual; a nova composição é exercitada exclusivamente pelos testes.

Riscos principais: extensão da fábrica quebrar overloads; schema validator divergir do runner; permissões confundidas com strings; caller confiar em garantia de cancelamento maior que a existente; testes de solução acionarem efeitos. Os testes e o gate de execução acima delimitam esses riscos.

Não coberto: isolamento real de stores/Graphiti, credenciais multiusuárias/BYOK público, API auth, budget monetário/hierárquico, jobs duráveis, streaming, modelo real, avaliação de qualidade ou operação WhatsApp. Esses itens continuam nas fases da especificação, incluindo aceitação real PostgreSQL da oficina.

## 7. Handoff

Estado original do handoff: plano proposto; implementação posteriormente autorizada e executada conforme checkpoint acima. Execução sequencial pelo agente principal, sem subagentes/revisão independente. Permanecem válidos os limites operacionais abaixo.

Após aprovação do plano e confirmação da forma de execução, implementar somente esta fatia. Qualquer uso pago, banco/serviço, migração, deploy, commit ou ativação operacional exige autorização aplicável separada. Ao terminar, apresentar evidência local e limitações; não anunciar plataforma migrada.
