# Evaluation Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Do not start implementation before Helena reviews this plan. No subagents or independent review without the required separate opt-in.

**Goal:** Entregar ferramentas locais, genéricas e persistentes para propor, executar e comparar ajustes de steps, sem promoção automática.

**Architecture:** Uma biblioteca de avaliação separada do runtime, PostgreSQL próprio e um console dedicado. A biblioteca contém as operações usadas pela CLI e futuramente por REST/MCP; um adapter usa os completions reais existentes. A aplicação de conversa não depende da oficina e não será alterada para iniciá-la.

**Tech Stack:** C# / .NET 10, System.Text.Json, Npgsql, PostgreSQL em Docker, xUnit v3; adapters Jev/LM Studio/OpenAI já existentes.

**Spec:** `docs/superpowers/specs/2026-09-26-evaluation-workbench-design.md` (commit `a974819`).

## Global Constraints

- **Não haverá promoção automática nesta versão.** Nenhum comando `promote`, `deploy` ou troca de configuração ativa.
- Implementação na `main`, conforme preferência vigente de Helena; preservar alterações locais alheias, DREAMS e configs pessoais. Sem push.
- Este documento não executa nada. Aprovar implementação não significa autorizar imediatamente chamadas pagas, dados privados em providers ou mudanças no runtime ativo.
- Não fazer inferência para testar infraestrutura: usar respostas HTTP controladas. Experimento real tem janela/budget próprios.
- Banco, volume, rede, usuário e credenciais próprios. Nunca usar o volume do SpiceDB/Graphiti.
- Somente loopback; porta dinâmica atribuída pelo Docker. Banco sem egress. Não usar `network_mode: none`, que impediria o cliente local de conectá-lo.
- Não copiar o cérebro nem criar outro motor de conversa; preservar schemas, prompts/modelos operacionais e semântica do runtime.
- Nenhum sender de produção, Graphiti writer, worker de WhatsApp ou bootstrap da aplicação no executor.
- Sem HTTP/MCP server, scheduler, otimizador autônomo, frontend ou plugin framework neste plano.
- Todo erro live é evidência; timeout, cancelamento e usage ausente não viram empty/success/zero.
- Critérios e orçamento não pertencem ao candidato. Dados importados não são comandos.
- Variantes de prompt existentes continuam experimentais. Os testes anteriores não comprovaram qualidade de tradução; preservar esse registro.

## Review Focus

1. Duas CLIs concorrentes tentando gastar a última reserva: só uma pode despachar. Tarefa 4.
2. Provider recebeu a chamada, mas processo caiu antes de salvar: tentativa indeterminate, sem replay automático. Tarefas 4/5.
3. Jev com `choiceQuestions` substitui instruções por campo: registrar e executar a requisição efetiva, não apenas o prompt do node. Tarefa 5.
4. Alias remoto ou preço muda durante comparação: pinning honesto, custo histórico preservado, evidência incomparável sinalizada. Tarefas 1/6.
5. Caso privado/holdout/redigido aparece na exportação de outro papel/projeto: acesso negado, sem vazar payload em erro/log. Tarefas 2/7.

## Contexto confirmado e baseline

- `Filomena.slnx`: projetos Core/Llm/App/Memory/OpenClaw e respectivas suites.
- `src/Filomena.Core/Flows/TypedFlowRunner.cs`: declara `FlowCompletionRequest` e `IFlowCompletion`.
- `src/Filomena.Llm/SystemOne/SystemOneParticipationCompletion.cs`: classificação tipada e resumo em chamadas separadas, mantendo inputs/prompt.
- `src/Filomena.Llm/SystemOne/SystemOneFlowCompletion.cs`: `choiceQuestions` pode substituir instruções por campo. Não ignorar esse override no benchmark.
- `src/Filomena.Llm/OpenAi/OpenAiFlowCompletion.cs`: completion retorna texto; usage não faz parte desse retorno.
- `src/Filomena.Llm/LmStudio/FlowModelCompletion.cs`: usa o cliente existente e o contrato estruturado.
- `runtime/docker-compose.yml`: infraestrutura de memória/autorização existente, fora do escopo de edição.
- Há alterações anteriores não commitadas. Antes de executar, registrar `git status` e baseline sem reset/stash automático.
- Baseline conhecido de outra rodada: Llm 805 pass/47 skip; Core 19 falhas locais (FlowBindingValidatorTests/FlowCompilerTests); OpenClaw erros de compilação em FrontendBuildTests/SettingsStoreTests. Reconfirmar na execução, não tratar esses números como resultado desta rodada.

