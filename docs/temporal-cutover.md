# Cutover ocioso para Temporal

O codigo esta preparado para migrar contexto local ocioso. O corte operacional
nao foi executado para sessoes antigas nesta sessao: nenhum processo nativo dono
ou checkpoint live foi identificado nos workspaces verificados. O destino local
foi instalado em Docker (endpoint `127.0.0.1:7233`, namespace `cortex-local`, queues
`cortex-local-workflows` e `cortex-local-effects`); veja [a configuracao](temporal-docker.md).
Os testes de importacao usam somente checkpoints sinteticos.
Nao ha troca automatica de dono, replay de mensagens nem rearm de envio.

## Preparacao concreta

1. Identifique processo/PID dono, installationId, SessionKey, projeto/versao/revisao,
   diretorio privado da origem, epoch/cursor da fonte e rota de transporte.
   Identifique destino: endpoint, namespace, fila de workflows, fila exclusiva de
   efeitos, diretorio privado de artifacts e operador. Nao derive estes valores
   de notas antigas. O destino precisa usar o mesmo escopo de instalacao/sessao.
2. Pause novas admissoes no dono atual. Drene os buffers e execucoes; pedidos
   humanos pendentes precisam ser resolvidos ou cancelados antes da captura.
   Reconcilie inbox observado/admitido/quarentenado e outbound pending,
   indeterminate ou sent ainda sem history_committed. Nunca mande de novo para
   descobrir se um envio anterior foi entregue.
3. Grave o checkpoint local por seu fluxo normal de shutdown e pare o processo.
   Preserve backup privado consistente de `observation-live-context.json`,
   `observation-live-inbound.jsonl`, `observation-live-outbound.jsonl` e projeto.
   Nao edite o checkpoint para fazer a validacao passar. O comando recusa PID
   vivo (inclusive reutilizado) e mantem leases de leitura que negam escritor.

## Arquivo privado de configuracao

Serialize `TemporalCutoverConfiguration` do Filo com estas propriedades (nomes
PascalCase, enums numericos conforme `SessionEntryKind`):

| Campo | Origem |
| --- | --- |
| Temporal | Objeto `TemporalRuntimeOptions`: InstallationId, TargetHost, Namespace, WorkflowTaskQueue, EffectTaskQueue, ArtifactDirectory |
| OperatorRef | Identidade confiavel do operador do destino |
| SourceDirectory | Diretorio absoluto do checkpoint local, separado do store de destino |
| SourceOwnerProcessName / SourceOwnerPid | Nome/PID confirmado do dono ja parado |
| Session | RuntimeId, CharacterId, ChannelId, AccountId, NativeConversationId exatos |
| ConversationNamespace / SelfActorIds / PeerActorIds / EntryKind | Binding de autoridade do host original |
| AllowsNativeGroupParticipants | Flag exata do host original para participantes nativos de grupo (padrao false) |
| Project | SessionFlowProject capturado: ProjectId, ProjectVersion, ProjectRevision, Document |
| SourceEpoch / SourceCursor | Linhagem e cursor atual conferidos da fonte |

O arquivo contem dados privados do host e do projeto: mantenha ACL privada e
fora do git. O checkpoint e os journals sao lidos sem alterar a origem. Checksums,
binding, sequencia/transicoes dos journals e escopo sao validados com os mesmos
validadores do runtime. Shadow sem checkpoint local completo precisa de captura
explicita pelo host; este comando suporta o checkpoint live V1 existente.

## Dry-run e autorizacao

```powershell
dotnet run --project C:/dev/filo/src/Filomena.App -- --temporal-cutover --config C:/private/cutover.json --dry-run
```

Dry-run e o padrao se nenhuma opcao de execucao for fornecida. Ele captura
manifest/history/dedupe/projeto no store privado de destino, mas nao conecta ao
Temporal, cria workflows, executa modelos ou envia mensagens. A saida tem somente
CutoverId, fingerprint SHA-256, estado/watermark/epoch/cursor e destino concreto.
Uma origem alterada gera outra identidade; a aprovacao anterior nao vale para ela.

Apresente esses valores e o backup ao operador para autorizacao do corte real.
Somente depois dessa autorizacao execute, usando o CutoverId conferido:

```powershell
dotnet run --project C:/dev/filo/src/Filomena.App -- --temporal-cutover --config C:/private/cutover.json --execute --approved-cutover-id <CutoverId-do-dry-run>
```

A importacao chama `ImportIdleAsync`; nao chama Receive para mensagens historicas.
O destino vazio e hidratado com estado, enteredAt, watermark, historico retido e
todos os IDs de dedupe. Repetir o mesmo manifest nao cria trabalho. Estado divergente
ou fingerprint alterado e rejeitado. O marcador privado `target-import-verified`
so aparece apos comparar o snapshot importado com o original. Um marcador privado
de ownership vincula o manifest a instalacao/sessao/revisao. Na primeira ativacao,
o host confere novamente o fingerprint; inicia o worker de workflows antes de consultar o pai
e exige o estado importado no servidor configurado. A ativacao e registrada para
permitir reinicios posteriores sem reinterpretar o checkpoint local antigo.
A fila de efeitos fica suspensa ate conferir o contexto do servidor e o inbox;
queries/replay do controle continuam disponiveis durante essa validacao.
Falha/timeout exige
consultar/repetir a mesma identidade, mantendo a origem parada.

## Troca de dono e verificacao

1. Mantenha a origem parada depois da importacao. Confira no destino snapshot,
   cursors, dedupe, ausencia de filho ativo e de envio/execucao novo.
2. Configure o host de destino com o mesmo store, endpoint, namespace e queues;
   registre os bindings reais antes de iniciar os workers. O comando de importacao
   fecha seus workers e libera o store antes de iniciar o host normal.
3. Registre a troca de dono no controle operacional da instalacao e conserve o
   marcador privado. Garanta um unico dono de admissao, journals e transporte.
   Retome a fonte no cursor conferido, sem autoreplay do backlog antigo. Um live
   window ainda exige seu novo preflight e autorizacao de transporte existentes.
4. Confira a primeira admissao nova, watermark, history e recibos; a repeticao de
   um provider ID antigo deve ser Duplicate. Preserve backup e evidencia da troca.

## Rollback

Antes de qualquer progresso do destino, `ValidateRollback` exige o snapshot
exatamente igual ao capturado; pare o destino antes de restaurar o dono antigo.
Depois de nova admissao, efeito ou recibo, rollback exige reconciliacao e decisao
nova de dono. Nao restaure um backup antigo enquanto o destino continua ativo.
O comando nao faz rollback automatico, troca de dono ou ativacao de transporte.
