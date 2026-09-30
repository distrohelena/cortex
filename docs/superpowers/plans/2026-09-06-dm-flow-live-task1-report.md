# Task 1 — Executor limitado de portas

Status: implementação concluída no recorte próprio; sem commit ou staging.

Arquivos próprios:

- `src/Filomena.App/FlowRuntime/DmFlowCompiler.cs`
- `src/Filomena.App/FlowRuntime/DmFlowExecutor.cs`
- `tests/Filomena.Llm.Tests/DmFlowExecutorTests.cs`

Implementado:

- envelope `filomena-local-project`, projeto v1 com um único estado e snapshot defensivo;
- somente `dm`, 1–8 `prompt` e `send`, com DAG conectado, portas tipadas, inferência legada inequívoca, limites e rejeição de referências/fios inválidos;
- provedor executável `lmstudio`, modelo por instrução ou padrão do cliente;
- validação completa do evento DM WhatsApp e do peer normalizado antes da primeira inferência;
- execução sequencial sem histórico, dados locais por chamada, `send:false` sem remetente e envio único pela `SessionKey` observada;
- respostas vazias/oversized, falhas, cancelamento e receipt inválido não iniciam/repetem envio;
- rejeição determinística de envelopes/nodes escalares para evitar `InvalidOperationException`/HTTP 500.

Testes:

- RED: `dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj -c Release --no-restore --filter DmFlowExecutorTests` falhou inicialmente porque `FlowRuntime` ainda não existia (e os tipos de integração da Task 3 ainda estavam ausentes).
- GREEN verificado: mesma seleção terminou com `1 passed, 0 failed` para o round-trip DM → prompt → send.
- Adicionados também testes offline para `send:false`, cancelamento, peer divergente e JSON escalar. O foco pós-hardening não foi relançado para não concorrer com a suíte LLM executada pelo agente principal.

Restrições respeitadas: nenhum Gateway/App live, login, envio real, Graphiti/DREAMS, subagente, commit ou staging. A diferença `ArgumentException` versus `InvalidDataException` observada na fronteira dos testes de integração permanece explícita para o agente principal ajustar sem alterar as interfaces da spec.