## Entregas e dependências

| Slice | Tarefas | Resultado revisável |
|---|---|---|
| A: caderno durável | 1–3 | Revisões, casos, avaliações e decisões persistidos; backup restaurável |
| B: execução controlada | 4–5 | Pipeline existente executado com limites e rastro por tentativa |
| C: ferramentas úteis | 6–7 | Comparação, priorização e CLI JSON sem ativação |
| D: primeiro uso | 8–9 | Corpus e perfis preparados; integração local comprovada; live separado |

Dependências: 1 → 2 → 3; 1+2 → 4; 4 → 5; 1+2+5 → 6; 2+4+5+6 → 7; 7 → 8 → 9.
Implementação sequencial recomendada, no mesmo agente. Cada tarefa termina em commit local só dos arquivos próprios.

## Estrutura de arquivos proposta

Novos projetos:

- `src/Filomena.Evaluation/Filomena.Evaluation.csproj`: domínio, store, application service e adapter; referencia Core/Llm, não App/OpenClaw/Memory.
- `src/Filomena.Evaluation.Cli/Filomena.Evaluation.Cli.csproj`: console fino que referencia Evaluation.
- `tests/Filomena.Evaluation.Tests/Filomena.Evaluation.Tests.csproj`: unit/integration com categorias separadas, sem referência a App.

Subpastas de Evaluation: `Contracts`, `Storage`, `Execution`, `Comparison`, `Tools`.
Um tipo/arquivo por responsabilidade coesa, sem hierarchy de repositories genéricos.
Payloads pequenos da V1 ficam no PostgreSQL (`jsonb`/`bytea`) com limites explícitos; não adicionar object store externo agora.

Contratos auxiliares citados nas tarefas ficam em `Contracts/EvaluationContracts.cs`
até seu tamanho justificar separação por domínio: `EvaluationCase` (case/dataset refs,
input/context artifact refs, split e expectativas); `ConfigurationRevision` (revision
ref e perfil validado); `ExperimentResult` (id/status/contagens/attempt refs);
`UsageObservation` (valores nullable, unidade, origem); `ProviderObservation`
(status/model/request ref/usage nullable); `ComparisonReport` (refs, elegibilidade,
métricas e evidências); `IssueQuery` (scope, tags, período, paginação);
`IssueSummary` (categoria, severidade, frequência, denominador e evidence refs);
`ToolRequest`/`ToolResponse` são definidos na tarefa 7. IDs sempre scope-bound.
Não implementar tipos JSON permissivos que ignorem versão ou campos desconhecidos.

### Tarefa 1: revisões e manifestos executáveis

**Files:** criar os três `.csproj`, registrar em `Filomena.slnx`; criar `Contracts/EvaluationContracts.cs`, `Contracts/RevisionHasher.cs`, `Contracts/ManifestValidator.cs`; testes `RevisionTests.cs`, `ManifestTests.cs` no novo projeto.

**Interfaces:**
- `EvaluationScope(Guid WorkspaceId, Guid ProjectId)`; `RevisionRef(Guid Id, string Sha256)`.
- `RevisionDocument(int ContractVersion, string Kind, JsonElement Payload)`; `RevisionHasher.Hash(RevisionDocument document) : string`.
- `ExperimentManifest`: scope, target/configuration/dataset/evaluator/policy refs, baseline, candidatos, repetitions, seed, ordem, limites e stop rules. `ManifestValidator.Validate(ExperimentManifest manifest) : IReadOnlyList<ValidationIssue>`.
- `ValidationIssue(string Code, string Path)`; não incluir segredo/valor inválido no diagnóstico.

