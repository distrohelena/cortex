# Native Admission Journal V1 — implementação local

Status: implementação experimental em checkout isolado; **NOT READY para operação live**.
Nenhum listener, Graphiti live, modelo, outbound, Gateway ativo ou configuração pessoal foi acionado/modificado.

## Localização e autoridade

Upstream: OpenClaw `v2026.8.1`, commit `ea806575e6450e4d1efdfc72c19f04be982a1b9b`,
checkout isolado `../openclaw/native-observation-v1` sob a raiz dos worktrees.
Esta mudança NÃO está instalada no Gateway.

O ponto modificado é a transação de `createChannelIngressQueue().enqueue`, em
`src/channels/message/ingress-queue.ts`. O caminho nativo permanece:

```text
WhatsApp message-delivery.handleMessagesUpsert
  → createWhatsAppIngressMonitor / durableInboundMonitor.admit
  → ChannelIngressMonitor.admitOnce [0, 100, 300 ms]
  → queue.enqueue
      BEGIN IMMEDIATE
        INSERT channel_ingress_events (pending)
        INSERT native_observation_records
        UPDATE native_observation_epochs.tail
      COMMIT
  → processamento legado
  → complete / tombstone do payload da fila
```

A observação não usa `message_received`. O journal não substitui a fila existente.
Approval reactions que forem consumidas ANTES da admissão nativa estão fora deste
stream: ele representa **admissões duráveis**, não todos os pacotes do socket.

## Opt-in e atomicidade

`createNativeObservationHost` começa desarmado. Somente a chamada explícita do host
`armExperimentalCapture()` arma aquele processo e a rota exata (database/queue/lane).
Não existe flag de project JSON, prompt ou graph que arme isso. O SDK do plugin recebe
somente consulta do opt-in e tipos de metadata; não recebe a função de armar.
Um epoch persistido não rearma um processo novo.

Desarmado: a admissão não exige self identity nem grava observações.
Armado: falta de identidade, falha de INSERT ou falha de COMMIT reverte a transação
inteira. Nada fica parcialmente `pending` ou observado. Os retries existentes são
preservados; não há worker adicional. Depois de três falhas, o erro segue o caminho
nativo explícito de descarte, sem ObservationRecord órfão e sem fingir um gap de commit.

O diagnostic `observation_admission_persistence_failed` inclui attempt ref aleatória,
categoria, retry count, timestamp e health `degraded`, sem payload, JID ou SQL error
text potencialmente sensível. A saúde só é recuperada depois de um commit nativo bem-sucedido.

## Metadata e record

| Informação | Origem concreta | Tratamento |
| --- | --- | --- |
| native ID | `WAMessage.key.id` | Preservado; não derivado de texto/tempo |
| sessão | `WAMessage.key.remoteJid`, account da queue | Lane exata; ref opaca no journal |
| ator DM | peer JID nativo ou self autenticado para `fromMe` | Ref opaca; sem display-name binding |
| `fromMe` | `WAMessage.key.fromMe` | Boolean obrigatório para captura; ausência não vira `false` inventado |
| self PN/LID | `socketSession.self`, produzido por `readWebSelfIdentityForDecision` | Copiado sincronamente antes de `admit`; sem lookup posterior |
| timestamp | `WAMessage.messageTimestamp` | Timestamp nativo em ms quando disponível; ausência permanece ausência |
| payload | Payload serializado da admissão nativa | Retido independentemente do tombstone legado |

`captureWhatsAppObservationIdentity` retira somente o sufixo nativo de dispositivo
do JID. PN e LID autenticados viram aliases do mesmo self. A identidade canônica é
ancorada no PN autenticado; sem PN suficiente, captura falha fechada. Não há resolução
heurística de um peer LID para PN. Phase 0 é DM; esse helper não habilita grupos.

Record exposto pelo leitor confiável:

```text
sequence, nativeMessageId, sessionRef, actorRef, fromMe,
selfIdentityRef, occurredAt?, payload, journalEpoch
```

Os aliases nativos ficam no payload privado da admissão; não são prompt inputs.
O adapter futuro deve projetar conteúdo para o runtime, não entregar o envelope nativo
inteiro à LLM. Logs normais não recebem snapshots/payloads.

## Epoch, snapshot, replay e retenção

Contrato `inbound-observation:v1`. Métodos locais do host: `openObservation()`,
`pollObservation(cursor)`, `describe()`. Sem chamada de agent ou transport.

