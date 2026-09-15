# Outbound controlado — design proposto
Data: 2026-09-11. Status: **design aprovado; implementação limitada à Fase 0 disabled/shadow. Nenhum envio real.**

## Ajustes obrigatórios aprovados / limite da Fase 0

- Enabled tem gate determinístico de capabilities: identidade estável de origem e inbound, route binding, self suppression, receipt, correlação de echo e **single physical message**. Falta de qualquer propriedade bloqueia. O MCP atual não as comprova; suporte a chunks é blocker, não detalhe de representação. Uma chamada MCP não equivale a uma mensagem física. Antes de enabled, comprovar single-part garantido ou preflight confiável que rejeite chunking. Nesta implementação, mesmo capabilities completas retornam `phase0_dispatch_unavailable`: não existe dispatch real.
- Shadow não altera histórico nem estado por consequência da fala. `wouldTransitionTo` é diagnóstico, não um estado paralelo. Portanto Shadow Live NÃO prova a dinâmica pós-fala da FSM; essa evolução depende do gate DM Live.
- `indeterminate`/pending após crash exige reconciliação: zero retry automático, zero sent-history append e zero novo outbound naquela sessão. Inbound continua sendo preservado; não inferir entrega por texto/timing. A resolução operacional futura será explicitamente `sent | not_sent | abandoned`, auditada e baseada em evidência externa. Não implementar resolução automática. Até resolver, a agente não deve conversar como se soubesse o que foi recebido.
- Projeto nunca rearma host. `ShadowOutboundHost` inicia disabled, recebe allowlist exata e documento/roles revisados via API C# administrativa. O endpoint de simulação não recebe esse host. `Rearm` é plumbing operacional para um bootstrap confiável futuro, não um campo do project JSON.

### Concretização pequena nesta implementação

Sem adicionar dependência de banco: um journal append-only local, writer exclusivo durante o lifetime, UTF-8 e `Flush(true)` antes de publicar shadowed. Registro incompleto/corrompido bloqueia abertura; falha de escrita envenena o writer, não o reinicia vazio. Guarda refs opacas de sessão/execution/inbound, namespace `shadow:v1`, resultado shadowed e revisão do enforcement. Não contém texto/JID. Deduplica execution e interseção dos IDs de inbound contra registros duráveis, inclusive após novo GUID de execution/rebatch. Deduplicação antes da geração/inbox durável completa continua trabalho futuro de live dispatch; nesta fase o replay pode consumir geração, mas não produzir outro WOULD_SEND autorizado.

Essa redução é apropriada para um único evento terminal shadow, não implementa as transações futuras send/history/FSM descritas abaixo. Journal não é distributed queue, não tem worker de retry. Registros futuros `real:v1` pending/indeterminate são reconhecidos somente para bloquear; a Fase 0 nunca os cria, resolve ou promove. `sent/not_sent/abandoned` são vocabulário futuro, não autorização para reenvio.

Enviar V1/Fase 0: `exec + message:Texto`, outputs `status + diagnostico`; saídas de controle `disabled | shadowed | blocked | failed`, sem `then` genérico nem `sent`. Qualquer Trocar Estado alcançado após uma tentativa fica hipotético, independentemente de wiring indevido. Tentativas numeradas a partir de 1; uma única oportunidade por execution. Nenhum append, Graphiti writer ou sender é dependência do host.

Proveniência usa identidade de objeto TypedValue privado ao runner. Recall e compilação precisam corresponder às roles autorizadas pelo host e ao mesmo objeto de origem. Novo output/clone não herda autorização por igualdade de texto/hash. Generation/regeneration substitui a candidate corrente e revoga aprovação anterior. Documento pinado por hash exato inclui prompts/config/versions/wiring; mudanças de serialization também exigem novo pin administrativo. Permissão não vem do título do node.

History predecessor-outbound complement, persistência de inbox antes do processamento, receipts/echo bridge e commit sent/history/state permanecem inativos nesta fase. O design futuro abaixo permanece rationale, não declaração de implementação.

## Atualização: Shadow Bootstrap + Preflight

A infraestrutura de inbox/pinning/preflight posterior está descrita em [shadow-bootstrap-preflight.md](../../shadow-bootstrap-preflight.md). O handoff ao novo SessionFlowRuntime foi implementado e testado com fakes, sem listener. Dedup inbound agora possui journal separado; isso não restaura automaticamente history/FSM após restart. O bootstrap bloqueia retomada sem contexto restaurado/reconciliado.