- [ ] Escrever testes: mesma ordem de propriedades produz mesmo hash; mudança no prompt/threshold/choiceQuestions altera hash; arrays mantêm ordem; duplicata de chave JSON, versão desconhecida, parâmetro não suportado e refs cross-project são rejeitados, sem chamada externa.
- [ ] Rodar `dotnet test tests/Filomena.Evaluation.Tests -c NativeMemory --filter 'FullyQualifiedName~RevisionTests|FullyQualifiedName~ManifestTests'`; observar falha pelo comportamento ausente.
- [ ] Implementar contratos versionados e hash SHA-256 de JSON canônico: ordenar propriedades ordinalmente, preservar strings e representação numérica aceita pelo contrato, rejeitar propriedades duplicadas. Não normalizar texto de prompt nem prometer equivalência de números arbitrários.
- [ ] Fixar perfis com `recipe=completion|typed-with-summary`; registrar provider/model/endpoint alias, parâmetros, threshold, choiceQuestions, schema e prompts efetivos. Snapshot de configuração inclui árvore completa; refs não podem apontar para alias mutável sem snapshot.
- [ ] Rodar mesmos testes; PASS e nenhuma chamada HTTP. Commit `feat(evaluation): add immutable experiment contracts`.

### Tarefa 2: store PostgreSQL e autoridade local

**Files:** criar `Storage/PostgresEvaluationStore.cs`, `Storage/Migrations/001_evaluation.sql`, `Storage/MigrationRunner.cs`, `Contracts/EvaluationAccess.cs`; testes `StoreTests.cs`, `AccessTests.cs`.

**Interfaces:**
- `EvaluationAccess`: principal host-resolved, scope, operações permitidas e acesso a holdout/artifacts; nunca construído a partir do manifesto do agente.
- `PostgresEvaluationStore.PutRevisionAsync(EvaluationAccess access, RevisionDocument document, CancellationToken ct) : Task<RevisionRef>`.
- `GetRevisionAsync(EvaluationAccess access, RevisionRef revision, CancellationToken ct) : Task<RevisionDocument>`.
- `AppendAssessmentAsync(EvaluationAccess access, Assessment assessment, CancellationToken ct) : Task<Guid>`.
- `RedactArtifactAsync(EvaluationAccess access, Guid artifactId, string reason, CancellationToken ct) : Task`.
- `Assessment`: case/trial/evaluator refs, critério, outcome `pass|fail|unscored`, severity, evidence refs, author kind, supersedes opcional.

- [ ] Escrever testes de roundtrip real, constraints cross-project, tentativa de alterar revisão utilizada, supersessão, dataset reservado e artifact redigido. Redação torna replay indisponível sem destruir a auditoria; usuário da aplicação não pode executar DDL.
- [ ] Rodar testes unitários de acesso; manter testes PostgreSQL como `Category=Postgres`, opt-in explícito e skip com motivo se sem DB. Não reportar skip como integração aprovada.
- [ ] Implementar com Npgsql e SQL parametrizado; selecionar/pinar uma versão compatível com net10.0 no momento da implementação, registrar lockfile, sem versões flutuantes. Transação/advisory lock nas migrações.
- [ ] Criar tabelas para revisions, artifacts, experiments, trials, attempts, assessments, comparisons, hypotheses, decisions, activation_records e audit. FKs compostas incluem scope; constraints para estados/valores; reads sempre scope-bound. Tentativas/leases/orçamento serão completados na tarefa 4.
- [ ] Guardar artifacts privados com autorização obrigatória no application service; bloquear leitura de holdout ao proponente. Papel local vem de arquivo host-owned com ACL, não de `--role admin` ou input JSON. Não alegar isolamento contra o administrador do próprio host.
- [ ] PASS unitários; rodar integração depois da tarefa 3. Commit `feat(evaluation): persist evidence with scoped access`.

### Tarefa 3: banco local isolado e recuperação

**Files:** criar `runtime/evaluation/compose.yml`, `runtime/evaluation/Start-EvaluationDatabase.ps1`, `runtime/evaluation/Backup-EvaluationDatabase.ps1`, `docs/evaluation-operations.md`; testes `DatabaseLifecycleTests.cs`.

**Interfaces:** script de start retorna apenas `containerRef`, `host=127.0.0.1`, `port`, `credentialRef`; arquivo privado fora do repo guarda conexão/segredo com ACL restrita. Backup recebe destino explícito; restore sempre exige volume/container novo.

