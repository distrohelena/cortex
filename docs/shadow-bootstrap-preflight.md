# Shadow Bootstrap + Preflight — Fase 0

2026-09-11. Infraestrutura implementada; **NOT READY para Shadow Live**.
Nenhum listener, modelo, Graphiti live, MCP read de mensagens pessoais ou transport send foi iniciado nesta rodada. Nenhuma configuração operacional existente foi modificada.

## Fronteiras implementadas

```text
--shadow-preflight [host.json] [--json] [--live-read-only]
  → entrada antecipada, antes do host/lifecycle legado
  → config exclusiva do host
  → bytes do projeto + SHA-256 esperado → PinnedShadowProject
  → checks estáticos + opcional Graphiti read-only explícito
  → resultado humano ou JSON; exit 0 somente READY, caso contrário 2
  → termina SEM listener, inclusive se READY

Handoff interno, exercitado somente com fakes de transporte/modelo/memória:
  envelope de fonte confiável → binding exato/DM/self
  → journal inbound durável → canonicalização dos refs
  → SessionFlowRuntime.ReceiveAsync (batching/queue originais)
  → recall/compiler/enforcement reais → Enviar shadow
  → WOULD_SEND, sem transport/history outbound/transição por fala
```

O preflight não instancia `FilomenaTurnWorker`, `ConversationTurnManager`, `InboundBridgeHttpServer`, `FilomenaAgent` ou sender MCP. `Program.cs` retorna antes de `FilomenaHostConfiguration.CreateBuilder`. O código de handoff é **internal**, sem comando start, listener, endpoint HTTP ou factory de fonte live. Um relatório JSON READY é dado de auditoria, não capability executável.

O bootstrap live completo está deliberadamente bloqueado: não há adapter canônico elegível para conectar. Não disfarçamos um transcript reader como stream nativo. A composição de runtime/Graphiti/modelo para captura contínua só será exposta depois de sanar e verificar o contrato de origem. Nada do lifecycle antigo é usado como fallback.

## Fonte inbound: comparação concreta

| Fonte atual | O que sabemos pelo código | Decisão Fase 0 |
|---|---|---|
| `events_poll` | Observa fila de `session.message`, não consome os eventos; cursor é contador RAM iniciado em zero no processo MCP; `messageId/messageSeq` opcionais; retorna gap | Não elegível: identidade nativa/restart, self, anchor e lifecycle não comprovados |
| `events_wait` | Mesma fila/identidades de poll; aguarda evento; não cria persistência/replay nativo | Não elegível pelos mesmos motivos |
| `messages_read` | `sessions.get`, histórico/transcript; não é inbox nativo nem atomic-tail de live ingress | Só histórico diagnóstico, nunca fonte de novas executions |
| HTTP bridge `message_received` | Encaminha providerMessageId, rota/namespace, sender e timestamp; adapter TS não usa hash de conteúdo para fabricar ID; receiver C# ainda aceita sender ausente e deduplica em RAM | Melhor ponto futuro para expor evento nativo, mas hoje sem self explícito, cursor/epoch/replay, autenticação de handoff e exclusão de dispatch legado |

Referências do repo:

- `src/Filomena.OpenClaw/Mcp/OpenClawConversationMapper.cs`: requires presence de ID/cursor, mas não prova sua origem/estabilidade; `role=user` não prova interlocutor humano externo; sender/timestamp opcionais.
- `src/Filomena.OpenClaw/Mcp/OpenClawMcpTransport.cs`: `ListenAsync` começa em cursor zero e recupera snapshots em gaps. Não foi reutilizado.
- `src/Filomena.OpenClaw/Bridge/OpenClawInboundBridgeReceiver.cs`: envelope schema1 não tem `fromMe/selfJid/selfLid`, epoch ou posição durável. Nenhuma alteração feita.
- `plugins/openclaw-memory-graphiti/event-bridge.ts` e `channel-adapters/whatsapp.ts`: forwarding read do evento não comprova ownership exclusivo da conversa. Nenhuma alteração feita.

Código instalado reinspecionado read-only:

- `openclaw/dist/channel-server-DvhWr6Rt.js`: cursor inicial linha 108; incremento 398–400; `session.message` 460–464; projection `messageId/messageSeq` 532–540; poll/wait 650–685; read é transcript.
- `.openclaw/extensions/whatsapp/dist/monitor-CySzv38g.js`: `selfJid/selfLid/fromMe` existem upstream nas linhas 1294–1297. A existência upstream **não prova** exposição na bridge atual nem estabilidade de todo o binding PN/LID.

Não fizemos leitura de configurações pessoais para deduzir que o agente antigo estaria inativo. Não há prova de exclusão do dispatch legado nessa DM; esse check permanece FAIL. A entrada separada garante que **nosso** preflight não chama lifecycle antigo, não que outro processo esteja impedido de processar a mesma DM.

## Contrato mínimo necessário da bridge (próxima aprovação)

Para uma única conta/canal/DM:

1. Envelope autenticado de observação nativa: account, channel, canonical session, kind DM, stable native provider message ID, actor identity e self identity/fromMe estruturais. PN/LID só por mapeamento comprovado, nunca nome/texto.
2. Stream **session-scoped** durável com epoch e posição ordenada, atomic-tail inicial, leitura posterior ao anchor e indicação inequívoca de gap/retenção. O journal atual espera posições contíguas nesse stream; não converter o cursor MCP global para essa semântica por heurística.
3. Prova operacional atual de que dispatch legado da rota está excluído, sem desativar apenas por suposição. Pode ser uma capacidade/lease de observação exclusiva da origem ou evidência administrativa verificável vinculada à rota/época; um boolean `legacyInactive` no JSON não é prova.
4. Timestamp e sua origem, preservados; receive timestamp é aceitável se declarado, nunca apresentado como timestamp original inventado.

Reply/thread avançado, grupos e mentions são deferred. A bridge TS já encaminha um ID fornecido; precisamos comprovar a sua ligação ao ID nativo e preservá-la no novo contrato. Não foi criado adapter heurístico nesta rodada. Não alterar Gateway/bridge sem nova autorização.

## Pinning e authority

`PinnedShadowProject.FromBytes` valida SHA-256 **dos bytes exatos** do artifact e o compiler normal de projeto; extrai `project` do wrapper existente, clona e congela. Revision usada pelo runtime = digest. Hot reload não existe no handoff. Mutation do arquivo/array posterior não muda a revisão.

Host config contém: projectFile/projectId/projectVersion/expectedProjectSha256, source, outboundMode, journalDirectory, SessionKey completo, conversationNamespace, selfActorIds/peerActorIds, outboundBindings, graphitiEndpoint e graphitiProbe. `source` só reconhece os dois contratos atuais, ambos bloqueados. Campos extras como `legacyInactive` ou capability booleans são rejeitados. O project não escolhe sessão, destinatário, namespace do host ou outbound ceiling.

Config está fora do artifact do personagem. Não criamos configuração pessoal nem escolhemos a DM por nome. O report usa digest/ref opaca, canal e diagnóstico de rota **configurada, ainda não resolvida live**. O operator deverá confirmar o binding real antes do gate. No modelo atual de memória, namespace WhatsApp não inclui account; uma conta/DM exata é obrigatória e o namespace live precisa ser verificado, não presumido.

O artifact histórico `typed-flow-slice42.json` não foi reescrito. O preflight também exige roles explícitas e um sink `send` na revisão escolhida; ele não insere Enviar nem reconecta transitions automaticamente. Uma revisão operacional adequada deve ser selecionada/aprovada explicitamente.

## Uso (sem iniciar nada contínuo)

```powershell
dotnet run --project src/Filomena.App -- --shadow-preflight
# Sem host config: NOT READY, exit 2, nenhuma dependência externa.

dotnet run --project src/Filomena.App -- --shadow-preflight <host.json> --json
# Static: nunca faz leitura de Graphiti/transport. Não inicia listener.

# Somente após autorização operacional e binding/endpoint/canaries confirmados:
dotnet run --project src/Filomena.App -- --shadow-preflight <host.json> --live-read-only --json
```

