# Migração da plataforma: acompanhamento de execução

## Nome da fundação: Cortex — 2026-09-30

**Atualização 2026-10-01:** plano aprovado e renomeação implementada localmente:
Core, Inference, Memory, OpenClaw, Evaluation e CLI. Filomena.App e a camada
específica Filomena.Llm permanecem. [Evidências e limites atuais](cortex-migration-status.md):
consumidores de produção compilam, mas a validação global continua com falhas
anteriores no compilador de flows e testes de frontend/settings. Sem deploy.
O parágrafo abaixo registra o estado documental anterior à implementação.

Aprovado em conversa o recorte: Cortex é a infraestrutura compartilhada;
Filomena permanece a aplicação/personagem e Damma é consumidora futura.
A [especificação de nomenclatura e compatibilidade](superpowers/specs/2026-09-30-cortex-foundation-design.md)
foi aprovada; o [plano de implementação](superpowers/plans/2026-09-30-cortex-foundation.md)
está escrito para revisão, sem implementação. Mantém o repositório/pasta atual,
dados, identidade persistida, brain e instalação ativa. Não substitui os planos
de inferência, ownership ou aceitação PostgreSQL existentes. Esta atualização
é documental; nenhum build, teste, serviço ou modelo foi executado nesta etapa.

## Escopo e autorização

### Auditoria das fatias maiores e revalidação de inferência (2026-09-28)

Reexecutados **51 testes HostInference/HostBound** em Core.Tests, configuração
NativeMemory, sem restore: todos passaram. Fixtures controladas, sem modelos ou
serviços; incluem direct route nos três estados, Boundary sintético, rechecagem
após regeneração e Shadow. Isso não é aceitação dos stores ou dos roots live.

Reconciliada a seção de comparação persistida no plano da oficina: os cinco
subpassos têm código, mas ainda não aceitação PostgreSQL. Identificada a pendência
funcional que impede considerar recovery completo: quarentena e declaração de
custo não implementam fechamento/reconciliação/retomada. O plano agora distingue
essa ausência das ferramentas já existentes e registra os casos concorrentes
necessários, sem inventar autorização para refund, replay ou orçamento novo.

Ownership de memória continua com especificação/plano em revisão e nenhuma
implementação. A escolha do primeiro consumidor permanece em aberto. Não foram
repetidas perguntas anteriores, implementadas políticas novas ou iniciadas operações.

### Verificação local: cancelamento e contabilização incerta (2026-09-28)

Fechada a verificação pendente dos dois casos em `EvaluationHttpHandlerTests`:
cancelamento anterior à admissão não cria tentativa nem chama transporte;
cancelamento depois do envio controlado registra `indeterminate/request_cancelled`,
sem uso inventado e sem retry. O journal de teste rejeita token cancelado no
Finish, comprovando que o handler usa token separado para contabilizar o efeito.

Execução atual: **630 testes estritamente offline passaram**, excluindo PostgreSQL,
LocalSocket e DatabaseLifecycleTests. Build da CLI sem restore passou com zero
warnings/errors. Hash do brain v3 preservado. Ajustada apenas formatação na
aceitação preparada; nenhum código de produto alterado neste checkpoint.

A teoria `Bound_cli_and_real_executor_complete_or_stop_controlled_pipeline_without_replay`
foi compilada e inspecionada, **não executada**. Sua variante PostgreSQL cancela
após observar a reserva durável e verifica quarentena, reserva retida, ausência de
resultado e rejeição por uma aplicação reaberta. Ela ainda exige banco descartável
até migration 009 e listener loopback autorizado; não é prova de crash real.
Nenhum banco, modelo, listener, serviço, migration ou commit foi acionado.
Aceitação transacional, ownership de memória e demais fases continuam pendentes.

### Incremento local: histórico comparável sem inflação de evidência (2026-09-28)

`issues.history` lê 1–20 relatórios explícitos no mesmo snapshot read-only, exigindo
authority de evaluator e revalidando fontes/escopo/hash pelo reader existente.
Agrupa somente plano, policy e bindings iguais; trials repetidos em snapshots não
inflam contagens/custos. Conflitos de julgamento/medição/identidade não são resolvidos
por preferência: grupo fica `conflicting_evidence`, sem métricas. Fontes indisponíveis
ficam explícitas, com resultado `partial`, não sucesso vazio.

**628 testes offline passaram**, incluindo 13 cenários observados RED/GREEN e três
regressões adicionais. Integração PostgreSQL de duplicatas/negação cross-scope/
redaction/ausência de mutação preparada e compilada, não executada. Corrigidas na
verificação a ambiguidade de overload do hash e a chamada de teste a método internal;
o teste agora usa a API pública do reader. Sem alteração de migrations/brain/operação.