- [ ] Escrever integração que cria DB descartável identificado por teste, persiste revisão+artifact, reinicia e restaura dump em outro volume; hashes e relações permanecem. Assert loopback/dynamic-port, separação de roles e isolamento de rede.
- [ ] Preparar Compose próprio, sem importar o Compose ativo. Fixar imagem por versão/digest verificados, volume dedicado, rede interna e publicação dinâmica somente em loopback. Verificar o comportamento efetivo no Docker instalado; não declarar isolamento só por ler YAML.
- [ ] Resolver imagem local primeiro; se faltar, indicar aquisição necessária em vez de prometer provisioning totalmente offline. Nunca baixar `latest` silenciosamente. Segredos novos exclusivos da oficina, nunca reutilizar credenciais live.
- [ ] Executar start/integration somente quando implementação/provisioning local estiverem autorizados; descobrir porta no mapping do container, readiness por estado do processo/DB e erro concreto. Espera cancelável, sem sleep fixo como prova de prontidão.
- [ ] Rodar `dotnet test tests/Filomena.Evaluation.Tests -c NativeMemory --filter Category=Postgres` no DB de teste; comprovar restore. Nenhum comando derruba container/volume não criado por esse harness. Commit `feat(evaluation): provision isolated local evidence database`.

### Tarefa 4: execução durável, orçamento e cancelamento

**Files:** criar `Execution/ExperimentCoordinator.cs`, `Execution/AttemptLedger.cs`, `Execution/BudgetReservation.cs`; migration `002_execution.sql`; testes `ExperimentCoordinatorTests.cs`, `BudgetConcurrencyTests.cs`, `CrashRecoveryTests.cs`.

**Interfaces:**
- `PlanAsync(EvaluationAccess access, ExperimentManifest manifest, string idempotencyKey, CancellationToken ct) : Task<Guid>`.
- `RunAsync(EvaluationAccess access, Guid experimentId, CancellationToken ct) : Task<ExperimentResult>`.
- `CancelAsync(EvaluationAccess access, Guid experimentId, CancellationToken ct) : Task`.
- `BeginAttemptAsync(AttemptIntent intent, CancellationToken ct) : Task<AttemptLease>`; `FinishAttemptAsync(AttemptLease lease, AttemptObservation observation, CancellationToken ct) : Task`.
- `AttemptIntent`: experiment/trial/call refs, reservation, owner; `AttemptLease` inclui fencing token. Observation contém status, tempos, usage opcional, sanitized error e artifact refs.

- [ ] Escrever testes: 2 workers disputam reserva de 1 chamada → somente 1 transporte invocado; idempotency key com payload diferente → conflito; lease antigo não pode finalizar trabalho de outro owner; cancelamento impede próximo dispatch; erro de persistência impede dispatch.
- [ ] Escrever crash tests antes/depois do envio: tentativa iniciada sem outcome vira indeterminate, reserva permanece retida, não é reenviada após restart/expiração do lease. Reexecução exige decisão explícita vinculada, não retry automático.
- [ ] Implementar ledger transacional por chamada física, incluindo chamadas internas/retries; validar budget de dinheiro/tokens/calls/concurrency antes de cada dispatch. Se não for possível limitar/estimar conservadoramente uma cobrança, negar modo financeiro estrito com diagnóstico.
- [ ] Implementar relógio monotônico injetável, timestamps UTC e execução determinística da agenda por seed (não da LLM). Ordem intercalada entre candidatos; dataset e stop rules pinados.
- [ ] Resolver status final com itens não iniciados/erros/indeterminate visíveis. `completed` exige contabilização de todas as tentativas, não qualidade aprovada. Falha não libera reserva incerta como se nunca houvesse cobrança.
- [ ] Rodar `dotnet test tests/Filomena.Evaluation.Tests -c NativeMemory --filter 'FullyQualifiedName~ExperimentCoordinatorTests|FullyQualifiedName~BudgetConcurrencyTests|FullyQualifiedName~CrashRecoveryTests'`; PASS com transporte fake e PostgreSQL real nas transações concorrentes. Commit `feat(evaluation): execute bounded durable experiments`.

### Tarefa 5: adapter dos completions reais e observação de chamadas

**Files:** criar `Execution/FlowCompletionEvaluationTarget.cs`, `Execution/EvaluationProviderFactory.cs`, `Execution/EvaluationHttpHandler.cs`, `Execution/ProviderObservationParser.cs`; testes `FlowTargetTests.cs`, `ProviderObservationTests.cs`. Ler, sem reescrever automaticamente: `SystemOneParticipationCompletion`, `SystemOneFlowCompletion`, `OpenAiFlowCompletion`, `FlowModelCompletion` e clientes associados.

