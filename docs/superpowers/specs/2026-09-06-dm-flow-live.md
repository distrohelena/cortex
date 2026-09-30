# Primeiro fluxo real de DM

## Objetivo e limites

Implementar apenas **Mensagem privada → Instrução → Enviar mensagem** usando o editor atual, a ponte inbound existente, LM Studio e o remetente MCP existente. Uma mensagem é uma execução. Sessão é a conversa identificada estruturalmente por canal + conta + ID nativo; não existe porta de sessão.

Não implementar Branch, Acumular, transições de estados, memória, publicação, catálogo novo ou backend de autosave. Manter esses desenhos exportáveis. Não iniciar/parar Gateway, fazer login, enviar mensagens durante os testes ou tocar Graphiti/DREAMS.md. O primeiro envio real pertence ao canário controlado por Helena.

## Dados e portas

Manter os formatos de projeto e grafo versão 1. Conexões ganham `sourcePort` e `targetPort`, IDs técnicos estáveis; rótulos continuam em português.

| Node | Entradas | Saídas |
|---|---|---|
| `dm` | nenhuma | `message`: texto; `from`: ActorRef |
| `prompt` | `message`: texto | `message`: texto |
| `send` | `message`: texto; `to`: ActorRef | nenhuma |

Exemplo: `dm.message → prompt.message`, `prompt.message → send.message`, `dm.from → send.to`. A porta `to` não permite redirecionamento: deve conter o remetente estrutural da própria DM. Rota de envio é exclusivamente a SessionKey observada no evento, nunca texto gerado ou configuração de um node.

Para não perder desenhos: inferir portas `message` em fios legados sem porta quando inequívoco. Demais nodes mantêm seu caráter visual. Não inventar compatibilidade entre texto e Pessoa. Fios novos guardam ambas as portas; uma entrada recebe um fio, saídas podem alimentar mais de um destino. Remover um fio preserva os outros da mesma saída.

## Compilação e execução limitadas

Um projeto ativável tem exatamente um estado, uma entrada `dm`, 1–8 nodes `prompt` e um `send`; nenhum outro tipo, nenhum node solto, ciclo, referência inválida ou porta obrigatória ausente. Até 20 nodes/64 fios, IDs únicos, instruções e conteúdo limitados a 16.000 caracteres. `lmstudio` é o único provedor executável. Modelo é o escolhido pelo node, ou o padrão já configurado. Não carregar modelo por API de gerenciamento.

Validação completa ocorre antes de qualquer geração/envio. Uma execução usa somente o texto da entrada conectada e a instrução do node, sem histórico ou memória implícita. O resultado vazio, falha ou cancelamento impede envio. Só um `messages_send` por execução. Falha de envio incerta nunca é repetida automaticamente.

Usar novos arquivos pequenos em `Filomena.App/FlowRuntime`; não retomar o compilador portátil pausado de `Filomena.Core/Flows`. O executor é independente de MCP: consome `IModelResponseChatClient` e `IConversationSender`.

Interfaces compartilhadas em namespace `Filomena.App.FlowRuntime`:

```csharp
CompiledDmFlow DmFlowCompiler.Compile(JsonElement projectDocument);
// CompiledDmFlow é um snapshot defensivo e não expõe coleções mutáveis.
Task<DmFlowExecutionResult> DmFlowExecutor.ExecuteAsync(
    CompiledDmFlow flow, InboundConversationEvent message,
    bool send, CancellationToken cancellationToken);
// Construtor: DmFlowExecutor(IModelResponseChatClient model, IConversationSender sender)
// Resultado: record DmFlowExecutionResult(string Text, string? OutboundMessageId)
```

O modo `send:false` gera texto, mas nunca chama o remetente. O evento precisa ser DM, ter Sender, timestamp/ID/rota não vazios e identidade coerente com conversa; o remetente precisa corresponder ao peer WhatsApp normalizado da DM. Reusar normalização existente; nunca casar por nome.

## Ativação explícita e host

`--playground` permanece isolado. Somente `--playground --flow-live` registra ponte inbound + consumidor de fluxo + remetente MCP. Não registrar `FilomenaTurnWorker`, `ConversationTurnManager` ou arbiter nesse host. Porta da ponte existente 5147 mantém proteção contra dois consumidores: se outro App ocupa a porta, falhar startup, não competir.

Configuração `FlowLive`: `Channel=whatsapp`, `AccountId=default`, `AllowedDmNativeId` obrigatório e canônico (uma única DM). Ausência ou grupo falha fechado no startup. Não mudar appsettings/credenciais automaticamente.

O host inicia inativo. API local (mesmas verificações Host/Origin e header `X-Filomena-Playground: 1`):

- `GET /api/flow/live`: `{ available, active, target: {channel,accountId,nativeId} | null }`.
- `POST /api/flow/live/activate`: corpo = documento completo de projeto, validado antes da troca atômica do snapshot. Limite 1 MB só nesse endpoint. Retorna status acima. Sem persistência/reativação após restart.
- `POST /api/flow/live/deactivate`: invalida snapshot e cancela execuções ainda em geração; envio já entregue ao transporte não pode ser desfeito.

Frontend mostra **Ativar na DM / Desativar** e alvo apenas quando `available:true`. Ativar envia snapshot do desenho; editar não modifica o snapshot ativo. Alteração no desenho deve mostrar que precisa reaplicar, não afirmar que o ativo acompanha o rascunho. Nenhuma chamada de ativação automática em reload.

Consumidor descarta eventos sem plano ativo, de grupos, outra conta/canal/DM ou sem identidade. Deduplicação por `(channel,accountId,nativeConversationId,providerMessageId)` no processo, antes de geração. Nunca agregar mensagens. Execuções têm dados locais; concorrência limitada e sem mistura. Não repetir eventos processados após troca do desenho; restart não promete exactly-once durável. Não reenviar histórico de MCP nem reprocessar fila anterior à ativação: capturar snapshot na admissão.

## Aceitação

1. Round-trip das portas e node enviar no projeto/reload, preservando fios antigos.
2. Tipos incompatíveis e conectores desconhecidos rejeitados no editor e compilador.
3. DM autorizada passa seu texto à instrução e resultado ao mesmo destinatário/rota.
4. Provider/modelo por node preservados; modelo vazio herda padrão.
5. Grupo, outra DM/conta, sender ausente/divergente e plano inativo: zero chamadas ao modelo e ao envio.
6. Duplicata inbound: uma geração e um envio; duas mensagens: duas execuções sem misturar conteúdo.
7. Falhas de modelo, texto vazio, cancelamento e envio incerto não produzem replay.
8. Ativação inválida não substitui plano válido; desativação bloqueia novos envios.
9. Host playground normal não cria MCP/ponte/worker real.
10. Testes offline usam provider/sender simulados; nenhum envio real ou alteração Graphiti/Gateway.

## Operação

Helena encerra apenas a instância antiga de Filomena.App, se estiver usando a porta 5147, configura a DM e inicia o novo modo. Gateway permanece sob seu controle. Depois monta o caminho, ativa e envia uma mensagem na DM. Sem Gateway pronto, documentar a etapa pendente sem alegar teste live concluído.