Cada arm cria epoch novo com start boundary durável. Sequence contígua por rota/epoch,
em ordem de commit da admissão, NÃO em ordem de timestamp do provider. IDs nativos
são únicos por scope, inclusive entre epochs. Replay do mesmo native ID não cria
outro record lógico. Se a fila já tiver apagado seu dedupe tombstone, a unicidade
da observação ainda impede uma segunda admissão experimental desse ID.

Snapshot e tail saem de uma única consulta SQLite com o mesmo read snapshot.
`poll(after C)` retorna somente sequences maiores que C. O snapshot inicial serve
apenas como contexto; nunca é admitido como execution. Não misturamos uma leitura
separada de transcript com o tail. Histórico anterior ao epoch NÃO é migrado nem
rotulado como replayable. Isso pode deixar o contexto inicial curto/vazio.

Retenção de homologação: janela de sete dias e máximo de 5.000 records por epoch.
Capacidade/tempo esgotado bloqueiam novas admissões experimentais, sem evict silencioso.
`purgeExpiredPayloads()` é manutenção explícita do host, não efeito de Open/Poll.
Ela remove o payload apenas de epochs expirados; preserva IDs/metadata para dedupe.
Não há archive infinito de conteúdo nem worker de purge/retry. A metadata de dedupe
continua acumulando e requer política futura se isso sair da homologação.

Cursor contém versão, epoch, session ref e sequence. Versão desconhecida, sessão errada,
sequence impossível, row ausente, contagem/tail incoerentes ou digest divergente falham
fechado. Expiração retorna `cursor_expired`; erro de leitura retorna `unavailable`,
nunca memória/snapshot vazio bem-sucedido. Digest detecta corrupção acidental, não é
PKI nem defesa contra um atacante com escrita arbitrária no SQLite.

Ao abrir explicitamente outro epoch, o anterior fica fechado. Poll de cursor antigo
continua permitindo replay dentro da retenção, mas retorna `epochClosed=true`:
isso não é uma captura ativa nem autoriza retomar uma Shadow Run anterior.

## Dispatch Inspection

`extensions/whatsapp/src/inbound/dispatch-inspection.ts` usa somente
`resolveMergedWhatsAppAccountConfig`: não chama access-control, pairing, hooks,
resolvers de aprovação, modelos ou carregamento de configuração.
O coordinator expõe `inspectDispatch(nativeSession)` sem executar seus handlers.

`active` significa que há caminho de processamento legado alcançável para a DM;
não significa que toda fala necessariamente invocará um modelo. `inactive` exige
exclusão demonstrada. Qualquer ambiguidade resulta em `unknown`.

Caminhos considerados:

1. Approval reactions em `handleMessagesUpsert`, antes da admissão/access-control.
   `approval-auth.ts` deriva seus approvers de `account.allowFrom`; lista vazia
   impede resolução de approval nessa rota.
2. `senderGateForDirect` / `checkInboundAccessControl`: `dmPolicy=disabled` bloqueia
   antes da entrega da mensagem normalizada.
3. Question reactions depois da normalização/acesso.
4. Hooks `message_received` no processamento aceito.
5. Fast abort/approve em `dispatch-from-config.prepare-operation.ts`.
6. Plugin-owned conversation bindings e seus handlers, antes do `before_dispatch`.
7. `before_dispatch`, plugins e caminho de agent posterior. `transportOnly` não
   exclui os caminhos anteriores e não é prova de isolamento.

Prova estática estreita implementada: `dmPolicy=disabled` E nenhum approval approver
derivado do `allowFrom` efetivo → `inactive`. DM disabled com approvers → `unknown`.
Entrada explicitamente permitida → `active`. Pairing store, access groups, alias
não resolvido ou config ausente não são “testados” por probes: retornam `unknown`.

**Blocker restante:** quando o coordinator usa `loadConfig`, a referência de config
original não prova o estado efetivo atual. A inspeção NÃO invoca esse callback como
probe e retorna `dynamic_effective_config_snapshot_required`. Falta um accessor
somente leitura, pertencente ao owner nativo, para o snapshot efetivo/revisão exata.
Não foi fabricado um `inactive` nem alterado o lifecycle para contornar isso.

## Capabilities e preflight .NET

O journal anuncia as sete garantias locais de identidade/cursor/replay/binding somente
quando armado e saudável. **Ele não anuncia `dispatchInspection=true`**, pois não é o
owner do dispatcher. A inspeção do coordinator é uma superfície separada; o adapter
host-bound deve correlacionar ambas, nunca aceitar um JSON do projeto como prova.

`NativeObservationPreflight` exige versão V1, health saudável, sessão exata, todas as
oito capabilities e dispatch `inactive`. Esses checks estão nos gates obrigatórios;
a CLI atual passa pelo mesmo avaliador com fonte `unbound/unavailable`, permanecendo
NOT READY. O transporte MCP instalado não foi substituído por fixtures ou por flags
de configuração alegando garantias inexistentes.