O terceiro comando **não foi executado**. Ele opta explicitamente por probes Graphiti read-only; não resolve gaps de transporte e nunca inicia listener. Endpoint não é inferido de config pessoal ou de defaults do Gateway. Timeout de 30 s segue o client existente, sem retry novo. O operador provisiona previamente o diretório de journal; preflight não cria uma árvore de deployment.

## Watermark, dedupe, crash e ordering

- Nenhum backlog é tratado como nova execution: initial tail é fixado por fonte confiável futura. `sequence <= initialTail` = backlog, sem handoff.
- Journal inbound separado (`shadow-inbox:v1`); header fixa binding incluindo namespace/self/peers, project revision, epoch e initial tail. Não resetar anchor no restart.
- ID primário = provider ID, scoped pela sessão. Hash apenas torna essa referência opaca; **não** é hash do conteúdo nem heurística de tempo.
- Writer exclusivo, `WriteThrough`, UTF-8 estrito, `Flush(true)` antes do ReceiveAsync. Registro truncado/corrompido falha; nada é apagado/reparado automaticamente.
- Estados: `observed → admitted(watermark) → completed|execution_failed(batch,execution)`; `self`/`duplicate` não abrem execution; `quarantined` exige reconciliação.
- Duplicata com ID/posição já vistos é ignorada; repetição do mesmo provider ID em nova posição contígua avança cursor sem novo handoff. Posição conflitante, gap/out-of-order ou epoch inesperado são preservados e bloqueiam; não inventar reordenação.
- Observe ocorre antes do handoff; admit só depois de ReceiveAsync. Crash em qualquer janela não resolvida preserva a mensagem e exige reconciliação. Batch/execution são associados no checkpoint de outcomes, não inventados antes de o runtime defini-los.
- Mensagens durante geração entram normalmente por ReceiveAsync; queue/batching/FSM continuam os do SessionFlowRuntime. O handoff não aguarda geração para aceitar a próxima mensagem. `DrainAsync` é checkpoint explícito para testes, não poll de transporte.
- Canonicalização copia texto/display label, mas reconstrói remetente/canal a partir do binding estrutural. IDs/UUIDs de um payload não substituem a autoridade da origem. Reply enriquecido é null/deferred, nunca reconstruído de texto.
- Após restart com executions anteriores, dedupe continua disponível, mas retomada do runtime exige restaurar/reconciliar history/state; **não há restore automático nesta V1**. Preflight/handoff bloqueiam esse caso, inclusive quando os registros de executions estão completos. Não fingir que runtime vazio lembra a conversa anterior.
- Inbound pode ser preservado em quarentena durante estado indeterminado/desarme, mas não abre nova execution. Rearm não elimina reconciliação. Não implementamos replay/resolução automática nem distributed event store.

### Privacidade do inbox

O journal inbound é storage operacional privado: precisa preservar o envelope/conteúdo para não perder dados antes de admission/crash. **Não é log comum**, não deve ir para git ou relatórios, e contém identificadores de transporte. Usar diretório exclusivo do operador com ACL adequada. A CLI não imprime esse conteúdo. Trace normal (`ShadowIngressAudit`) contém somente refs opacas e ligação batch/execution; runtime debug detalhado existente é separado. Não há Graphiti writer nem derivação de memória a partir do journal.

## Graphiti

`ShadowGraphitiProbe` usa `IGraphitiReadClient` existente (`get_entity_neighborhood`/`search_memory_facts`), mais `GraphitiMemoryReader` real para recalls Boundary e social. Valida namespace bruto dos facts antes de qualquer filtro. Participant UUID/stable_id/label Person precisam aparecer como evidência; só calcular o UUID esperado não basta.

Canary isolation exige controles positivos dos markers já existentes nos respectivos namespaces e exclusão cruzada. Empty não é prova de isolamento. Não criar canary ou memória. Probes ausentes/outage/timeout/schema inválido/fact de namespace errado produzem FAIL; nenhum erro vira `[]`. Vazio legítimo no recall = `success_empty`. Request de probe precisa corresponder ao namespace e peer configurados pelo host.