**Interfaces:**
- `IEvaluationTarget.ExecuteAsync(EvaluationCase input, ConfigurationRevision configuration, CancellationToken ct) : Task<TargetResult>`.
- `TargetResult`: output artifact, contract result e attempt refs; `EvaluationCase` referencia entrada e contexto pinados, sem autoridade.
- `EvaluationProviderFactory.Create(ConfigurationRevision configuration, EvaluationAccess access) : IFlowCompletion`.
- `ProviderObservationParser.Parse(string provider, int statusCode, ReadOnlyMemory<byte> body) : ProviderObservation`; usage/model/request-id ausentes permanecem null/unavailable.

- [ ] Escrever testes usando os adapters reais e HTTP controlado: Jev recebe os campos tipados e Gemma somente resumo; ambas as chamadas têm attempts próprios; erro na segunda não produz sucesso parcial. Gemma/Luna full-output usam schema inteiro.
- [ ] Escrever caso com `choiceQuestions` não vazio: requisição efetiva e hash refletem overrides. Não declarar comparação de prompts equivalente quando um override ignora justamente a instrução alterada. Perfis e relatório devem mostrar isso.
- [ ] Construir HttpClients exclusivos da oficina com handler que registra/reserva antes do SendAsync físico e observa resposta sem reenviar ou alterar payload/headers do provider. Não usar monkey-patch global nem factory da aplicação live.
- [ ] Parsers preservam usage original permitida e métricas normalizadas com proveniência. Limitar tamanho do corpo de observação; corpo grande não pode provocar retry de chamada já enviada. Failure parsing usage não significa generation empty; status do custo fica unavailable.
- [ ] Pin da factory inclui modelo/config do cliente, não apenas `FlowCompletionRequest`; parâmetros não suportados falham. Credenciais só resolvidas no dispatch autorizado; request/response artifacts nunca guardam Authorization ou cadeia de raciocínio interna.
- [ ] Rodar `dotnet test tests/Filomena.Evaluation.Tests -c NativeMemory --filter 'FullyQualifiedName~FlowTargetTests|FullyQualifiedName~ProviderObservationTests'`; provar zero sender/Graphiti e conferir bytes enviados com HTTP fake. Não mudar semântica dos providers ativos. Commit `feat(evaluation): measure real completion pipelines safely`.

### Tarefa 6: avaliação, custo e comparação útil

**Files:** criar `Comparison/AssessmentEvaluator.cs`, `Comparison/ExperimentComparer.cs`, `Comparison/CostProjection.cs`, `Comparison/InvestigationHistory.cs`; testes `ComparisonTests.cs`, `CostTests.cs`, `InvestigationHistoryTests.cs`.

**Interfaces:**
- `CompareAsync(EvaluationAccess access, Guid experimentId, RevisionRef comparisonPolicy, CancellationToken ct) : Task<ComparisonReport>`.
- `CostProjection.Calculate(UsageObservation usage, PriceSnapshot price) : CostEstimate`.
- `ListIssuesAsync(EvaluationAccess access, IssueQuery query, CancellationToken ct) : Task<IReadOnlyList<IssueSummary>>`.
- `PriceSnapshot`: moeda, fonte/data/vigência e componentes decimais; `CostEstimate` separa valor/unknown e motivo. `ComparisonReport` referencia todas as versões e evidências.