O status live continua **NOT READY**: MCP transcript events não comprovam identidade nativa durável, self/cursor inicial ou isolamento do dispatch legado. A bridge não foi alterada. O preflight retorna antes do host legado, e checks ausentes/outage são FAIL crítico. Probes Graphiti são read-only e opt-in; não foram executados live nesta rodada. O restante desta seção preserva o histórico do design; nenhuma capability de enabled foi liberada.

## 1. Decisão arquitetural

Separar três autorizações que não se substituem:

1. O runtime confirma a origem e a integridade da candidate e que o enforcement obrigatório daquela execution terminou Allow.
2. A SendAuthorizationPolicy do host determina se este deployment pode materializar essa resposta nesta sessão.
3. O TransportAdapter materializa somente a rota confiável autorizada; não decide lifecycle, personalidade, conteúdo ou FSM.

Recomendação V1: **capability/proveniência execution-local + SendGuard no host + journal operacional durável**, um único payload textual para a sessão de origem. Enviar não escolhe destinatário e não aceita aprovação textual/JSON do modelo.

Fluxo proposto:
```text
inbound confiável → sessão/batch durável → runtime + Graphiti live
 → geração Luna+B → enforcement congelado
 → candidate com recibo interno de aprovação
 → Enviar → SendGuard + journal
    disabled → disabled
    shadow   → shadowed / WOULD_SEND (sem transport/history/transition de fala)
    enabled  → pending durável → recheck/dispatch → transport
       accepted → journal sent + history commit → saída sent → transição
       failed/indeterminate → terminal, sem regeneração e sem reenvio
```

Não substitui grounding por uma garantia determinística de veracidade. A capability comprova execução do processo exigido, não perfeição semântica do modelo.

## 2. Achados do baseline anterior à implementação da Fase 0

Esta seção preserva a inspeção read-only que fundamentou o design. Os acréscimos da Fase 0 estão descritos acima; os achados negativos do transport permanecem. Na inspeção não houve messages_send, poll/read de mensagens pessoais, alteração operacional ou chamada de modelo.

### Aplicação/runtime

- [OpenClawMcpTransport.cs](../../../src/Filomena.OpenClaw/Mcp/OpenClawMcpTransport.cs), SendTextAsync: chama messages_send com session_key/text; exige result.messageId e retorna SendReceipt. Não preserva channel/runId/toJid no recibo atual. Ausência de ID lança InvalidDataException com outcome unknown.
- [OpenClawMcpSender.cs](../../../src/Filomena.OpenClaw/Mcp/OpenClawMcpSender.cs): não repete sends; aposenta conexão falha para chamadas futuras. Isso NÃO controla retries internos do Gateway.
- [OpenClawConversationMapper.cs](../../../src/Filomena.OpenClaw/Mcp/OpenClawConversationMapper.cs): aceita eventos MCP role=user; sender/timestamp podem faltar. role=user não é prova de mensagem humana externa. A classificação DM atual por “não termina em @g.us” é insuficiente como autorização de envio.
- [OpenClawInboundBridgeReceiver.cs](../../../src/Filomena.OpenClaw/Bridge/OpenClawInboundBridgeReceiver.cs): recebe rota/sender/ID/timestamp estruturais, mas o envelope V1 não contém fromMe/selfJid/selfLid; não tem own-message suppression explícita. Dedup atual é limitado e em RAM.
- [event-bridge.ts](../../../plugins/openclaw-memory-graphiti/event-bridge.ts): forward apenas message_received, sem fromMe/self IDs; [whatsapp.ts](../../../plugins/openclaw-memory-graphiti/channel-adapters/whatsapp.ts) preserva @lid não resolvido e pode preferir senderE164 fornecido estruturalmente. Não resolve PN/LID por nome.
- [InboundBridgeHttpServer.cs](../../../src/Filomena.App/InboundBridgeHttpServer.cs): loopback, JSON, sem autenticação de origem visível nesse endpoint. Loopback sozinho não deve ser tratado como capability contra processos locais arbitrários.
- [SessionFlowRuntime.Execution.cs](../../../src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.Execution.cs): ReceiveAsync já admite Outbound em history sem abrir batch. Mas IDs/dedupe/sessões estão em RAM; CreateBatch gera GUIDs novos e congela History/HistoryWatermark no fechamento. QueueBatchLocked serializa execuções por sessão, NÃO impede admissão/fechamento de outros batches durante await. SelectHistory usa esse snapshot, não o history atual.
- Transição atual só é aplicada ao término de um run bem-sucedido, com compare-and-set do StateVersion/StateId. O sink experimental é observação anterior a esse commit; não representa uma confirmação de transporte.
- TypedFlowCompiler não tem node Enviar executável neste grafo. FilomenaTurnWorker ainda conecta bridge ao ConversationTurnManager legado; não presumir que iniciar o worker atual ativa o novo session DAG.
- [SessionFlowRuntime.Memory.cs](../../../src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.Memory.cs): deriva namespace da sessão, verifica participant UUID/namespace, chama memoryReader e aplica MemoryResultPolicy; exceções falham a execução. Isso não substitui verificar a configuração/isolamento do reader LIVE.
- [SessionFlowEndpoints.cs](../../../src/Filomena.App/Playground/SessionFlowEndpoints.cs): ingress de testes pode fabricar direction/route/outputs. Esse endpoint NÃO receberá capability outbound live.

