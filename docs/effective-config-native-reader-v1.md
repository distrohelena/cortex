# Effective Configuration Snapshot + Native Observation Reader V1

Status: implementação isolada; nenhuma implantação ou prova live nesta rodada.
Data: 2026-09-11. Esta evidência não autoriza Shadow Live.

## Escopo e fronteira

```text
owner nativo de configuração + coordinator + registry carregado
    → inspeção read-only vinculada à revisão
journal SQLite nativo
    → reader HTTP loopback autenticado, somente GET
    → NativeObservationReader .NET
    → transport preflight (sem runtime conversacional/listener)
```

Checkouts usados:

- OpenClaw v2026.8.1: `C:/Users/Helena/.config/superpowers/worktrees/openclaw/native-observation-v1`.
- Filomena: `C:/Users/Helena/.config/superpowers/worktrees/filomena/typed-flow-slice2`.

Não houve instalação, mudança de Gateway/config pessoal, rearm de captura ativa,
consulta Graphiti, chamada de modelo, `messages_send`, listener da DM ou Shadow Run.
Os testes HTTP usaram loopback temporário sem conta real; SQLite usou diretórios
temporários. Testes de lifecycle preexistentes usam fakes, não agentes reais.

## 1. Snapshot efetivo: owner, consistência e revisão

Owner: `src/config/runtime-snapshot.ts`, estado `runtimeConfigSnapshot` e seus
metadados de publicação. Nova função `readEffectiveRuntimeConfigSnapshot(project)`;
exportação pelo SDK existente `runtime-config-snapshot`.

```text
{ version: "effective-runtime-config:v1", revision, capturedAt, value }
```

A leitura é síncrona, de memória, sem `loadConfig`, refresh, acesso ao arquivo de
configuração, access control ou resolução de credenciais. A projeção recebe um
clone destacado do estado já resolvido. Antes de retornar, confere referência do
owner, metadados, epoch e fingerprint novamente. Mudança durante a projeção ou
estado não clonável resulta em ausência de evidência, nunca em snapshot aproximado.

A revisão combina epoch aleatório do owner, contador de publicação e fingerprint
canônico. Publicação, mutação in-place e reset do owner invalidam revisões antigas.
`capturedAt` é instante da leitura, não identidade da revisão.

A projeção WhatsApp (`inbound/effective-dispatch.ts`) seleciona apenas política DM,
allow rules efetivas da conta e entradas de plugin relevantes. Não exporta tokens,
diretórios de autenticação, modelos ou o documento completo. Valores dinâmicos não
resolvidos não são executados para tentar obter uma decisão.

## 2. Dispatch Inspection e estado realmente carregado

O monitor nativo fornece a leitura do owner ao coordinator. Este registra uma
lease de inspeção por canal/conta e a remove ao parar intake. Ausência ou sobreposição
de owners é `unknown`. Nenhum callback legado de mensagem é usado como probe.

A inspeção considera tanto configuração efetiva atual quanto configuração retida
pelos caminhos já construídos. Fechar a configuração atual não prova que um handler
antigo foi reconstruído. Nesse caso retorna `retained_runtime_paths_not_excluded`.

Famílias diagnosticadas: allow rules, approval reactions pré-admission, commands,
plugins, agent dispatch, route bindings e comportamento transport-only. A prova
estática existente usa o gate nativo de DM antes de hooks/commands/agent; considera
separadamente os approvers de reactions, que podem agir antes da admissão.

Além da configuração, `native-observation-owner.ts` lê o registry ativo e sua geração
pelos getters de `src/plugins/runtime.ts`. Não invoca registros. Ausência do registry,
troca durante a leitura ou owner ambíguo falha fechado. Registrations opacas de hooks,
commands, interactive handlers, bindings, subscriptions, lifecycle, services,
schedulers, session actions, HTTP/Gateway handlers e plugins carregados não cobertos
pelo owner nativo impedem uma prova de exclusão.

| Resultado | Semântica implementada |
|---|---|
| `inactive` | Todos os caminhos cobertos foram excluídos: gate DM fechado, approvers vazios, configuração retida também exclui caminhos e nenhum registro opaco observado. |
| `active` | Há caminho nativo alcançável para a sessão exata. Não significa que todo texto necessariamente causaria uma resposta. |
| `unknown` | Owner/registry ausente, configuração incompleta, regra não resolvida, retained state incompatível, plugin/handler opaco ou outra falta de prova. |

`transportOnly` isoladamente nunca prova exclusão. Plugins opacos continuam
`unknown`, inclusive quando o gate DM está fechado; não se presume que tenham
somente os entrypoints conhecidos. Diagnósticos não despejam configuração.