É histórico de desenhos idênticos, não normalização estatística entre experimentos
incompatíveis, ranking global ou promoção automática. Custo por critério pode se
sobrepor; não equivale a total faturado. Aceitação transacional real e demais fases
da plataforma permanecem pendentes.

### Checkpoint de escopo global e próxima fronteira (2026-09-28)

Revisão atual contra o roadmap, sem executar novos testes nesta entrega documental:

| Fase | Evidência disponível | O que ainda impede conclusão |
|---|---|---|
| 1 — inferência host-bound | código e testes controlados; última validação histórica descrita em `host-bound-inference.md` | não significa roots operacionais migrados ou stores isolados |
| 2 — oficina | ferramentas/import/run/comparação/decisão/export de referências em código; último checkpoint **612 testes offline**, build/whitespace verdes | aceitação PostgreSQL/concorrência/crash/restore; bootstrap operacional host-owned; reconciliação/retomada e export/retention ainda parciais |
| 3 — ownership | proposta e agora plano local de leitura bound, com mapa de callers real | revisão do plano, implementação, inventário privado e integração backend; nenhum ownership real inferido |
| 4 — consumidor | alternativas Damma/Dota documentadas | decisão do primeiro caso útil/dados permitidos; não há produto de domínio implementado |
| 5 — jobs/budget agregado | requisitos de raiz/filhas/revogação documentados | desenho/fatia concreta e aceitação durável, sem novo scheduler especulativo |
| 6 — API pública | biblioteca/CLI host-owned não equivalem a autenticação pública | contrato externo/threat model/authority e aceitação próprios |

Plano preparado: [leitura de memória vinculada ao host](superpowers/plans/2026-09-28-host-bound-memory-read.md).
Não implementado nesta entrega. Evita novo contexto de execução, preserva parser
Graphiti e usa factory pelo batch capturado. A inspeção confirmou que participation
review **proíbe memória**; o plano não deve ampliar essa capacidade. Execução normal
e response preview compartilham `QueryMemoryAsync` e precisam do mesmo binding.

Os testes Graphiti existentes ficam em Llm.Tests (referencia App), não em um
suposto projeto Memory.Tests. O plano usa Core.Tests + referência explícita a Memory
para evitar trazer App à validação local. A proposta continua sem ativação,
migração de IDs ou serviço externo. Questões anteriores sobre banco, ownership e
primeiro consumidor não foram repetidas; este documento não presume respostas.

### Incremento local: importação de corpus pela superfície persistida (2026-09-28)

Adicionados `artifact.import` e `case.import`, usando o store existente. Payload
limitado/fechado, sem leitura de caminhos ou URLs do caller; resposta contém refs,
não conteúdo. Authority vem do host. Fechada lacuna no registro de caso: agora a
referência exige também acesso ao holdout, além de escopo/hash/disponibilidade.
Proposer não cria holdout; operator não ganha acesso ao corpus.

**612 testes estritamente offline passaram**. Onze guardas da ferramenta e um do
store observaram RED/GREEN. Aceitação PostgreSQL de roundtrip/isolamento/redaction
preparada e compilada, não executada. O teste E2E preparado agora importa também
o corpus pela CLI. Corrigido setup histórico de teste holdout que nomeava o acesso
como proposer, mas usava o role default evaluator. Isso não é integração aprovada.

Artifact e caso são operações separadas; falha pode deixar artifact sem caso,
sem fingir importação concluída. Case key é única e não há retry automático nem
overwrite. Sem migration nova/aplicada, serviços, modelos ou alteração do brain.

### Incremento local: comando de execução com binding do host (2026-09-28)

`EvaluationApplication` conecta `experiment.run` ao `CompletionTrialExecutor`
existente, uma tentativa de trial por comando. A composição recebe datasource e
endpoints do host e exige `Evaluate` por invocação; JSON aceita somente a identidade
do experimento. CLI standalone continua sem binding. Sem nova fila, retry automático,
ativação de produção ou bootstrap de credenciais. Falha após invocação preserva
incerteza; nenhum pending trial não significa experimento concluído.

Três guardas novos observados RED/GREEN, mais dez regressões de entrada/authority/
cancelamento: **600 testes estritamente offline passaram**. Aceitação PostgreSQL
agora chama também `experiment.run` pela CLI real; compilada, não executada. Não
comprova operação, integração transacional ou modelo. Aceitação com banco e demais
fatias da plataforma continuam pendentes.

### Incremento local: aceitação composta preparada (2026-09-28)

Teste `Bound_cli_and_real_executor_complete_controlled_workbench_pipeline_once`
agora atravessa import/plan/aprovação, executor+adapter+journal reais com resposta
HTTP controlada, avaliação, comparação, decisão e export. Encontrado e corrigido
setup de integração anterior sem a nova aprovação/moeda. Teste compilado, **não
executado**: depende de PostgreSQL descartável migrado até 009. Não comprova ainda
o fluxo operacional, qualidade de modelo ou fechamento financeiro.