### OpenClaw instalado

Fontes locais inspecionadas, sem editar Gateway:
- C:/Users/Helena/AppData/Roaming/npm/node_modules/openclaw/dist/channel-server-DvhWr6Rt.js, sendMessage (~265), message event (~506), tools (~618–702).
- send-CcjRWFvK.js, buildGatewayDeliveryPayload (~440), send (~978–1028).
- send-nMOWDy09.js, withDurableMessageSendContextCore.
- deliver-Cojdy1ar.js, mirrorDeliveredPayloads (~229), stage/enqueue (~1279).
- C:/Users/Helena/.openclaw/extensions/whatsapp/dist/monitor-CySzv38g.js, normalizer (~1042).
- send-C3S-NqyO.js, receipt/providerAccepted e suporte de chunks.

Achados:
1. messages_send aceita SOMENTE session_key e text. Resolve sessions.describe e usa conversation.to/channel/accountId/threadId. Cria randomUUID de idempotência em CADA invocação. Não aceita chave do chamador nem rota esperada.
2. Retorna structuredContent.result com messageId, channel, runId e alguns campos de rota conforme adapter. O texto “sent” do MCP sozinho não é confirmação suficiente.
3. Gateway aguarda o send durável e retorna o último result do batch. O provider WhatsApp trabalha com providerAccepted/IDs; isso comprova aceitação de envio, NÃO entrega/leitura no dispositivo de Helena. “sent” no nosso contrato significará provider-accepted.
4. Há pipeline de fila durável e recuperação; o ramo inspecionado usa skipQueue dependente de autoridade runtime. O MCP público não oferece controle de queue/retry. Logo “nenhum retry na aplicação” NÃO prova ausência de retransmissão/recovery no transport.
5. O transporte suporta múltiplos resultados/chunks; o MCP projeta somente o último messageId. Uma chamada não prova uma única mensagem física.
6. messages_read retorna sessions.get: histórico de sessão/transcript, não inbox WhatsApp bruto. events_poll/wait observam session.message, com cursor, role, messageId/messageSeq opcionais e raw. Cursor pertence ao processo MCP e não é chave durável de replay.
7. Após envio há tentativa de mirror assistant no transcript. Falha de mirror é logada após envio já bem-sucedido. A chamada de mirror inspecionada não passa o provider messageId como identidade do registro de transcript; não há prova de igualdade entre IDs das duas superfícies. Echo pode faltar e pode ter ID diferente.
8. WhatsApp nativo dispõe de fromMe/selfJid/selfLid e supressão de echo recente por fromMe+ID+rota. Esses campos não chegam integralmente à bridge C# atual. Supressão “recent” não é nossa garantia pós-restart.
9. Timestamp pode faltar em MCP; outbound do plugin usa horário observado de message_sent. Não usar timestamp/texto/display name para identidade ou dedupe.
10. O plugin existente captura outbound message_sent se autoCapture habilitado. Este slice não cria escrita Graphiti, mas não promete que a captura passiva existente deixe de escrever. Estado desse mecanismo deve ser declarado na homologação; não alterado silenciosamente.

**Consequência:** podemos aprovar a arquitetura e implementar shadow sem enviar. Enabled exige fechar as lacunas de receipt/rota/self/retry/chunking. O adapter MCP atual, sozinho, não comprova todos os invariants solicitados.

## 3. Responsabilidades e configuração

**Enviar:** node específico de efeito externo, não Instruction. Pede envio da candidate; sem provider, target, channel override, metadata livre ou retries. Não faz lookup por título.

**SendGuard / SendAuthorizationPolicy:** host-owned, determinístico. Valida capability, sessão, allowlist, DM, mode, revision, número de attempts, replay, tamanho e saúde operacional. É a única entrada para o sender. Chamadas legadas diretas ao sender devem ficar fora do deployment de homologação.

