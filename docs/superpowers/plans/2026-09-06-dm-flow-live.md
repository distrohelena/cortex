# DM real pelo editor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Conectar uma DM autorizada a Instrução e Enviar mensagem, sem implementar o restante do motor.

**Architecture:** Portas identificadas no editor, compilador/executor limitado em C#, ponte inbound e remetente MCP reutilizados. Ativação explícita de snapshot somente no host opt-in; nenhuma FSM em execução.

**Tech Stack:** .NET SDK 10.0.400, C# / net10.0, JavaScript ESM, xUnit v3 e node:test, dependências já instaladas.

**Spec:** `docs/superpowers/specs/2026-09-06-dm-flow-live.md`

## Global Constraints

- Somente WhatsApp DM autorizada; nenhuma resposta em grupo.
- Uma mensagem = uma execução. Sessão = canal + conta + conversa nativa.
- Não iniciar/parar Gateway, fazer login, enviar mensagens reais, alterar Graphiti ou `workspace/DREAMS.md`.
- Sem APIs de carregamento de modelos; reusar Gemma configurável e MCP persistente.
- Não implementar Acumular, Branch, transições de estado ou autosave.
- Sem push. Trabalhar na main já autorizada, preservar mudanças anteriores; não fazer commits misturando a base suja.
- Delegados `gpt-5.6-luna`, esforço `high`, sem delegação recursiva.

## Task 1: Executor limitado de portas (Luna)

**Files:** criar `src/Filomena.App/FlowRuntime/DmFlowCompiler.cs`, `DmFlowExecutor.cs`; testar `tests/Filomena.Llm.Tests/DmFlowExecutorTests.cs`.

**Interfaces:** produzir exatamente as interfaces C# da spec. Consumir modelos normalizados existentes e interfaces LLM/remetente. Nada de hosting/configuração nesta tarefa.

- [ ] Criar teste RED com projeto literal: dm.message→p.message, p.message→send.message, dm.from→send.to. Provider falso retorna `Resposta`; assert texto, rota e receipt retornados. Acrescentar cancelamento, modo simulado e rejeição de destinos divergentes.
- [ ] Executar `dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj -c Release --no-restore --filter DmFlowExecutorTests` e registrar falhas esperadas.
- [ ] Implementar parsing estrito do envelope `filomena-local-project`, estado único, validação de tipo/porta/DAG e snapshot defensivo. Recusar nodes visuais antes da primeira inferência. Executar texto em ordem de dependências, manter ActorRef como valor separado, verificar destinatário antes de chamar `IConversationSender.SendTextAsync(message.SessionKey, text, token)`.
- [ ] Testar: falta de porta, Person→texto, fios duplicados, ciclos, referências inválidas, provider não suportado, grupos, sender ausente, duas execuções concorrentes, modelo por node, geração vazia/falha, send falha sem retry.
- [ ] Rodar teste focado, autorrevisar e reportar RED/GREEN e arquivos; não commit.

## Task 2: Portas e Enviar mensagem no editor (Luna)

**Files:** somente `src/Filomena.App/Playground/flow-model.mjs`, `flow.js`, `flow.html`, `flow.css`, `tests/frontend/flow-*.test.mjs` relacionados.

**Interfaces:** fios `{source,target,sourcePort,targetPort}`; portas da spec. HTTP de ativação/status conforme spec. IDs `send` para node; mensagens visíveis em português.