Separado `Category=LocalSocket` para o smoke de adapter que abre listener efêmero.
A seleção estritamente sem banco/Docker/listener passou **587 testes**; o checkpoint
de 588 incluía esse smoke local sem modelo. Build CLI e whitespace verdes; hash
brain v3 inalterado. Sem alterações operacionais, migrations aplicadas ou commits.

### Incremento local: fronteira CLI testável (2026-09-28)

Extraído `CommandDispatcher` usado pelo entrypoint real. Modo offline preservado;
modo persistido recebe tools/access somente da composição host-owned e o binário
standalone continua sem esse binding. Argumentos/stdin não concedem role, scope,
endpoint ou credencial. `host_binding_required` ocorre antes de conexão/input.
Entrada limitada, saída única JSON e status conservador após invocação incerta.

**588 testes offline passaram**; build CLI/whitespace scoped verdes. Seis casos
comportamentais RED/GREEN, smoke do binário real offline e rejeição do persisted
sem binding verificados. Banco/serviços/modelos não iniciados. Composição host,
aceitação PostgreSQL e fluxo persistido ponta a ponta ainda pendentes. A escolha
de priorizar essa fronteira evita expandir billing comercial fora da primeira
migração; declarações financeiras permanecem conservadoras, sem refund automático.

### Incremento local: readback contábil de declarações (2026-09-28)

`experiment.accounting` agora compõe aprovação e declarações na mesma transação
read-only repeatable-read. Verifica vínculos de tentativa/owner/generation/moeda/
evidência e oculta valores cuja evidência foi redigida/está indisponível. Se faltar
qualquer declaração, o total não é publicado como completo; subtotal conhecido,
ausências e evidências indisponíveis aparecem separados. Exposição retida inclui
overrun declarado, sem liberar reservas ou reativar execução.

**582 testes offline passaram**, CLI build sem warnings/errors, whitespace scoped
verde. Oito casos de projeção observados RED/GREEN e duas regressões de overflow/
withholding. Assertions PostgreSQL de readback/overrun/redaction compiladas e não
executadas. Ainda é atestação de operador, não invoice verificada; correção,
fechamento/retomada e aceitação real continuam pendentes. Nenhum efeito externo.

### Incremento local: declaração de cobrança com evidência (2026-09-28)

`attempt.charge.declare` acrescenta declaração do operador vinculada à tentativa
física, owner/generation originais, approval/moeda e artifact scoped de cobrança.
Não é invoice verificada automaticamente. Store preserva raw outcome/reservas e
usa locks parent/trial/attempt; excesso declarado cancela novas admissões. Custo
menor/zero não libera saldo, não resolve execução incerta nem autoriza retry.
Migration 009 preparada, não aplicada. Falhas/ausência de proveniência bloqueiam.

**572 testes offline passaram**, CLI build e whitespace scoped verdes. Política
e guardas tiveram RED/GREEN; aceitação PostgreSQL de evidência/idempotência/
overrun/history compilada e não executada. Readback/projeção de declarações,
correção/supersessão, settlement final e retomada autorizada ainda faltam; o reader
atual de reservas não deve ser apresentado como fechamento financeiro. Não houve
serviço, banco, modelo, migration aplicada ou commit; objetivo global incompleto.

### Incremento local: revogação por cancelamento (2026-09-28)

Reutilizado `experiment.cancel` para bloquear novas admissões. Capability estreita
`CancelExperiment` concedida a evaluator/operator, não proposer; operator não
ganha `Evaluate`. Cancelamento scoped adquire o lock do experimento e registra
mudança + audit na mesma transação; repetição não renova authority nem apaga provas.
Reservas e aprovação histórica são preservadas; observações tardias continuam
aceitas. Nenhuma promessa de interromper requisições já admitidas/submetidas.

**549 testes offline passaram** e CLI build sem warnings/errors. Teste de authority
do operator observado RED/GREEN. Aceitação PostgreSQL de isolamento, repetição
concorrente, auditoria, rejeição de novo attempt e Finish tardio foi ampliada e
compilada, não executada. Sem nova migration neste incremento. Reconciliação de
consumo e aceitação real continuam pendentes; brain e operação não foram tocados.

### Incremento local: aprovação conectada à admissão (2026-09-28)

`AttemptLedger.BeginAsync` agora exige a declaração monetária persistida do plano
sob seus locks existentes; verifica escopo/experimento/hash/teto/moeda antes da
reserva e preserva cancellation/fencing/unknown-attempt. Settings do endpoint
host-owned exigem moeda explícita. Nova tentativa registra approval ID e moeda;
migration 008 preparada, não aplicada. Histórico não é reinterpretado, e Finish
continua aceitando contabilização tardia sem exigir uma nova aprovação.