**TransportAdapter:** recebe OutboundContext interno e bytes/texto exatos. Verifica rota esperada, entrega uma vez e classifica resultado/receipt. Não adiciona prefixos, texto, split, fallback de canal ou conteúdo pós-enforcement; transformações inevitáveis devem ser comprovadamente de representação, não nova semântica.

**Outbound journal:** persistência operacional, não memória social. Reserva atômica, idempotência, recovery, history commit e auditoria.

Configuração máxima no deployment/host:
- mode=disabled por default e após restart; enabled exige rearmamento explícito;
- uma sessão exata, account/agent/runtime/channel, DM e binding de rota;
- projeto/revisão/enforcement manifest aprovados;
- versões de policy e epoch de autorização;
- limites de tamanho de payload compatíveis com single-part; sem truncar;
- health/reconciliation latch.

Projeto apenas solicita Enviar e pode restringir mais, nunca ampliar. Nenhuma configuração de permission em output de LLM. Alteração operacional autenticada por mecanismo administrativo do host, logando ator/version/horário, sem editar graph ou config pessoal. Entrada inválida/reload falho resulta disabled, não last-known-enabled.

disabled: resultado disabled, nenhuma chamada externa para envio; rejeições de schema podem ocorrer antes. shadow: executa as verificações relevantes, reserva tentativa shadow e simula decisão de history; sem transport send/append. enabled: valida tudo e faz recheck imediatamente antes do dispatch.

Modo nunca muda uma tentativa shadow antiga para real; nova autorização vale apenas para novas executions elegíveis, com epoch compatível.

## 4. Proveniência: alternativas e escolha

A. **Text + recibo privado ligado ao valor de origem**, recomendado nesta V1. Menor mudança visual; exige propagação de lineage segura no runtime.
B. ApprovedOutbound como tipo público com capability privada: mais claro no canvas, porém adiciona tipo/portas e tratamento de serialization. Um enum/tipo sozinho não autentica JSON.
C. Revalidar texto dentro de Enviar: duplica trabalho, muda latência/processo e não resolve origem por si. Não recomendado.

Em A, Enviar mantém inputs exec + mensagem: Texto. O TypedValue carrega internamente identidade de origem imutável, fora do JSON/LLM. Registry privado contém:
execution/batch/session/host-instance, candidateId, producer/value identity, hash e texto exato, versão generation/regeneration, approved project revision, boundary-set snapshot/fingerprint, resultado completo e versões do enforcement, instante de emissão e consumption flag.

Não basta procurar um hash igual em uma tabela: string idêntica fabricada por Instruction normal NÃO herda a capability. Apenas wires/aliases transparentes que preservam o mesmo valor confiável mantêm lineage; concatenação, property reconstruction, JSON/roundtrip ou novo output perdem-na. Capability não persiste nem atravessa execution.

O host exige um manifest do processo de enforcement, não simples resultado “permitir”. Remover Consultar Limites, fabricar lista vazia ou trocar um profile no projeto não pode mintar recibo. No empty set, apenas recall trusted bem-sucedido + compiler com resultado vazio completo emite o recibo de bypass; outage não é vazio. O finalizador confiável pode ser interno à estratégia/profile e ao bypass validado, sem novo node visual.

Block nunca emite capability. Regeneração tem outro candidateId, descarta aprovação anterior, exige novo enforcement. Falha de enforcement ou fonte inválida aborta. Recibo expirado, parcial, de outra execução/versão/sessão ou valor alterado: blocked invalid_provenance.

OutboundContext é argumento implícito do runtime, não porta serializável: origem, entry kind, route binding validado, epoch, execution. Nenhum modelo/graph pode construir esse contexto.

## 5. Exact-session e SendResult

Allowlist com igualdade exata da tupla SessionKey existente: RuntimeId estável de deployment, CharacterId, ChannelId, AccountId, NativeConversationId, mais DM e binding MCP interno. Sem curingas, nomes, substring de número ou “todos os contatos”.

Rota Helena será confirmada administrativamente em pré-flight, sem publicar JID neste documento. PN/LID alias só com evidência estrutural confiável; não ampliar allowlist para um alias desconhecido. DM reconhecido positivamente como peer permitido; qualquer grupo/status/broadcast/unknown falha mesmo se alguma allowlist acidental o citar.

A rota vem da sessão admitida por ingress autenticado/isolado do host, nunca do project/LLM. Resolver MCP session_key → destino deve concordar com essa tupla. **Preflight sessions.describe sozinho deixa uma race de resolução no send**: enabled exige binding estável e protegido contra remapeamento concorrente ou API/adapter existente capaz de fixar/validar a rota atomicamente. Não usar validação pós-envio como proteção do destinatário.

