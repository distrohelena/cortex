# Temporal local em Docker

Ambiente instalado nesta sessao:

| Configuracao | Valor |
| --- | --- |
| Compose | `runtime/docker-compose.temporal.yml`, projeto `cortex-temporal` |
| Imagem | Temporal CLI 1.9.1 / Server 1.32.0 / UI 2.54.1, digest fixado no Compose |
| Endpoint SDK | `127.0.0.1:7233` |
| UI | `http://127.0.0.1:8233` |
| Namespace | `cortex-local` |
| InstallationId | `cortex-local` |
| Workflow queue | `cortex-local-workflows` |
| Effect queue | `cortex-local-effects` |
| Banco Temporal | Volume Docker `cortex-temporal_temporal-data`, `/var/lib/temporal/temporal.sqlite` |
| Store privado do host | `C:/dev/cortex/state/temporal/artifacts`, ACL exclusiva do usuario do host |
| Playground | `http://127.0.0.1:18793/flow` |

A [imagem oficial](https://github.com/temporalio/cli#run-via-docker) fornece servidor
local e UI. `--db-filename` conserva os dados do servidor no volume; veja a
[documentacao oficial de persistencia](https://github.com/temporalio/documentation/blob/main/docs/cli/setup-cli.mdx).
Este e o ambiente local de desenvolvimento do Cortex. Workflows/Activities do
Filo executam no host Windows, que possui o store privado e os bindings. O servidor
Docker recebe somente controle e referencias. Containerizar tambem esses workers
exigiria montar o mesmo store e transferir a propriedade dos bindings/transporte.

## Operacao

```powershell
docker compose -f runtime/docker-compose.temporal.yml up -d --wait
./tools/start-temporal-playground.ps1
```

O launcher configura o backend Temporal e mostra o codigo de login local no
console. Nao grave esse codigo em arquivos de log. Abra `/flow`, entre com o codigo,
adicione `humanFeedback` e configure pergunta/timeout. Reiniciar o Playground
exige outro login; pedidos duraveis continuam no servidor e no store.
Somente um processo pode ser dono desse store. Ctrl+C fecha o host sem cancelar
workflows; cancelamento de execucao e uma acao explicita do Playground.

```powershell
docker compose -f runtime/docker-compose.temporal.yml ps
docker compose -f runtime/docker-compose.temporal.yml restart temporal
docker compose -f runtime/docker-compose.temporal.yml stop
```

O healthcheck exige servidor saudavel e RPC de workflow no namespace configurado.
O init do volume concede a propriedade ao usuario `temporal` e permissao 0700.
As portas ficam somente em loopback. Preserve o volume e o store do host em
backups consistentes; nao use `down -v` para parar o ambiente.

## Verificacao executada

Dois testes explicitos em `DockerRuntimeSmokeTests` validam o servidor instalado:
primeiro executa feedback autenticado com efeitos fake nas queues configuradas,
depois o container e reiniciado e o resultado fechado e lido sem nenhum worker.
O arquivo do recibo sintetico fica em area local ignorada pelo git. Esses testes
possuem `Category=LocalSocket`; a suite offline normal os exclui, e nesta sessao
ambos foram executados separadamente e passaram. Nenhum modelo ou transporte
real foi usado. A UI respondeu HTTP 200; QA visual continua indisponivel.

A procura de origem examinou os workspaces Cortex/Filo, inclusive `.local-runtime`,
e processos native-live/native-shadow. Nao foi identificado checkpoint live nem
processo nativo ativo nesses locais. Novas sessoes do launcher usam Temporal;
uma origem antiga descoberta depois segue [o runbook de cutover](temporal-cutover.md).