Sete testes de binding e três de configuração foram vistos falhando e passaram
após implementação. **546 testes offline passaram**; CLI build sem warnings ou
errors, whitespace scoped válido. Fixtures PostgreSQL ajustadas para aprovação
explícita; novos casos de rejeição sem reserva e proveniência compilados, não
executados. Ainda faltam revogação/reconciliação, aceitação transacional real e
as demais fases da plataforma. Nenhum serviço/banco/modelo foi iniciado.

### Incremento local: contrato monetário explícito (2026-09-28)

Atualização posterior: `MonetaryBudgetStore` e ferramentas
`experiment.budget.approve/show` implementadas em fonte, com migration 007
preparada e **não aplicada**. O store resolve o plano scoped sob o mesmo lock
inicial usado por claim/AttemptLedger, valida estado/attempts/reservas e grava
declaração imutável. Recibo idempotente é histórico, não renovação de autoridade.
Os campos JSON são obrigatórios; omissão do teto não vira zero silenciosamente.
**536 testes offline passaram**, CLI build sem warnings/errors. Testes de banco
para escopo, idempotência concorrente, imutabilidade e corrida com claim foram
compilados, não executados. A persistência ainda não está aceita operacionalmente.
Admissão currency-aware, revogação e settlement continuam pendentes; nenhum
gasto ou modelo foi habilitado. Os parágrafos abaixo preservam o checkpoint puro
que precedeu este store.

`MonetaryBudgetContract.Approve` valida uma declaração do operador para o plano
exato, com moeda explícita e teto inalterado. Rejeita outro escopo, revisão ou
experimento, execução iniciada e qualquer reserva não zero ou desconhecida.
Não altera manifests históricos nem presume USD. O código de moeda valida apenas
o formato de três letras maiúsculas, não disponibilidade de cobrança nessa moeda.

Este helper é validação pura, não uma aprovação durável nem uma capability de
execução. `UnstartedMonetaryPlan` deve futuramente ser resolvido pelo store sob
lock, com a gravação da aprovação na mesma transação; aceitar esse DTO do cliente
como prova seria inseguro. Os records retornados não autorizam chamadas ou gastos.
Persistência, reserva concorrente, reconciliação e retomada continuam pendentes.

Evidência desta execução: 14 testes focados falharam contra o stub e passaram após
a implementação; dez casos adicionais cobrem contrato inválido e todas as
dimensões de reserva. **527 testes offline passaram**, excluindo PostgreSQL e
DatabaseLifecycleTests. Build da CLI: zero warnings/errors; whitespace dos dois
arquivos verificado. Nenhum banco, serviço, modelo, migration ou commit executado.
Hash do brain v3 preservado: `47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`.

Em 2026-09-27 Helena autorizou seguir com todas as fatias. A especificação canônica
continua em `superpowers/specs/2026-09-27-intelligence-platform-evolution-design.md`.
Isto não redefine os horizontes adiados como implementação imediata, nem autoriza
serviços, bancos reais, inferência paga, ativação, commits ou revisão independente.
Execução sequencial no checkout atual; alterações anteriores são preservadas.

## Fases e evidência necessária

| Fase | Estado atual | Próxima evidência necessária |
|---|---|---|
| 1. Inferência host-bound | Implementação local, 74 testes focados passaram na entrega anterior | Preservar regressões; não confundir com wiring operacional |
| 2. Oficina | Implementação parcial; aceitação PostgreSQL não executada | Completar ferramentas/reconciliação; depois integração, concorrência e restore autorizados |
| 3. Ownership dos stores/memória | Auditoria e estratégia existentes, implementação pendente | Manifesto de ownership e bindings, negações por ID/relação/cache/job/export, integração autorizada |
| 4. Consumidor não conversacional | Não implementado | Selecionar tarefa Damma ou Dota e dados permitidos antes do código de domínio |
| 5. Jobs/orçamento agregado | Não implementado | Contratos específicos, reserva concorrente, crash/revogação e aceitação durável |
| 6. API/extensões | Não implementado | Autenticação e contratos aprovados; streaming seguro se necessário; SDK depois |

O objetivo permanece todas as fatias da migração, não apenas a próxima ferramenta.
Motor de vida completo, atuação Dota V4, promoção automática e billing continuam
explicitamente adiados pela especificação. Decisões de produto ainda abertas não
serão substituídas por fixtures apresentadas como produtos completos.

## Checkpoint histórico: início da tarefa 7 do plano da oficina

Plano: `superpowers/plans/2026-09-26-evaluation-workbench.md`.
Primeiro incremento: `experiment.status` read-only no serviço de ferramentas
existente. Deve mostrar estado armazenado, contagens de trials/tentativas,
reservas retidas e pendências sem ler prompts/outputs ou inventar custo observado.
Consulta scoped em um único statement para snapshot consistente, sem reclamar
trabalho, recuperar leases, liberar reservas ou acionar providers.