Output flat do node, alinhado a enum/string atuais, sem novo objeto aninhado:
- status: disabled | shadowed | sent | blocked | failed | indeterminate
- diagnostico: código controlado
- portas de controle correspondentes; sent é a única continuação para transição dependente da fala.
ProviderMessageRef fica no journal/trace interno, não precisa virar porta visual.

Distinções:
- blocked/not_authorized: allowlist, origem, provenance, duplicate, version, tamanho.
- failed/transport_unavailable ou transport_rejected: somente prova de que nenhum dispatch ocorreu.
- indeterminate/transport_failure: timeout/cancel/desconexão/output malformado depois de dispatch possível; rejeição parcial também indeterminate.
- disabled é explícito, não semantic Block.
Falha do send nunca volta à regeneração. Sem “then” que misture sent com failed.

## 6. Max-one, journal e idempotência

Um ordinal=0 por execution. Reserva compare-and-set exclusiva; mesmo duas branches paralelas não enviam duas vezes. Segunda entrada de Enviar retorna blocked duplicate_send_attempt. Primeira tentativa bloqueada/falha não abre uma segunda oportunidade dentro da mesma execution. Shadow também consome sua tentativa naquele epoch.

RAM + execution GUID não bastam: CreateBatch atual gera novos GUIDs após replay. Precisamos persistir, antes de work com efeito:
- inbox dedupe por deployment/channel/account/session/provider inbound ID;
- atribuição durável dos IDs de input ao batch/execution;
- unicidade de consumo desses inputs para resposta;
- attempt por execution+ordinal;
- estado do send e eventual history/transition commit.

Chave local: host installation identity + stable session key + executionId persistido + ordinal 0. Hash serve como ref opaca, não como autenticação. Nunca chave só do texto. Persistir association input→execution impede replay com GUID novo ou rebatching do mesmo evento. Novo conteúdo humano com ID novo é outro input; não deduplicar por texto.

Proposta mínima: um arquivo SQLite local transacional do host, tabelas pequenas inbox/execution/outbound/history-commit; unique constraints e uma instância writer por deployment (lease de processo). Sem broker, jobs genéricos ou outbox que reenvia. Alternativa append-only fsync é possível, mas exige construir atomicidade/recovery de múltiplos índices; não é menor semanticamente. Não depende de Graphiti. Perda/corrupção/inacessibilidade do journal desabilita enabled; não reconstruir vazio e continuar.

Estado durável:
reserved → pending_dispatch → sent | failed | indeterminate
shadowed/blocked/disabled também auditáveis; nunca promovidos a pending_dispatch.

pending_dispatch é commit/fsync ANTES de invocar o adapter. Kill switch é rechecado após preparação/espera por concorrência e no ponto de dispatch; um fence curto coordena mudança de modo com início da chamada, sem manter lock por todo network await.

No restart, pending_dispatch vira indeterminate (pode ter enviado). Não existe retry automático nem replay dos inputs associados. reserved antigo também não é reenviado automaticamente. Persistir tombstones por toda homologação; não expirar dedupe enquanto eventos antigos puderem reaparecer.

## 7. Crash, confirmação e transporte

sent = provider aceitou UMA mensagem com ID inequívoco e rota esperada, não “Helena leu”. Recibo atual C# deverá ser enriquecido internamente para preservar provider/channel/account/session/ref, sem inferir campos ausentes.

Se provider aceitou e processo morreu antes do journal sent: indeterminate, nenhum retry. Sem ID local não temos uma chave consultável pelo MCP para provar qual send foi esse. Poll/read com texto/horário parecido não resolve. Reconciliação somente por receipt/event correlacionável estruturalmente ou ação administrativa; até lá pausar novos outbounds da sessão por history incerto.

Se sent foi persistido mas history append/transition não: journal contém commit intent e texto autorizado protegido por ACL; reidratar/append uma vez antes de reabrir a sessão. Journal registra historyCommitted e transitionApplied com IDs únicos. Crash após append antes de ack não duplica history no recovery.

Falha de persistência após efeito remoto: indeterminate para a execution, health latch bloqueia próximos sends; não marca failed só porque o disco falhou.

**Sem retry da aplicação** em qualquer resultado. Não pedir que runtime “tente outra frase”. O contrato do provider precisa ainda demonstrar que não há split/recovery concorrente que viole single-message/no-retry. No MCP atual isso é uma lacuna real; não mudar Gateway para resolvê-la nesta rodada. Enabled permanece indisponível até evidência/solução bounded aprovada separadamente.

## 8. Own-message suppression e history canônico

Recomendação: **local append canônico após receipt**, não esperar echo.