- [ ] Escrever exemplos hand-calculated: 3 repetições de um caso, 2 corretas → consistência 2/3, não 100% por concordância; candidato barato com erro crítico fica inelegível; execução incompleta não vence por ter menos amostras; sucesso repetido em cache não conta como nova observação.
- [ ] Implementar contrato/schema/enum determinísticos e avaliações humanas importadas; julgamento semântico LLM fica opt-in em operação futura, não exigido na V1. Resumo sem avaliação fica unscored, não aprovado.
- [ ] Comparar somente conjuntos pareados e revisões compatíveis; apresentar breakdown por caso/critério, mediana/p95 com N, falhas e incerteza. Taxas com intervalo Wilson 95%; diferenças pareadas com bootstrap por caso/seed fixado na policy quando houver amostra suficiente; marcar amostra insuficiente em vez de inventar precisão.
- [ ] Implementar gates antes de Pareto qualidade/custo/latência. Dimensão desconhecida impede alegar dominância nessa dimensão; moedas diferentes só comparadas com snapshot de conversão explícito, não câmbio implícito.
- [ ] Testar price snapshot A versus B: custo registrado A não muda; projeção B é outro registro. Cobrança ausente ≠ zero. GPU/energia não medidas permanecem unavailable.
- [ ] Persistir hipótese/decisão e consultar recorrência, severidade, custo e evidência. Prioridade recomendada é explicável; não algoritmo que agenda sozinho. Registrar `não mudar` e condição de reabertura. Nenhum resultado promove configuração.
- [ ] Rodar `dotnet test tests/Filomena.Evaluation.Tests -c NativeMemory --filter 'FullyQualifiedName~ComparisonTests|FullyQualifiedName~CostTests|FullyQualifiedName~InvestigationHistoryTests'`; PASS. Commit `feat(evaluation): compare evidence and prioritize investigations`.

### Tarefa 7: application service e CLI para agentes

**Files:** criar `Tools/EvaluationApplication.cs`, `Tools/ToolRequest.cs`, `src/Filomena.Evaluation.Cli/Program.cs`, `src/Filomena.Evaluation.Cli/CommandDispatcher.cs`, `docs/evaluation-cli.md`; testes `CliTests.cs`, `ToolAuthorizationTests.cs`.

**Interfaces:** `EvaluationApplication.InvokeAsync(EvaluationAccess access, ToolRequest request, CancellationToken ct) : Task<ToolResponse>`; request/response possuem `contractVersion=1`, operation e payload versionado; response inclui refs/status/errorCode, nunca segredo.

- [ ] Escrever testes de CLI real com banco de teste e modelo fake: importar → planejar → executar → avaliar → comparar → decidir → exportar; JSON stdout limpo, progresso stderr; SIGINT/cancelamento preserva attempts; comando inexistente não chama serviços.
- [ ] Expor comandos `revision import/show`, `case import/list`, `experiment plan/run/status/cancel/reconcile`, `assessment add`, `compare`, `issues list`, `decision add`, `bundle export`. Implementar parser pequeno fechado, sem shell/SQL livre.
- [ ] Reconciliation exige operação autorizada e evidência; não permitir marcar envio remoto incerto como “nunca ocorreu” só para liberar orçamento. CLI não aceita segredo em argumentos nem papel privilegiado declarado pelo caller.
- [ ] Manifestos arbitrários não escolhem credential path nem endpoint livre. Endpoint/provider alias resolve pelo host e allowlist; bloqueia uso como SSRF/extração de dados. Exportação é scoped/redigida por padrão; payload completo exige permissão específica.
- [ ] Testar cross-project, holdout, artifact redigido, prompt injection no caso e tentativa de `promote/deploy/exec`: acesso negado/unsupported, zero mutação operacional. ACL local protege de agentes não autorizados; não prometer proteção contra administrador do host.
- [ ] Documentar futura paridade REST/MCP como transporte da mesma InvokeAsync, sem publicar servidor nesta tarefa. `activation_records` só registra ação externa por autoridade operacional; não acessível ao avaliador comum.
- [ ] Rodar `dotnet test tests/Filomena.Evaluation.Tests -c NativeMemory --filter 'FullyQualifiedName~CliTests|FullyQualifiedName~ToolAuthorizationTests'`; PASS. Commit `feat(evaluation): expose versioned experiment tools via CLI`.

### Tarefa 8: primeiro corpus e perfis, sem benchmark pago automático

**Files:** criar `evaluation/recipient-v1/cases.synthetic.json`, `evaluation/recipient-v1/profiles.json`, `evaluation/recipient-v1/manifest.template.json`, `evaluation/recipient-v1/README.md`; testes `RecipientManifestTests.cs`.

**Consumes:** contratos das tarefas 1/7 e artifact local `docs/manual-group-live-project-v3-recipient-v1.json`. Não alterar os projetos/pins ativos.