Readiness real de Graphiti/canaries **não medida nesta rodada**: nenhum endpoint/binding operacional de Helena foi assumido. Tests usam fakes, não são apresentados como prova LIVE. Os canaries históricos PRIVATE91/DRIFT42/PLEX73 permanecem evidência anterior, não PASS automático hoje.

## Outbound e estado

Host de preflight é novo e isolado, nasce disabled, só rearma shadow mediante config explícita; não modifica deployment existente. `enabled` solicitado fica FAIL. Send continua impossível por ausência de dispatch e gate de capabilities (incluindo single physical message/chunking). Shadow não append history nem transiciona por fala; hypothetical transition é apenas trace.

Preflight abre/valida o journal outbound sem criar attempt. Pending/indeterminate real exige reconciliação; handoff preserva inbound, mas não abre execution. Nenhum send, retry worker, echo bridge, writer Graphiti ou implementação DM Live foi adicionado.

## Readiness / cobertura

- Estrutura estática, pinning, journal, handoff e probes read-only implementados/testáveis.
- Fonte canônica live: **bloqueada**; stable inbound identity/self/durable cursor/legacy isolation não comprovados.
- Sessão exata: config validável, resolução LIVE ainda não comprovada.
- Graphiti LIVE + namespace/canaries: probes implementados, não executados contra serviços reais.
- Listener/start live: deliberadamente ausente. Resultado atual **NOT READY**, mesmo que Graphiti venha a passar.
- Restart de sessão processada: exige restauração/reconciliação antes de retomar; nenhum replay automático.

Testes novos: backlog/tail, duplicate, replay após restart, self, sessão/canal/group/namespace/sender/ID inválidos, ordem/gap/epoch, journal truncado/writer exclusivo, crash antes/depois de admission, duas mensagens durante geração, pinning, refs fabricadas no payload, disarm, schema de config, Graphiti memory/empty/outage/canary/participant, runtime real até shadow/outage sem approved sink. Suítes anteriores continuam cobrindo fake/cross-execution provenance, regeneration, kill switch, no outbound append/transition e enabled/chunking fail-closed.

**Não autorizar primeira execução Shadow Live ainda.** Próximo trabalho depende de aprovar o contrato mínimo da bridge e evidência operacional de isolamento. Não há mudança de prompts, modelo, compiler Boundary, detectors, FSM, memory semantics ou configurações externas neste slice.

## Verificação desta rodada

- 474 testes Core passaram, com testes live/pagos excluídos explicitamente.
- 52 testes backend focados passaram: shadow/preflight, Graphiti fake, project roundtrip, boundary/multi-detector e session integration.
- 102 testes frontend passaram.
- Build da solução: zero warnings/errors.
- `dotnet format --verify-no-changes` dos arquivos novos: exit 0.
- `git diff --check`: exit 0; somente avisos históricos de conversão LF/CRLF.
- Entrada CLI real, sem host config: `ready=false`, native exit code **2**, nenhum host/listener. Saída JSON preservada em [shadow-bootstrap-static-preflight.json](shadow-bootstrap-static-preflight.json). Falhas de binding/projeto/Graphiti nesse arquivo significam **não configurado/não executado**, não uma medição negativa da DM real.
- Configs sintéticas isoladas dos testes provaram pinning, shadow/disabled/enabled fail-closed e Graphiti não consultado no modo estático. Não representam configuração operacional de Helena.
- Fixture congelada SHA-256 preservado: `C468F9EE2594A1C671B7BDA11E67A5F95CE39F88DCCD8F82984B34E3E6540676`.

Zero transport calls, zero modelo e zero leitura/mutação live de Graphiti. Servidores HTTP locais efêmeros pertenciam somente aos testes existentes de backend. Sem commit local: o worktree já contém uma cadeia extensa de mudanças/arquivos experimentais anteriores não commitados dos quais este slice depende; não agregamos essa história num commit de fechamento. Sem push. Revisão e testes desta rodada feitos pelo agente; nenhuma revisão independente foi lançada.