Ingress distingue humanos, self/outbound, transcript mirror e unknown:
- fromMe confiável OU actor ∈ selfActorIds account-scoped → não abre execution;
- provider ID já em outbound journal → echo/dedupe, nunca input humano;
- evento assistant de transcript → evidência transport/mirror, não novo histórico canônico nem humano;
- evento ambíguo ou alias não resolvido → quarantine, não guessing por nome/texto.

Host self registry é verificado administrativamente/por adapter para a conta real. Não usar senderIsOwner como fromMe: owner pode ser Helena. Self PN/LID precisam estar cobertos; as identidades não são entregues à LLM. O fato de o WhatsApp interno suprimir echo recente não dispensa a defesa local durável.

Bridge atual perde fromMe. Enabled requer prova de que actor binding e self registry cobrem de forma inequívoca o envelope recebido OU extensão mínima metadata-only aprovada em outro passo. Se isso exigir mudança de Gateway/plugin fora da autorização atual, é blocker explícito, não fallback heurístico.

History:
1. receipt válido → journal sent + história proposta;
2. append local once, chave composta provider/account/session/messageId;
3. echo com esse ID → marcar observed, sem append;
4. mirror com ID próprio desconhecido → não importar como segunda fala; manter separado para diagnóstico.
IDs desconhecidos NÃO são fundidos por igualdade textual. Mensagens self manuais não correlacionadas ficam fora do piloto e causam revisão de contexto; não fingir que o agente as enviou.

O mirror do transcript pode não carregar ID de WhatsApp. Por isso poll/read não será fonte canônica de history nem recovery automático de inbox. Essa escolha resolve o atraso de echo sem prometer correlação que o contrato não oferece.

confirmed outbound → history NÃO chama Graphiti writer. O plugin passivo já existente poderá capturar message_sent conforme configuração atual; registrar e auditar essa interação antes de live, sem duplicar captura ou alterar configs silenciosamente.

## 9. Ordenação, watermark e FSM

Problema concreto: B2 pode fechar enquanto E1 gera; B2.History é congelado antes de E1 enviar. Append local posterior não aparece nesse snapshot, mesmo com E2 executando depois.

Mudança bounded necessária no runtime futuro: ao iniciar cada execução, após predecessor finalizar e antes de montar RuntimeOutputs, obter um **outbound commit frontier** e acrescentar ao history efetivo apenas outbounds confirmados de executions predecessoras da MESMA sessão, deduplicados. Congelar essa composição para toda E2. Não incluir novos inbounds além do watermark do batch, não repinar projeto/FSM, não inventar timestamps antigos nem renumerar admission watermarks.

Manter ordenação por sequence local confiável, não provider timestamp. Contexto pode conter a fala E1 enviada depois da mensagem que compõe B2; isso é temporalmente correto e deve conservar timestamps/direção. includeCharacterMessages precisa estar ativo no flow de homologação; se false, o host acusa configuração incompatível, não oculta esse requisito.

Exemplo:
t1 E1 gerando;
t2 inbound M2 admitido, B2 fecha com projeto/estado pinados;
t3 E1 sent + append + commit;
t4 E2 inicia, inputs M2 intactos, history inclui outbound E1 via frontier.
Sem cancelar E1 por M2. Conversas diferentes continuam independentes.

Estado dependente de ter falado: somente após provider-accepted + history commit, saída sent → Trocar Estado Engajada. Manter CAS existente. Admin state changes que conflitem com commit devem respeitar fencing/serialização da sessão; stale execution é bloqueada antes do dispatch quando detectável. Se staleness/incidente ocorrer após send, registrar sent sem mentir que foi abortado antes do efeito; pausar/reconciliar transição, não reenviar.

Para impedir graph mal conectado de usar failed/shadowed→Engajada, não basta confiança no wire. Host valida plano outbound do projeto e exige receipt sent para commit de transição derivada do ramo responder; transições legítimas de observar/esperar continuam normais. Não há “sempre engajar” no sender.

Crash recovery aplica somente commit pendente válido para epoch/state version conhecidos; divergência não é sobrescrita à força. Não restaurar estado “como se enviado” a partir de pending/indeterminate.

## 10. Shadow sem ficção de conversa

Escolha: shadow não altera history nem participation state por consequência de WOULD_SEND. Transições legítimas de presença/observação, não derivadas da fala, podem ocorrer no runtime shadow. “Teria ficado Engajada” aparece só em trace/counterfactual.

Alternativa simulando estado Engajada num fork ajuda testar caminho, mas não representa a conversa ouvida por Helena; NÃO usar esse fork para alimentar próximos recalls/prompts live. V1 não cria dois mundos de history.