- [ ] Escrever RED para `dm.from→send.to` coexistindo com `dm.message→prompt.message`; trocar fio de uma porta não apaga outra; rejeitar Person→message; export/reload preservam portas e estado/Branch/Acumular.
- [ ] Executar `node --test tests/frontend/flow-model.test.mjs tests/frontend/flow-project.test.mjs`.
- [ ] Implementar catálogo mínimo de portas no módulo atual. `connect(graph,source,target,sourcePort?,targetPort?)` aceita chamadas legadas inequívocas; uma entrada = um fio, saída = vários. `pending` identifica node + porta; renderizar fios pelas portas exatas, não primeiro `.out/.in`. `send` é terminal no desenho. Manter Person/Channel de nodes não executáveis como desenho, sem inventar valores.
- [ ] Adicionar Enviar mensagem ao menu (categoria Controle de fluxo é suficiente), entradas Mensagem/Para. Não substituir nem executar Saída/Exit. Manter cenas anteriores e prompts do usuário.
- [ ] Mostrar controle de ativação somente quando `GET /api/flow/live` retorna `available:true`. Ativar POST do snapshot `JSON.parse(saveProject(project))`; Desativar POST `{}`. Mostrar alvo e estado ativo/rascunho alterado. Falha API nunca pode parecer sucesso. Sem autosave nem autoativação.
- [ ] Rodar frontend completo + `node --check flow.js`, autorrevisar e reportar; não editar C# nem commit.

## Task 3: Host opt-in, admissão e integração (controlador)

**Files:** criar `FlowRuntime/DmFlowLiveOptions.cs`, `DmFlowLiveSession.cs`, `DmFlowLiveWorker.cs`; modificar `LocalPromptPlayground.cs`, ramo playground de `Program.cs`; testes `DmFlowLiveTests.cs` e documento operacional.

**Interfaces:** consumir Task 1 e Task 2. Reusar `OpenClawInboundBridgeReceiver`, `InboundBridgeHttpServer`, `OpenClawMcpSender`.

- [ ] Escrever testes RED para gate de DM/conta, inactive, duplicados, substituição inválida e desativação durante geração.
- [ ] Implementar configuração validada e snapshot cancelável em `DmFlowLiveSession`. `Activate(JsonElement)`, `Deactivate()`, `ObserveAsync(InboundConversationEvent, CancellationToken)`; admissão captura snapshot antes de aguardar execução; dedup no processo permanece após reaplicar. Rota nunca vem da resposta do modelo.
- [ ] Criar BackgroundService lendo receiver uma vez; não registrar arbiter/turn manager/worker antigo. Limitar trabalho em voo e observar exceções sem logar conteúdo sensível. Reusar sender persistente; warmup read-only opcional somente no modo live.
- [ ] Expor endpoints somente loopback mantendo Host/Origin/header guard. Resposta padrão `available:false` no playground isolado. Parametrizar Build para testes com modelo/remetente simulados sem MCP.
- [ ] Adicionar flag `--flow-live` ao ramo `--playground`; configurar via ambiente `FlowLive__AllowedDmNativeId`. Ativação nunca ocorre no startup; falta de alvo impede modo live.
- [ ] Executar suíte LLM/App, node:test completo, check JS, `git diff --check`. Testes de transporte/Core existentes podem ser executados se não atingirem suites já quebradas fora do recorte; reportar falhas anteriores sem ampliar escopo.

## Task 4: Revisão e entrega

- [ ] Revisor Luna high verifica escopos das Tasks 1–3, risco de envio duplicado/destino errado, opt-in real, alterações de portas e segurança loopback. Revisão comum de tarefa, não revisão independente especial.
- [ ] Encaminhar achados aos respectivos donos; corrigir/testar; repetir revisão focada se necessário.
- [ ] Registrar resultados e instruções para Helena iniciar App, ativar desenho e conduzir canário. Nenhum canário real sem ação de Helena.

## Registro de execução

Plano criado antes da implementação. Base suja preservada, main conforme pedido anterior. Tasks 1 e 2 têm conjuntos de escrita disjuntos; controlador cuida da Task 3. Constantes de UUID e namespaces não serão alteradas.

### Integração e verificações do controlador