Revisão da inspeção = lifetime do coordinator + revisão efetiva + fingerprint do
estado retido + geração do registry ativo. Configuração R2, reconnect/coordinator
novo ou registry novo invalidam a aprovação de R1.

## 3. Surface nativa read-only

Factory: `src/channels/message/native-observation-reader.ts`,
`createNativeObservationReaderServer`. Retorna servidor **não iniciado**.
Não chama `listen`, não arma captura e não recebe sender/model/legacy queue handler.

Surface autenticada por bearer host-owned, somente loopback, GET, sem browser Origin:

```text
GET /inbound-observation/v1/status?session=<opaque-ref>
GET /inbound-observation/v1/open?session=<opaque-ref>&revision=<R>
GET /inbound-observation/v1/poll?session=<opaque-ref>&revision=<R>&cursor=<json>
```

`status` fornece health, capabilities, refs opacas de sessão/self/peer, binding de
rota e inspeção revision-bound. `open`/`poll` exigem revisão atual e dispatch
`inactive`; divergência retorna HTTP 409. A conferência e leitura SQLite são
síncronas, sem `await` entre elas. A factory também confere que o host do journal
está ligado à mesma queue/conta/sessão dos parâmetros de rota.

Não existe endpoint para configurar, armar captura, aprovar uma sessão, ack,
complete, tombstone, executar agente ou enviar. Erros não viram batch vazio.

## 4. Adapter .NET

Arquivo: `src/Filomena.OpenClaw/Observation/NativeObservationReader.cs`.

- `InspectAsync`, `PreflightAsync`, `OpenAsync`, `PollAsync`, `ToInbound`.
- Versão estrita `inbound-observation:v1`; versões desconhecidas falham.
- Approval é objeto reader-local com construtor interno. JSON, revision string ou
  approval de outro reader não criam autorização para Open/Poll.
- Session ref e route-binding digest são conferidos contra o binding host-owned;
  um record de outra sessão invalida o batch inteiro, sem filtragem silenciosa.
- Preserva native ID, sequence, epoch/cursor, ator, self, fromMe, timestamp nativo
  quando existente e payload nativo destacado.
- Sequence contígua, IDs únicos no batch, enum de health e identidade de cada row
  são validados. Corrupção/gap não produz batch parcial.
- Native self PN/LID já é canonicalizado no journal a partir dos aliases copiados
  do coordinator. O reader vincula a ref resultante ao self confiável do host; não
  compara nomes nem usa texto/timing. `fromMe` é preservado.
- `ToInbound` projeta texto nativo simples ou `extendedTextMessage.text` para o
  envelope tipado existente. Não injeta payload nativo/aliases no texto do modelo.
  IDs estruturais seguem o binding trusted já usado pelo runtime. Mídia/variantes
  não suportadas falham explicitamente (`unsupported_payload`), sem descarte oculto.
- `occurredAt` ausente continua ausente no record. O envelope pode usar `receivedAt`
  nativo como fallback, sem afirmar que esse timestamp é o horário original.

Health: somente `healthy` passa; `degraded`, `gap_detected`, `cursor_expired` e
`unavailable` permanecem distintos. Falha HTTP, parse ou schema nunca é memória/
batch vazio válido. Redirects estão desabilitados no cliente do comando.

## 5. Snapshot, replay e restart

Sem mudança no journal: epoch/start boundary explícito, sequence por epoch/sessão,
payload preservado após tombstone legado, dedupe por identidade nativa, retenção
existente de sete dias/5.000 registros e expiration explícita.

Open retorna snapshot e C0 na mesma leitura consistente. Snapshot é contexto, não
lista de executions. O preflight não hidrata nem cria runtime conversacional.
Poll após C0 retorna novos records; repetir Poll pode repetir records com os mesmos
IDs. O durable inbound registry .NET decide se já os processou.

Testado com reader e registry: snapshot vazio → zero executions; E1 após C0 → uma
execution no SessionFlowRuntime real; replay → nenhuma segunda execution. Recriar
reader e usar cursor persistido preserva IDs. Self não é admitido como execution.

Shadow continua não resumível: processo/run termina; próximo start exige preflight,
project pin e snapshots novos, participation default e dedupe durável. Cursor de
epoch fechado serve para leitura/replay, não para retomar execution antiga. Nenhuma
fala shadow vai para history. Recovery de DM Live continua fora do slice.

## 6. Bootstrap/preflight

O comando existente agora separa static de `--transport-read-only`. A opção antiga
genérica `--live-read-only` é rejeitada: não há passagem automática para Graphiti.

Campos adicionais do documento host-only:

```text
source = native-observation-v1
observationEndpoint = http://127.0.0.1:<operator-port>/
observationTokenEnvironmentVariable = <nome da variável, não o segredo>
observationSessionRef = <ref fornecida pelo host nativo>
```

Continuam obrigatórios project pin, shadow ceiling, exact DM, namespace e canonical
self/peer, bindings revisados e journals locais. Project não fornece autoridade.
Static não conecta serviços. Transport verifica status/capabilities/revisão,
reconfere aprovação e faz um Open read-only revision-bound; não faz Poll loop.

Saída JSON inclui `transportReady`, `effectiveConfigRevision`, checks e resultado
global. Transport pode passar enquanto `ready=false` porque Graphiti segue
`not_run`. Exit 0 só para a fase explicitamente solicitada que passou; falha é 2.
Resultado serializado é audit, não capability reutilizável.

Comando futuro, **não executado contra instalação ativa nesta rodada**:

```text
dotnet run --project src/Filomena.App -- --shadow-preflight <host.json> --transport-read-only --json
```

## 7. Verificação e limites da evidência

- Owner snapshot: clone/coerência, publicação/mutação/reset, revision stable,
  ausência de loader e recusa de publicação durante a projeção.
- Dispatch: active/unknown/inactive, config retida, paths commands/plugins/agents,
  opaque plugin mesmo com DM fechado, registry ausente/trocado e handler nunca invocado.
- Reader nativo: SQLite real temporário + HTTP loopback, snapshot/poll/replay,
  route/auth/method rejection, stale revision e inspeção sem queue mutation.
- Journal/regressões nativas: rollback, retries, tombstone, PN/LID, dedupe,
  restart, expiry, corrupção, fonte desarmada e leitura sem mutação SQLite.
- .NET: 12 testes de reader/contrato/handoff, 6 de CLI, 164 regressões focadas
  aprovadas no core e 1 teste ignorado pela suíte. Nenhuma integração live usada.
- Typechecks core/extensions e formatters executados no checkout isolado.
- Rodada final OpenClaw: 106 testes aprovados (19 runtime-config, 69 WhatsApp,
  18 channels), sem falhas; typechecks core/extensions com exit 0. `git diff --check`
  passou nos dois worktrees. Total .NET focado: 182 aprovados, 1 ignorado.

O HTTP real foi testado contra journal nativo local. O adapter C# foi testado contra
fake HTTP de contrato e runtime real; não se afirma um teste live cruzado entre
os dois processos instalados nem prova da configuração pessoal da Helena.

## 8. Operação futura e readiness

O código fecha a ausência de snapshot/reader; não prova que o deployment atual
está isolado. A instalação ativa não recebeu estes arquivos, não tem reader armado
por esta rodada e não foi consultada.

Antes do transport preflight real, é necessário aprovar explicitamente a implantação
do checkout revisado, composição do host reader no processo nativo com coordinator
e owner reais, bind loopback/token/session e opt-in de captura. A factory não inicia
servidor por conta própria. A conta lógica da inspeção e a conta hash de 24 hex da
queue WhatsApp precisam corresponder; o parâmetro `queueAccountId` torna isso explícito.

Se a inspeção real retornar active/unknown, a operação para. A única prova de
exclusão nativa atualmente reconhecida depende de `dmPolicy=disabled` e `allowFrom=[]`
efetivos, inclusive no estado retido, e de não haver paths opacos. Esses campos
pertencem a `channels.whatsapp`/`accounts.<id>` com herança resolvida pelo owner.
Alterá-los pode afetar **todas as DMs da conta**, não só Helena: não é uma alteração
automática nem uma recomendação para fazê-la sem decidir esse impacto. Plugins/
handlers opacos exigem exclusão ou prova específica posterior; não basta declarar
isolamento. Novo preflight deve verificar a revisão efetivamente carregada.

| Gate operacional agora | Resultado |
|---|---|
| Transport live preflight | **NOT READY operacionalmente**: código testado; faltam provisionamento/arm explícitos e autorização para probe real. |
| Graphiti live preflight | **NOT READY**: depende primeiro do transport live verde. |
| Primeira Shadow Run | **NOT READY**: depende dos dois preflights live e aprovação separada. |

Não há blocker conhecido de snapshot coerente ou reader que exija executar access
control/agent. A existência de plugins/handlers opacos na instalação real permanece
uma condição possível de bloqueio, não verificada nem mascarada por fakes.

Sem commit de fechamento e sem push. Os worktrees contêm mudanças anteriores
dependentes; foram preservados sem staging indiscriminado. Nenhuma revisão externa
ou chamada de modelo foi executada.