- [ ] Preparar 12 casos sintéticos rotulados como sintéticos, incluindo destinatário terceiro, citação, autorreferência, ambiguity, chamado direto e continuidade DM: 8 de desenvolvimento no repositório e 4 reservados importados pelo papel avaliador no store privado, antes de ajuste. Não commitar holdout no checkout acessível ao proponente. Se o mesmo agente já viu todos, declarar ausência de holdout independente e não alegar validação reservada. Dados reais só em store privado após revisar acesso e completude.
- [ ] Não converter os resumos da auditoria em supostos payloads históricos. Casos históricos incompletos têm esse status e não certificam replay real.
- [ ] Criar perfis candidatos a partir das configurações existentes: Jev tipado + Gemma resumo; Gemma completo; Luna completo. Model IDs/parâmetros/preço devem ser importados da revisão escolhida e registrados, não inventados ou inferidos de nomes. Todos inicialmente disabled para chamadas live.
- [ ] Fase 1 compara perfis com prompt/contexto fixos; fase 2 compara prompt atual versus destinatário no finalista, sem mudar duas variáveis simultaneamente. Começar com 3 repetições por caso; 5 nos críticos na confirmação. Manifestos incluem custo de todas as chamadas, não só número de trials.
- [ ] Testar import/plan/capability validation com HTTP fake; preview informa contagem e custo estimado/unknown. Preços ausentes, override de instruções incompatível ou budget não autorizado bloqueiam `run`, mas não análise de manifesto.
- [ ] Rodar `dotnet test tests/Filomena.Evaluation.Tests -c NativeMemory --filter FullyQualifiedName~RecipientManifestTests`; PASS. Commit `docs(evaluation): add first recipient experiment bundle`.

### Tarefa 9: integração final, documentação e gate live separado

**Files:** criar `tests/Filomena.Evaluation.Tests/WorkbenchAcceptanceTests.cs`, atualizar `docs/evaluation-operations.md` e `docs/evaluation-cli.md` com resultados reais da implementação.

- [ ] Escrever aceitação: DB vazio → import → run fake → interromper/reabrir → comparação → backup/restore → mesmas refs/hashes/decisões, sem duplicar tentativa incerta. DB fora do ar não inicia nenhum processo de conversa nem escolhe modelo alternativo.
- [ ] Executar todas as suites novas e regressões Llm/Core relevantes, sem variáveis de opt-in live. Rodar `dotnet build Filomena.slnx -c NativeMemory`, registrar separadamente baseline alheio e regressões novas; não declarar solução verde se build global continuar quebrado.
- [ ] Executar `dotnet format src/Filomena.Evaluation/Filomena.Evaluation.csproj --verify-no-changes` e equivalente CLI/testes; `git diff --check`; revisar diff sem mudanças acidentais em App, brain, Graphiti ou outbound.
- [ ] Fazer revisão local de authority, custo desconhecido, callbacks/retries não observados, stale refs, retenção/holdout e qualquer caminho que altere config ativa. Revisão independente por outro agente só após aprovação separada.
- [ ] Registrar smoke CLI e restore com evidência. Não executar testes/modelos live sob o rótulo de smoke.
- [ ] Commit `test(evaluation): verify local workbench acceptance`; relatório consolidado, sem push. Exportar bundle candidato não ativa nada.
- [ ] Só depois solicitar/confirmar janela de benchmark real: perfis exatos, corpus permitido, orçamento monetário/calls, disponibilidade GPU e escopo de providers. Rodar comparação apenas com autorização; promoção continua fora desse fluxo.

## Checklist de cobertura do desenho

| Requisito | Tarefas |
|---|---|
| Revisões, proveniência, pinning e contratos extensíveis | 1, 2, 5 |
| PostgreSQL independente, ACL, backup/restore | 2, 3, 9 |
| Chamadas/retries, custo, crash, orçamento concorrente | 4, 5, 6 |
| Evidência não é julgamento; holdout e comparações honestas | 2, 6, 7, 8 |
| Histórico orienta próxima investigação sem busca combinatória | 6, 8 |
| Ferramentas genéricas e futura REST/MCP | 7 |
| Zero promoção automática e isolamento do runtime | 4, 5, 7, 9 |
| Exclusão/retention, exportação privada e modelos mutáveis | 1, 2, 6, 7 |

## Handoff

Plano ainda não executado. Revisão de Helena antes de código ou provisioning.
Execução recomendada: neste agente, sequencialmente, sem orquestração paralela.
Critérios determinam checkpoints; estimativa de tempo não substitui aceitação.