Runtime shadow separado do legado, identidade/deployment explícita, mesmo namespace canônico Graphiti read-only. Ao mudar para fase live: nova epoch, nenhuma fila shadow é disparada, nenhuma fala shadow importada como enviada, sessão/histórico conferidos. Não incrementar allowlist automaticamente.

Zero outbound real exige também exclusividade operacional: worker Phase C legado/autoresponder não pode estar enviando ao mesmo tempo. O novo SendGuard não controla outro processo/Gateway que tenha autonomia própria. Preflight humano verificará transporte-only e dono único de lifecycle; não faremos reconfiguração agora.

## 11. Trace, privacidade e kill switch

Eventos com: execution/batch refs, sessão opaca/channel, mode/epoch/revision, allowlist decision, origem/candidate ref/hash interno, boundary/enforcement versions e receipt, attempt/idempotency refs, dispatch stage, status/diagnostic, provider ref opaca, latência, historyCommitted/echo dedupe/transition.

Conteúdo/JID/UUID/memórias não aparecem em logs comuns. Journal privado contém o mínimo para recovery (texto autorizado necessário a history) sob ACL local, retenção limitada ao piloto e sem uso como memória. Logs usam refs opacas, não hashes de texto de baixa entropia publicados indiscriminadamente. Stack técnica apenas interna/sanitizada. Relatório experimental completo é artefato separado autorizado.

Métricas separam: accumulate, situação, presença, Graphiti live, geração, cada detector, wall paralelo, regeneração, SendGuard/journal, transport, history commit, total. Falha de provider não é FP/FN semântico.

Kill switch enabled→disabled passa por host, imediato para novos dispatches. Recheck no instante anterior à chamada. Depois do dispatch não prometer recall/cancelamento; inclusive fila remota já entregue ao Gateway pode agir depois. Por isso necessidade de resolver custody/retry do adapter antes de Fase 1.

## 12. Rollout e gates

### Fase 0 — Shadow Live

Após implementação/testes offline aprovados. DM exato Helena; grupos e outros DMs bloqueados também no ingresso do piloto. Graphiti REAL, read-only do novo runtime.

Meta inicial: 20 interações reais, buscando pelo menos 10 gerações elegíveis e cobertura de recall vazio/com boundaries, memória social, silence, transição e mensagem durante execução. Volume não substitui cobertura; não forçar geração ou injetar candidates para preencher cota.

Gate:
- zero send adapter invocations/real outbound originado pelo piloto;
- zero namespace/participant leakage, self executions, duplicates;
- zero hard blockers em WOULD_SEND (Boundary/personal grounding/context fidelity separados);
- history sem falas shadow, frontier/ordenação coerentes;
- outage distinto de empty, unknown/unsupported boundaries abortam;
- modos/kill/replay/journal observáveis;
- revisão de Helena dos candidatos e do custo de silêncio.
Canaries de isolamento preferem dados existentes/fixtures offline sem escrever Graphiti; teste live somente leitura. Não capturar automaticamente conteúdo privado irrelevante para relatório.

Shadow não prova send receipt, echo real ou entrega. Essas lacunas precisam de teste de contrato offline e, quando só observáveis live, canary explicitamente aprovado antes da série de DM.

### Fase 1 — DM Live

Somente nova aprovação humana após Shadow e resolução dos blockers de transport/self/rota. Enabled para UMA sessão, sem grupos. Primeiro uma mensagem controlada supervisionada para confirmar receipt/echo/history, depois 20–30 interações, com limite operacional de sends da campanha no host (não graph) e kill switch disponível.

Gate: zero cross-target, duplicates/self-loop/hard blockers enviados; nenhum indeterminate retry; history append once e transições pós-confirmação; monitorar Blocks/ABORTs/technical failures/context drift/voz/custo/latência e avaliação humana.
Qualquer desvio de destino, duplicação, self-loop, pending ambíguo ou history incerto desabilita/pausa e exige revisão. Sem auto-enable após reconnect.

Grupos são outro design; nada neste sucesso libera grupo ou outras plataformas. Latência atual ~38 s é desagradável, não motivo de tuning nesta fase.

## 13. Testes necessários antes de live

