# Simulação de sessões no playground

Esta API usa **somente respostas fake fornecidas no pedido**. Não envia mensagens, não chama LM Studio e não acessa Graphiti/OpenClaw. Ela é servida pelo mesmo host local do editor.

O botão de execução antigo continua sendo o teste de uma execução do slice 1. Para testar lotes e estado neste recorte, use a API abaixo. Importar o fixture no editor permite inspecionar/configurar os nodes; salvar o projeto não inicia uma sessão.

## Criar uma simulação sem substituir o projeto salvo

Na raiz do repositório, com o playground atualizado aberto:

```powershell
$sliceBase = 'http://127.0.0.1:18793'
$sliceHeaders = @{ 'X-Filomena-Playground' = '1' }
$sliceFixture = Get-Content -Raw -Encoding utf8 tests/fixtures/typed-flow-slice2.json | ConvertFrom-Json
$sliceStart = @{
    project = $sliceFixture
    responses = @{
        ai = @(
            '{"acao":"responder","proximo_estado":"engajada","motivo":"fixture"}'
        )
    }
} | ConvertTo-Json -Depth 100
$sliceRuntime = Invoke-RestMethod "$sliceBase/api/flow/typed/sessions" -Method Post -Headers $sliceHeaders -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($sliceStart))
$sliceId = $sliceRuntime.runtimeId
```

A fila `ai` fornece uma resposta por chamada desse node, em ordem. Configure mais respostas para executar mais lotes; fila esgotada é falha explícita, nunca chamada a um modelo real.

## Receber três mensagens de grupo

O fixture fecha com três mensagens ou com seu primeiro prazo habilitado. Para testar a quantidade, envie as três rapidamente:

```powershell
$sliceRoute = @{ channel='whatsapp'; accountId='fixture'; nativeId='5511981042361-1589823314@g.us'; kind='group' }
foreach ($sliceNumber in 1..3) {
    $sliceEvent = @{
        session = $sliceRoute
        message = @{
            messageId = "local-test-$sliceNumber"
            timestamp = [DateTimeOffset]::UtcNow.ToString('O')
            direction = 'inbound'
            value = @{
                texto = "Mensagem fake $sliceNumber"
                remetente = @{ nome='Pessoa de teste'; stableId='fixture:shared-actor' }
                canal = @{ nome='Drift (simulação)'; nativeId=$sliceRoute.nativeId }
                reply = $null
            }
        }
    } | ConvertTo-Json -Depth 20
    Invoke-RestMethod "$sliceBase/api/flow/typed/sessions/$sliceId/receive" -Method Post -Headers $sliceHeaders -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($sliceEvent))
}
$sliceDrain = @{session=$sliceRoute} | ConvertTo-Json
Invoke-RestMethod "$sliceBase/api/flow/typed/sessions/$sliceId/drain" -Method Post -Headers $sliceHeaders -ContentType 'application/json' -Body $sliceDrain
Invoke-RestMethod "$sliceBase/api/flow/typed/sessions/$sliceId" | ConvertTo-Json -Depth 20
```

O resultado esperado é uma execução do lote e estado `engajada` para essa sessão. Reentregar o mesmo `messageId` retorna `duplicate=true`, sem reaproveitar a mensagem em outro lote.

`drain` aguarda somente as continuações já enfileiradas. Não força liberação de buffers abertos nem avança relógios. Em teste de silêncio, consulte o status após o prazo; não confunda `drained=true` com “todos os timers venceram”. A espera HTTP tem timeout de 10 segundos e não cancela o runtime se apenas a consulta expirar.

Use `kind='dm'` e outro `nativeId` para simular uma DM. Trocar somente o nome não muda identidade; trocar conta/canal/conversa produz outra sessão operacional. Nenhuma dessas rotas é enviada ao provider.

## Validar e publicar nova versão administrativa

- `POST /api/flow/typed/project/validate`, body `{ "project": ... }`: valida todo o projeto sem executar.
- `POST /api/flow/typed/sessions/{id}/project`, mesmo body: publica uma nova versão somente para futuros lotes. Não altera o JSON salvo no disco.
- `POST /api/flow/project`: salva somente o desenho. Não publica automaticamente uma versão no runtime fake existente.

O status omite mensagens, outputs completos e prompts. Mostra somente sessões, execuções e trace estrutural. Os resultados tipados completos permanecem acessíveis ao test harness C#, não são despejados no status HTTP.

## Encerrar

```powershell
Invoke-RestMethod "$sliceBase/api/flow/typed/sessions/$sliceId/stop" -Method Post -Headers $sliceHeaders -ContentType 'application/json' -Body '{}'
```

Parar uma simulação elimina apenas seu estado efêmero. Reiniciar o host também descarta buffers/estado/runtime; um novo runtime começa no `initialStateId` do projeto. O desenho salvo permanece intacto. Até oito simulações podem ficar abertas por host; encerre as antigas ao terminar.