Testes offline: autorização antes de conexão, request estrito, contagens e
classificação conservadora. Testes PostgreSQL opt-in: isolamento por ID,
planejamento/claim/tentativa/cancelamento e ausência de mutação na consulta.
Esses testes de banco serão compilados, não executados nesta janela.

Ruling: continuar a implementação offline da oficina enquanto o provisioning está
suspenso não equivale a aceitar sua durabilidade. Não extrair garantias de budget
para o runtime nem ativar execução paga antes da aceitação real.

## Checkpoint 2026-09-28

- `experiment.status` implementado na biblioteca: snapshot SQL único, authority
  host-owned, sem payloads privados, reservas não confundidas com custo observado.
- Correção de completude: muitas tentativas em um trial não podem ocultar outro
  trial concluído sem evidência. Teste comportamental falhou e depois passou.
- `assessment.add` conectado ao store existente, sem novo banco/schema. Matriz de
  authority/validação observada com 14 falhas antes do fix e verde depois; mais
  seis regressões de null completam a matriz.
- 293 testes offline passaram; build CLI sem warnings/errors. Testes de banco
  foram adicionados/estendidos e compilados, não executados.
- Próximos itens da oficina: reconciliação explícita/fencing, histórico de decisões,
  comparação persistida/export e CLI host-owned, depois aceitação real de banco.
- Fases 3–6 continuam pendentes; este checkpoint não encerra o objetivo global.

Rulings: sem commits e sem nova worktree conforme limites vigentes; sem revisão
independente não autorizada. Testes de banco opt-in não são substituídos por mocks.
Os hashes de migrations existentes não foram alterados. `assessment.add` é append
sem idempotency key: resultado incerto continua exigindo reconciliação, não retry.

### Comparação pareada

Tarefa 6 avançou: bootstrap pareado por caso, policy versionada/seed explícita,
gate de amostra insuficiente, evidência inelegível e limite de trabalho. Preview
existente integrado sem nova ferramenta/serviço. 15 novos testes; suíte offline
308/308, CLI build e whitespace verdes. Não houve benchmark/modelos nem ranking real.
O resultado é macro-average por caso e não muda a média por tentativa existente.
Runtime .NET 10 faz parte da identidade de reprodução; guardar resultados além da seed.
Reconciliação, persistência de comparação/decisões, export, CLI operacional e
aceitação PostgreSQL continuam pendentes. A migração completa ainda não foi concluída.

### Consulta do corpus

`case.list` implementado sobre store/ferramentas existentes, com paginação UUID,
scope/holdout resolvidos pelo host e exclusão de artifacts redigidos antes do limite.
Nove testes offline novos; 317/317 no conjunto offline, build CLI e whitespace verdes.
Aceitação PostgreSQL separada contempla wrong project, wrong workspace e paginação,
mas não executada. Não há claim de snapshot entre páginas nem export completo.
Todas as demais pendências e fases permanecem no acompanhamento acima.

### Preparação da fase 3

Inspeção de fonte identificou namespace derivado da conversa sem conta/projeto,
AgentUuid persistido com `agent:filomena`, múltiplas chamadas físicas no hybrid e
composition roots que recebem reader bruto. Proposta específica em
`superpowers/specs/2026-09-28-memory-ownership-boundary-design.md`.
Recomenda binding legado explícito, autoridade revalidada por chamada e antes da
devolução, preservando IDs/semântica atuais. Não foi implementada/ativada; não foi
inventariado dado privado nem provado isolamento SpiceDB. A escolha do primeiro
consumidor não conversacional (Damma/Dota) foi perguntada sem bloquear trabalho local.

### Reconciliação: estágio de quarentena

Implementado `experiment.quarantine` operator-only, com binding ao owner/generation,
evidência scoped existente e decisão idempotente. Transação conserva tentativas,
outcomes, reservas e pending work; invalida o owner e não reinicia execução.
334 testes offline passaram; build CLI sem warnings/errors. Teste PostgreSQL
concorrente/reabertura compilado, não executado. Não é settlement nem restituição:
reconciliação continua requerida, custo desconhecido não vira zero.
Não declarar fase 2 pronta a partir deste incremento.

### Preparação da aceitação de restart/restore

`DatabaseLifecycleTests` ampliado para usar `MigrationRunner` e
`PostgresEvaluationStore` reais: cria revisão, artifact e case vinculados; verifica
hashes, conteúdo, vínculos e negação de leitura em outro projeto antes/depois de
restart e restore. Reaplica o runner para verificar a cadeia de checksums.
As credenciais são lidas apenas dentro do teste opt-in e não são impressas.

Verificação local em 2026-09-28: 334/334 testes offline passaram; whitespace do
arquivo alterado passou. O teste de lifecycle compilou, mas foi explicitamente
excluído da execução, assim como os demais testes PostgreSQL. Nenhum Docker,
banco, modelo ou serviço conversacional foi iniciado. Aceitação transacional,
backup/restore e isolamento com banco real continuam pendentes de janela
autorizada; compilação não constitui essa evidência.