- Task 1: compiler/executor entregues; relatório em `2026-09-06-dm-flow-live-task1-report.md`. Testes adicionais de falha/concorrência solicitados antes da entrega final.
- Task 3: host opt-in, API local, alvo único, cancelamento, dedup e consumidor implementados. Integração bridge → worker → modelo/remetente simulados executada sem serviços reais.
- Ajuste pequeno adicional no receiver: callback genérico opcional antes da fila. Somente o modo flow aplica uma versão de ativação efêmera, impedindo que mensagens enfileiradas inativas sejam executadas depois de ativar/reaplicar. Nenhuma mudança no evento do provider, UUID, memória ou comportamento default do receiver. Teste RED de API ausente e GREEN executados.
- Testes de HTTP também verificam projeto inválido → 400 mantendo plano anterior; header ausente → 403; playground isolado → indisponível.
- `dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj -c Release --no-restore`: 77 passaram na primeira rodada com o ajuste de fila; ver resultado final abaixo após fechamento dos testes adicionais.
- `node --test tests/frontend/*.test.mjs`: 35 passaram na rodada intermediária.
- `node --check src/Filomena.App/Playground/flow.js`: exit 0.
- `git diff --check`: exit 0; somente avisos de conversão LF/CRLF da base.
- Suíte ampla Core: 138 passaram, 19 falharam nas pendências do compilador/binding anterior (`Filomena.Core.Tests.Flows`), não alterado neste recorte. Não mascarar nem excluir esses testes.
- Suíte ampla OpenClaw: não compila pelos testes anteriores `FrontendBuildTests.cs`/`SettingsStoreTests.cs`, que referenciam `Filomena.App.Frontend`, `Filomena.App.Settings` e tipos ausentes. Nenhum filtro de build para esconder isso.
- Nenhum Gateway iniciado/parado, inferência real, envio real, push ou alteração em Graphiti/DREAMS.
- Procedimento operacional: `docs/dm-flow-canary.md`. Canário real e verificação visual permanecem com Helena; browser de teste indisponível nesta sessão.

### Fechamento do recorte

Tasks 1–3 implementadas. Revisão comum Luna high de backend concluída: sem achados concretos restantes depois das correções. A revisão adicionou bloqueio de JID de grupo rotulado como DM e deduplicação do bridge por canal/conta/ID nativo, mantendo todos os namespaces e UUIDs. A hipótese de rota divergente foi retirada após inspeção do produtor: SessionKey e mensagem vêm do mesmo hook; permanece documentada a confiança no produtor loopback, não uma promessa de autenticação.

Verificação final executada:

| Comando | Resultado |
| --- | --- |
| `dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj -c Release --no-restore` | 92 passaram, zero falhas; inclui novos testes de fluxo e integração App |
| `node --test tests/frontend/*.test.mjs` | 37 passaram, zero falhas |
| `node --check src/Filomena.App/Playground/flow.js` | exit 0 |
| `git diff --check` | exit 0 com configuração Git normal; avisos LF/CRLF |

Cobertura adicional: compilação inválida antes de inferência, grupos rotulados indevidamente, provider inválido, modelo vazio/falha, envio incerto sem retry, duas execuções concorrentes isoladas, cancelamento e modo sem envio. O bridge tem teste com mesmo provider ID em contas diferentes. Composição do modo real inspecionada em teste sem instanciar MCP, confirmando ausência do worker/arbiter antigo.

As falhas preexistentes das suítes Core/OpenClaw descritas acima permanecem: não declarar a solução inteira verde. Não houve commit misturando alterações anteriores, push ou teste live.

Revisão frontend Luna high: corrigido o único achado de preservação visual — fios antigos ambíguos agora aparecem tracejados entre os blocos, removíveis, sem inventar portas tipadas. Ainda exigem reconexão para execução. Controlador corrigiu também ativação com resposta HTTP perdida: status é reconciliado por GET, incerteza fica visível e Desativar é idempotente mesmo se a UI acreditar que está inativa. Testes estruturais RED (2 falhas) e GREEN executados; inspeção visual em browser não foi possível.