1. Matriz disabled/shadow/enabled × sessão exata/outro DM/grupo/unknown × proveniência válida/falsa.
2. JSON/texto fabricado, mesmo texto/hash, Instruction que devolve “permitir”, bypass de recall, outage→empty, versões não aprovadas, candidate alterada/regenerada/reutilizada.
3. Dois Enviar (sequencial/paralelo), mesma execution replay, mesmo input IDs com novo GUID/rebatch, dois processos, restart, journal corrompido e escrita impossível.
4. Kill switch durante geração, após reserva, enquanto aguarda adapter, imediatamente antes e depois do dispatch.
5. Pre-dispatch rejection vs timeout pós-aceitação, receipt vazio/malformed, partial-send, rota divergente, split e retry remoto.
6. Crash antes de pending, após pending, após aceitação remota, após sent durável, após history append, antes/depois da transição.
7. Echo antes do receipt, depois, duplicado, ID de transcript distinto, missing echo, self PN/LID, unknown actor, owner humano, IDs iguais em contas diferentes.
8. M2 chega/fecha batch durante E1; E2 inclui somente outbounds predecessores confirmados, preservando watermark de inbounds e pin de state/project. CAS e recuperação sem duplicar.
9. Shadow branches nunca append/engajam por fala; erro de send nunca regenera; bypass sem limites correto.
10. Graphiti live namespaces/atores/validade temporal/canaries; empty/outage sem mistura; nenhuma nova escrita.
11. Roundtrip node/enum sem capability serializada; fixture API sem autoridade live; projeto mal configurado rejeitado pelo host.
12. Payload textual único: limite Unicode/formatação conforme adapter, sem prefixo/truncamento/chunking escondido. Testes de segurança de ingress loopback/identidade e acesso à administração.

## 14. Riscos e plano pequeno posterior

Blockers de enabled hoje:
- correlação receipt/echo e todos os IDs de partes não garantidos pelo MCP;
- retry/custódia durável Gateway fora do controle do sender atual;
- rota resolvida novamente dentro de messages_send, sem expected-route binding atômico;
- bridge não transporta fromMe/self alias e ingress loopback não prova identidade de processo;
- novo DAG ainda não ligado ao ingress live; velho worker possui lifecycle próprio;
- history pinned e dedupe em RAM insuficientes para requisitos reais.

Não resolver por relaxamento. Contrato insuficiente mantém enabled bloqueado, mesmo que shadow passe. Não trocar Gateway/API silenciosamente; se não houver superfície já suportada suficiente, pedir autorização para mudança bounded de adapter/metadata, fora deste design aprovado.

Implementação posterior em três incrementos, com checkpoints:
1. Offline: SendGuard/recibo privado/Enviar/journal/terminal outcomes, sem sender real habilitado; testes de invariants/restart. Resolver fronteira history/FSM e live ingress confiável como parte necessária, não novo subsystem semântico.
2. Shadow: ligar bridge confiável ao session runtime + Graphiti live; legado fora desse deployment; traces, gates reais, nenhuma chamada send.
3. Só após aprovação e blockers resolvidos: adapter single-part/receipt/route, canary DM, depois campanha 20–30. Nenhum grupo, retry, model routing, writer Graphiti ou detector novo.

O escopo é maior que adicionar um node: persistência de replay e recorte de history são pré-requisitos de outbound seguro, demonstrados no código. Ainda assim não exige plugin framework, policy DSL, PKI, Assertion ou queue genérica de retries.

## 15. Respostas diretas

**Q1. Fonte canônica/dedupe:** receipt de send + append local, indexado por provider/account/session/messageId. Echo com mesmo ID só confirma observação. Poll/read de transcript não é fonte canônica; ID diferente não é fundido por texto. Falta de correlação é explicitamente preservada.

**Q2. Timeout após possível aceitação:** indeterminate, zero retry e sem append/transition como enviado. Pausa operacional da sessão até reconciliação inequívoca; não usar LLM ou matching de texto para decidir.

**Q3. Outra sessão:** OutboundContext imutável originado pelo host + allowlist exata + recibo bound à mesma execution/session + adapter com rota verificada/fixada. Nenhuma porta to/channel. Project sem manifest autorizado não recebe capability. A lacuna de rota MCP é blocker, não segurança presumida.

**Q4. Crash/restart:** journal durable pending antes de dispatch, input→execution estável/dedupe persistente, ordinal único. Pending recuperado indeterminate e nunca reenviado. Se journal não puder provar estado, disabled; não criar execution nova para replay antigo.

**Q5. Engajada:** depois de provider-accepted e history commit, saída sent, CAS/fencing. Falha/indeterminate/shadow não engajam como consequência de fala. Transições de presença independentes continuam com sua semântica.

**Q6. Shadow:** mesmas verificações/reserva/traces em namespace de tentativa shadow, sender nunca chamado, nenhum append nem transição de fala. Counterfactual separado e sem retroalimentar history. Mudança para enabled não dispara backlog shadow.