A superfície local está implementada, mas não foi publicada como uma nova RPC na
instalação ativa. Binding do reader nativo ao adapter live e comprovação de snapshot
efetivo são pendências explícitas, não readiness obtida nos testes.

## Shadow Run não resumível

`ShadowInboundJournal.StartNonResumableRun` grava boundary `shadow-run:v1` com RunId
novo, revisão pinada, epoch/cursor novos. Preserva todos os IDs já vistos; trabalho
antigo observado/admitido é abandonado, nunca retomado. O runtime novo começa do
default do projeto; não restaura FSM anterior e não registra fala shadow no history.

O preflight pode validar o journal anterior para essa nova run sem escrever uma run
ou iniciar execução. Arquivo corrompido/binding diferente continua falhando. Um novo
snapshot saudável continua obrigatório; reset de run não corrige integridade da source.
`FromMe` nativo ou actor self suprime execution, mantendo avanço/dedupe do cursor.
Nada disso define recovery para DM Live.

## Verificação e limites

Testes locais usam bancos SQLite reais temporários, triggers de falha de INSERT e de
COMMIT, e filas/monitor originais; não usam fila substituta para provar atomicidade.
Testes de identidade/config usam entradas artificiais identificadas como testes,
NÃO como evidência da DM live. Testes Graphiti envolvidos são exclusivamente fakes.

Verificação final local em 2026-09-11:

| Suíte/verificação | Resultado |
| --- | --- |
| Native observation + ingress queue + ingress monitor | 84 testes passaram |
| WhatsApp identity + dispatch inspection + durable receive | 22 testes passaram |
| SQLite schema compatibility/additive | 3 testes passaram |
| Filomena.Core.Tests, filtro `FullyQualifiedName~Shadow` | 59 testes passaram |
| Filomena.Llm.Tests, mesmo filtro, somente fakes | 14 testes passaram |
| `run-tsgo.mjs -p tsconfig.core.json` | exit 0 |
| `run-tsgo.mjs -p tsconfig.extensions.json` | exit 0 |
| `generate-kysely-types.mts --verify` | exit 0 |
| `oxfmt --check` nos 13 arquivos TS alterados | passou |
| `git diff --check` nos dois worktrees | exit 0; avisos CRLF no worktree Filomena |

Total focado: **182 testes passaram**, não a suíte completa dos projetos.
O teste de epoch encerrado foi observado falhando antes da correção e passou depois.
Os testes de falha transacional verificam ausência de rows parciais e retries esgotados.
As leituras usam um único statement SQLite; foi testada a fronteira snapshot/evento
posterior, mas não se está alegando um teste de stress multi-processo concorrente.
A composição com o adapter live/coordinator efetivo ainda não foi exercitada: requer
o contrato restante abaixo. Aprovação local não substitui essa prova.

Sem listener contínuo, sem `messages_send`, sem outbound, sem execução real de agent,
sem Graphiti live, sem mutation externa, sem configuração/restart/relink do Gateway.
Prompts, Luna, grounding B, FSM, Boundary e SendGuard não foram alterados nesta rodada.

## Próximo contrato mínimo / ação manual

Antes de transport live preflight:

- disponibilizar o reader V1 no processo que realmente possui a admissão nativa,
  com binding host-owned da rota exata e sem expor arming em RPC/project JSON;
- fornecer snapshot **read-only e efetivo** da configuração desse owner, com revisão;
- verificar isolamento real; não basta o arquivo dizer `transportOnly`;
- revisar build/implantação e autorizar manualmente o opt-in experimental. Nada disso
  foi aplicado à instalação ativa nesta rodada.

Uma opção de isolamento a discutir separadamente é `dmPolicy=disabled` e `allowFrom=[]`
na configuração efetiva da conta alvo. Isso tem impacto no legado e approvals; NÃO é
instrução para alterar agora. Depois da autorização/ação manual, o owner deve comprovar
os valores efetivos. Configurar por conta é importante para não modificar outras contas.

| Gate live | Estado nesta rodada |
| --- | --- |
| Transport live preflight | NOT READY: source/owner efetivos ainda não ligados/comprovados |
| Graphiti live preflight | NOT READY nesta sequência; não executado |
| Primeira Shadow Run | NOT READY; não iniciada |

Sem commit de fechamento e sem push nesta rodada. A verificação focada está verde,
mas o binding live e a inspeção da configuração dinâmica permanecem pendentes.
