# Primeiro flow real: DM → Instrução → Enviar mensagem

## Limite desta entrega

Uma mensagem recebida inicia uma execução independente. A sessão é a conversa observada (canal + conta + ID nativo), não um ID escolhido pelo modelo. Só uma DM WhatsApp configurada pode executar. Grupos e outros contatos não executam.

O playground comum continua isolado. O modo real inicia **desativado** e exige um clique explícito para ativar um snapshot validado. Alterar o desenho não altera esse snapshot até reaplicar. Reiniciar a App perde a ativação.

Acumular, Branch e transições por Saída ainda são desenhos: não podem fazer parte do projeto ativado neste recorte. Não há nova memória, scheduler ou alteração no Graphiti.

## Iniciar por sua conta

O Gateway é controlado por Helena. Estes comandos não iniciam o Gateway. Não rode simultaneamente a App antiga e este modo: ambos usam a mesma porta do bridge inbound.

Em `C:\dev\filomena`:

```powershell
$env:FlowLive__AllowedDmNativeId = '5511944558218@s.whatsapp.net'
dotnet run --project src/Filomena.App/Filomena.App.csproj -- --playground --flow-live --live-reload
```

`FlowLive__Channel` é `whatsapp` e `FlowLive__AccountId` é `default` por padrão. O alvo deve ser um ID canônico, não nome de exibição. A configuração permanece opcional e nenhum arquivo de configuração da conta foi alterado.

Abra `http://127.0.0.1:18793/flow`.

## Desenhar e ativar

Use um único estado com:

- DM recebida;
- uma Instrução (ou uma sequência de até oito);
- Enviar mensagem.

Conecte:

| Origem | Destino |
| --- | --- |
| DM / Mensagem | Instrução / Mensagem |
| Instrução / Mensagem | Enviar mensagem / Mensagem |
| DM / Remetente | Enviar mensagem / Para |

Remova a antiga Saída e quaisquer blocos soltos do projeto de teste. Escolha o modelo no catálogo existente, escreva a instrução e ative o fluxo. O modelo recebe apenas a mensagem anterior e a instrução do node; nenhum arbiter ou prompt de personalidade oculto é adicionado.

Envie uma mensagem da DM autorizada. O bridge existente entrega o evento, o executor chama o modelo e o MCP persistente envia pela **session route original**. A resposta do modelo não pode escolher outro chat.

Clique em Desativar para cancelar gerações ainda em andamento. Um envio já entregue ao transporte não pode ser desfeito.

## Segurança e limitações conhecidas

- Endpoints do editor são loopback, com validação de Host/Origin/header. O transporte usa o bridge e as credenciais locais existentes; nenhum login novo.
- O bridge continua confiando no produtor local OpenClaw: a rota vem do mesmo hook `message_received` que a identidade normalizada. Não é um endpoint autenticado para produtores externos; não exponha sua porta na rede. A consistência dos campos não é prova criptográfica da origem. Não interpretamos a `sessionKey` como identidade nem reconstruímos rotas por telefone.
- Modelo e envio com falha não são repetidos automaticamente. Envio com resultado incerto não é reenviado.
- Deduplicação é local ao processo. Guarda até 10.000 IDs; ao atingir o limite, recusa novas execuções em vez de esquecer IDs e arriscar replay. Não oferece exatamente-uma-vez após restart.
- Até quatro gerações simultâneas, no máximo 64 execuções admitidas em andamento. Não agrega mensagens e não garante ordem de conclusão.
- Não ativa projetos antigos silenciosamente e não salva ativação em disco.
- O canário real depende do Gateway, bridge e LM Studio estarem disponíveis. A validação automatizada usa modelo/remetente falsos; não comprova envio WhatsApp real.

Spec: `docs/superpowers/specs/2026-09-06-dm-flow-live.md`.
Plano e resultados: `docs/superpowers/plans/2026-09-06-dm-flow-live.md`.