### Registro de decisões investigativas

`decision.add` implementado na biblioteca host-owned existente, evaluator-only,
sem ativação ou execução. Reutiliza `evaluation.decisions`/audit sem modificar
migrations. Preserva motivo, condição de reabertura e refs de evidência scoped;
inclui `no_change`, idempotência por ID/autor/conteúdo e conflitos explícitos.
Evidência redigida não autoriza nova decisão; receipt de decisão anterior apenas
reconhece o registro histórico. Não altera experimentos/reservas/activation records.

14 testes de ferramenta observaram RED→GREEN; seis testes adicionais de contrato
e acesso direto passaram. Suíte offline: 354/354. Build CLI: zero warnings/errors.
Aceitação PostgreSQL de concorrência, isolamento, redaction e ausência de ativação
compilada, não executada. Consulta de histórico, comparação persistida, settlement,
CLI operacional e aceitação real continuam pendentes; nenhuma fase global encerrada.

### Leitura do histórico de decisões

`decision.list` adicionada à biblioteca, restrita ao evaluator do host. Consulta
SQL única por página, filtro opcional de experimento e cursor UUID limitado ao
escopo corrente. Registros cuja evidência não está disponível conservam identidade
histórica, mas não expõem motivo, condição de reabertura ou refs. Não é remoção de
todas as cópias, snapshot entre páginas nem autorização de ativação.

Oito testes da ferramenta observaram RED→GREEN e três verificam acesso direto.
365/365 testes offline passaram; testes PostgreSQL de escopo, paginação e redação
foram ampliados/compilados, não executados. Comparação persistida, priorização,
settlement e aceitação durável continuam necessários à oficina completa.

### Auditoria do próximo vínculo de comparação

O próximo passo não é salvar diretamente `comparison.preview`: trials pinam input
revisions, assessments apontam para corpus cases, e a associação entre ambos ainda
não é formal. Além disso, há múltiplos attempts em receitas reais, enquanto o
preview usa uma observação agregada por célula. Subpassos/negações registrados no
plano existente, tarefa 7; nenhuma comparação persistida foi declarada implementada.

Auto-revisão também removeu `FOR SHARE` desnecessário de `decision.add`: esse lock
exigiria UPDATE em artifacts, não concedido pelas migrations da aplicação. Não
foram ampliadas permissões. Aceitação preparada exige `evaluation_app` sem UPDATE;
não executada por ausência da janela de banco. Evidência é observada na validação,
não reservada contra redação futura. 365 testes offline continuam passando.

### Associação explícita corpus/input

`case.bind_input` implementado na biblioteca para evaluator host-owned. Nova
migration 005 somente preparada, sem aplicação: vínculos imutáveis/scoped entre
case, artifact exato, input revision e provenance revision. Hash/kind/inputSha256
são verificados; origem fixa `evaluator_declaration`, nunca prova inventada de
execução do preparador. IDs legados não são preenchidos automaticamente.

Sete testes de ferramenta RED→GREEN; catálogo novo observado com duas falhas antes
da adição. Sete testes de validação documental adicionais. 379/379 testes offline
passaram. Aceitação PostgreSQL de concorrência, isolamento e imutabilidade
compilada, não executada. Comparador persistido ainda não consome esse vínculo;
resolver evidências/attempts/policy e persistir o relatório continuam pendentes.

### Medição de trials com múltiplas chamadas

`TrialMeasurement.Aggregate` separa identidade do trial, IDs de attempts, duração
do pipeline, totais de tokens e estimativas com refs de preço. Conta chamadas com
erro HTTP; não soma tempos de chamadas como latência. Campo ausente/overflow não
vira zero; moedas diferentes ou preço ausente não ganham conversão implícita.
Tentativas de outro trial, duplicatas e estados incoerentes são rejeitados.

14 testes novos; sete cenários RED→GREEN, mais uma regressão de moeda parcial
observada falhando antes da correção. Esse componente é aritmética sobre evidência
resolvida, não prova de completude da query, preço aplicável ao modelo ou cobrança
confirmada. Leitor scoped e comparador persistido ainda precisam consumi-lo.

### Leitor de medições persistidas

`trial.measurement` conectado à biblioteca host-owned: leitura repeatable-read e
read-only do trial/resultado/todas as tentativas, scope e experiment ID obrigatórios.
Não retorna completion output. Valida hash/revisões antes de extrair elapsed;
artifact redigido deixa elapsed desconhecido, e elapsed ausente é rejeitado.
Não converte reserva em preço: custo monetário ainda desconhecido nesse leitor.

