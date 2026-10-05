# Runtime Temporal

O Core conserva a semantica do grafo; `Cortex.Temporal` usa o SDK .NET 1.20.0.
O servidor Temporal guarda controle e referencias. Projetos, conversa, prompts,
perguntas, respostas humanas, respostas do modelo e evidencia ficam no store privado
da instalacao. O store exige diretorio dedicado, identidade da instalacao e um
unico processo escritor. No Windows a DACL concede acesso ao usuario do host;
no Unix o diretorio usa 0700 e os arquivos 0600. Preserve o store e os journals
no mesmo backup consistente: o historico Temporal sozinho nao restaura os dados.

## Configuracao Filo

`SessionBackend=temporal` exige `Temporal:InstallationId`, `TargetHost`, `Namespace`,
`WorkflowTaskQueue`, `EffectTaskQueue` e `ArtifactDirectory`. As filas devem ser
diferentes e exclusivas do dono da instalacao. `OperatorRef` identifica o operador
confiavel do host. Para comandos de observacao, forneca o mesmo objeto `Temporal`
no arquivo de configuracao; o teto shadow/live e o preflight existentes continuam
obrigatorios. Nenhum binding fixture autoriza inferencia ou envio em producao.

O Playground troca um nonce gerado pelo launcher por cookie HttpOnly/SameSite
Strict em loopback; mutacoes exigem a origem exata. Reiniciar o host exige novo
login. A resposta humana recebe um atestado privado do operador do servidor.
O body HTTP nao concede identidade nem autoridade. Configuracao do node:
`humanFeedback`, `settings.question`, `settings.timeoutSeconds` opcional positivo;
entrada `context` opcional texto, saida `feedback` texto, exits `then` e `timeout`.

## Espera e recuperacao

Feedback sem timeout espera duravelmente, sem thread ou Task do host retida.
Parar cliente ou worker conserva o pedido. Cancelar explicitamente a execucao
fecha o pedido sem executar `then`. Timers e Updates sao registrados no Temporal;
primeira resolucao valida vence. Retentativas usam a mesma operationId e resposta.

Passos puros, artifacts e preparacao de feedback usam ate cinco tentativas com
backoff de 1 a 30 segundos. Inferencia, memoria, boundary e envio usam uma tentativa.
O resultado privado de cada passo inclui snapshot e traces em um checkpoint
atomico antes de publicar os indices. Recuperacao reutiliza esse checkpoint.
Inferencia com intent mas sem resultado duravel falha explicitamente, sem gerar
novamente. Reserva/dispatch sem recibo comprovado exige reconciliacao; nunca
reenviar automaticamente. Recibo confirmado permite commit idempotente de fala e
transicao. Autoridade de envio e contexto de review sao revalidados no sink.

O pai serializa filhos por sessao, preserva dedupe, watermark e buffers na rotacao.
O backlog padrao e 256; um buffer aberto reserva sua vaga. Historico retido limita
10.000 entradas e 1.000.000 caracteres; paginas congelam o conjunto privado para
evitar mudancas entre leituras. Retencao/limpeza fisica dos artifacts ainda deve
preservar referencias de workflows e backups: nao apague arquivos por idade.

## Verificacao

`dotnet test tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj` executa
servidor local persistente entre workers e time skipping para timers, sem provider
real. O binario de teste do Temporal precisa estar disponivel (o SDK pode baixa-lo).
`TemporalAcceptanceTests` fecha e reabre o store, preserva ExecutionId/revisao,
verifica prompt -> feedback -> prompt -> exit (1 chamada de cada, 1 transicao),
inspeciona payloads base64 e faz replay de pai e filho. `ReplayCompatibilityTests`
reproduz o golden sintetico V1 de feedback em `Fixtures/flow-feedback-v1.json`.
Mudancas de comandos do Workflow precisam manter esse replay; use versionamento
do SDK para evolucoes incompatíveis. Nunca substitua o golden por dados reais.

As suites Core cobrem intent sem output, checkpoint sem snapshot, journals
reservados/indeterminados/confirmados, commit perdido, ACL e dedupe. As suites
Filo cobrem HTTP/autenticacao, reconexao e host observado com completion fake.
A verificacao visual do Playground nao foi possivel nesta sessao: nao havia
navegador conectado. Os testes de DOM, frontend e HTTP foram executados.

Cutover de sessoes existentes segue [temporal-cutover.md](temporal-cutover.md).
O ambiente Docker local e o launcher configurado estao em [temporal-docker.md](temporal-docker.md).