403/403 testes offline passaram; quatro testes da ferramenta RED→GREEN e regressão
de elapsed ausente reproduzida/corrigida. Aceitação PostgreSQL preparada, não
executada. Comparação por critério/policy, projeção pinada de preços, relatório
persistido e aceitação real continuam necessários; não declarar oficina concluída.

### Seleção de avaliação ligada ao trial

`trial.assessment` implementado com binding explícito de caso/input, revisão de
avaliador obtida do experimento, e evidência obrigatória do resultado do trial.
Histórico completo por critério entra na regra versionada de terminal único:
branches, ciclos, predecessores ausentes e revisão incompatível não aprovam nada.
Avaliação redigida não revive uma aprovação antiga. Outcome/severity preservados;
nenhum critério crítico ou rubric completo é inventado.

13 testes do seletor e seis da ferramenta observaram RED→GREEN. 422/422 offline
passaram. Aceitação PostgreSQL foi preparada/compilada, não executada. Ainda falta
compor todos os critérios/policy com medições num relatório persistido; essa
consulta não equivale a concluir a comparação ou as fases restantes da plataforma.

### Composição de comparação com policy pinada

`TrialComparisonAssembler` combina medições e critérios resolvidos com agenda
esperada e policy versionada/hash verificado. Critérios ausentes, ambíguos ou com
binding incompatível não aprovam o trial. Falha crítica é definida pela policy,
não inventada a partir da severidade. Detalhes por critério e consumo permanecem
no relatório mesmo quando o trial é excluído das estatísticas por incompletude.

Deduplicação abrange trial lógico, todas as chamadas físicas e avaliações
selecionadas. Uma receita com duas chamadas não vira duas observações; custo ou
moeda ausente não vira zero/USD. Mantida compatibilidade do preview anterior.

Verificação atual: **437/437 testes offline**, build da CLI com zero warnings/errors,
formatação dos arquivos desta fatia verificada. Regressões de avaliação duplicada,
hash inválido e ausência de breakdown observaram falha antes da correção.
Hash do brain v3 permaneceu igual ao checkpoint. Revisão feita pelo autor,
não por sessão independente. Nenhum serviço, banco ou modelo executado.

Limite: composição pura de inputs resolvidos, ainda não comparação persistida.
Faltam leitura consistente de toda a evidência no mesmo snapshot do banco,
projeção pinada de preços, persistência e revalidação de acesso/redaction do
relatório. Aceitação PostgreSQL continua não executada; migrations não aplicadas.
Não declarar conclusão do workbench nem da migração da plataforma.

### Leitura composta do experimento

`experiment.compare` adicionado à biblioteca host-owned, somente para evaluator.
Agenda, policy, casos/inputs, medições e avaliações usam uma única conexão e
transação read-only/repeatable-read. Hash do plano e cada linha da agenda são
verificados; não há reconstrução heurística da ordem original dos inputs.
Caller fornece explicitamente a lista ordenada e os bindings, mas isso não
substitui autorização nem a verificação contra o plano persistido.

Seis testes de ferramenta e três de plano passaram após falhas observadas.
**446/446 offline passaram**, CLI compilou sem warnings/errors e formatação scoped
passou. Teste PostgreSQL preparado/compilado, NÃO executado: isolamento por projeto
e workspace, resultado real, redaction, binding, avaliador pinado e ausência de
mutações. Uma seleção intermediária incluiu um teste PostgreSQL que foi skipped
por falta de opt-in; o comando final excluiu explicitamente toda essa categoria.
Nenhum banco/serviço/modelo foi acessado ou iniciado.

Ainda faltam projeção pinada de preços e persistência/releitura autorizada do
relatório, além da aceitação transacional real. A comparação read-only não é
conclusão de toda a tarefa 6/7 ou da plataforma. Sem migrations novas/aplicadas.

### Projeção pinada de preços

Comparação aceita vínculos explícitos attempt→revisão de preço, resolvidos no
mesmo snapshot scoped. Termos têm modelo observado, adapter, request hash,
período, intervalo de input tokens, tier quando aplicável, moeda, fonte e taxas.
São declarações do evaluator, não descoberta automática de tarifas nem cobrança
confirmada. URI de fonte não é acessada. Tier ausente só é admitido com declaração
explícita de taxas independentes do tier; não se inventa um tier observado.

Reprecificar não altera tentativas/reservas anteriores. Chamadas sem preço,
incertas/com erro HTTP, incompatibilidades e componentes incompletos mantêm custo
desconhecido, com motivo. Relatório preserva refs e origem da projeção. Memória,
brain, providers e operação permanecem intocados nesta fatia.

**464/464 offline**, CLI build sem warnings/errors e formatação scoped verde.
Treze cenários de projeção observaram RED→GREEN; mais regressão de tier, validação
de bindings e testes de ferramenta. Corrigida serialização da fixture de ferramenta
para camelCase, evitando rejeição por casing mascarar o check específico.
Aceitação PostgreSQL expandida e compilada, NÃO executada. Nenhuma migration
nova/aplicada, serviço iniciado ou inferência executada. Persistência e releitura
autorizada de relatórios continuam como próximo incremento, não concluídas.

### Relatórios persistidos e dependências de evidência

Preparadas operações `comparison.save/show` na biblioteca. Save gera o relatório
pelo host dentro da transação; não recebe conteúdo arbitrário do caller. Registra
hash, versão/método, experimento/policy, autor e links scoped às fontes consultadas.
Show revalida fontes, hash e bindings, ocultando conteúdo quando fontes foram
redigidas. Relatório não fica acessível pela API genérica de artifacts.

Migration 006 aditiva preparada/embutida, **não aplicada**. Idempotência sequencial
definida por ID/autor/request; recibo histórico não renova acesso. Inserção concorrente
ou commit incerto não dispara retry automático. Retenção física/purge de bytes
derivados ainda não foi implementada; withholding não significa exclusão.

**475/475 offline**, CLI build e formatação scoped verdes. Seis guardas de ferramenta,
dois casos de catálogo e três rejeições de estrutura incompleta observaram falha
antes da implementação/correção. Testes de integridade, cópia de inputs e aceitação
PostgreSQL foram acrescentados; os PostgreSQL somente compilados, NÃO executados.
Revisão do autor, sem revisão independente. Brain inalterado, nenhum banco,
serviço, modelo, migration aplicada ou commit nesta fatia.

Próximos itens ainda necessários: priorização/consulta de issues, export seguro,
reconciliação/settlement, ligação operacional da CLI e aceitação real PostgreSQL;
as fases seguintes da plataforma também não estão concluídas.

### Consulta de recorrência por relatório

`issues.list` implementado sobre um relatório explicitamente escolhido e releitura
autorizada de suas fontes. Separa candidatos/critério, repetições/casos distintos,
falhas confirmadas/unscored/evidência não resolvida. Referências permitem voltar
às avaliações responsáveis; histórico indisponível não vira análise vazia válida.

Ordem versionada e explicável: falhas críticas, casos afetados, quantidade de
falhas, severidade, identidade. Nenhuma promoção ou execução automática. Custo
exposto é custo dos trials com falha, não economia prevista; linhas de critérios
podem se sobrepor e não devem ser somadas como total de gasto.

**485/485 offline**, CLI build e formatação scoped verdes. Cinco cenários puros e
três guardas de ferramenta observaram RED→GREEN, mais duas regressões de custo e
integridade. Aceitação PostgreSQL de issues/redaction preparada/compilada, não
executada. Sem serviços, modelos, migrations aplicadas ou commits.

Ainda falta agregação histórica entre relatórios com compatibilidade/deduplicação
explícitas, além de export, reconciliação e aceitação real; consulta de um relatório
não conclui essas capacidades nem o objetivo completo da plataforma.

### Exportação resumida privada

`bundle.export` devolve documento versionado/hash, estatísticas e referências de
um relatório após revalidar acesso às fontes. Não inclui bytes dos artifacts,
prompts, respostas ou credenciais. Marcado como evidência privada, não pública ou
anonimizada. Não escreve arquivos nem ativa configuração; hash não concede authority.

**493/493 offline**, CLI build sem warnings/errors e formatação scoped verde.
Cinco testes do empacotador e três guardas de ferramenta observaram RED→GREEN.
Aceitação PostgreSQL estendida para export/redaction, somente compilada. Nenhum
serviço, modelo, banco ou migration executado; brain inalterado.

Limite explícito: summary/references, não exportação de payload privado completo
nem bundle instalável de perfil candidato. Cópias exportadas não são revogadas por
redaction posterior. Retenção/export completo e integração operacional seguem
pendentes, assim como reconciliação, aceitação real e fases restantes da plataforma.

### Reconciliação contábil read-only

`experiment.accounting` implementado para operator/evaluator: compara totais do
pai com reservas de todas as tentativas no mesmo snapshot; mostra tokens observados,
uso desconhecido, tentativas incertas, discrepâncias e overruns. Retém reservas
maiores sem reparação automática, refund ou autorização para reenviar.

**503/503 offline**, CLI build e formatação scoped verdes. Sete cenários puros e
três guardas de ferramenta RED→GREEN, incluindo reserva maior inexplicada no pai.
Aceitação PostgreSQL preparada/compilada, não executada. Nenhum banco ou serviço
iniciado, nenhum modelo ou migration executado, brain intacto.

Gap concreto para settlement: teto/reservas atuais não possuem moeda; projeções
de preços não equivalem a cobrança confirmada. A leitura expõe isso explicitamente.
Antes de refund, é necessário contrato versionado para moeda/evidência e testes
transacionais. Manifests antigos não receberão moeda default nem hash reescrito.
Reconciliar usage de forma diagnóstica não conclui recuperação/settlement/retomada.
