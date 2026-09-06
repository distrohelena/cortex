# Auditoria do commit — typed-flow slice 1

## Seleção autorizada

Somente categoria 1 foi staged. A base anterior ainda não versionada foi incluída apenas onde necessária para editor, persistência, tipos, fake runtime, testes ou build.

| Categoria | Destino |
|---|---|
| 1: editor/canvas, host local mínimo, projeto, runtime tipado, completion schema, fixture/testes/build | Incluída |
| 2: executor DM antigo, ativação live, warmup MCP, árbitro conversacional, agente, envio, planos de canário live | Preservada fora |
| 3: configurações pessoais, appsettings, alterações de transporte, experimentos Frontend/Settings, DREAMS | Preservada fora |

Arquivos mistos tiveram conteúdo seletivo preparado no índice, não uma substituição do worktree:
- Program: somente entrada `--playground`/`--live-reload`.
- LocalPromptPlayground: assets/catálogo/projeto/typed endpoints; sem DmFlowLiveHosting, ativação, envio ou API antiga de geração.
- LmStudioClient: somente seleção de modelo por chamada e CompleteStructuredAsync; nenhuma alteração do árbitro/timeout/enum de participação.
- HTML/JS: sem controles ou chamadas HTTP de live.
- Assets/csproj: somente recursos necessários; nenhuma inclusão da página antiga de prompt livre.
- flow-model/flow-live.test: sem políticas/status de live; compatibilidade de projetos/portas mantida.
- C# incluído foi formatado apenas na cópia candidata; originais preservados.

## Conteúdo exato incluído

```text
.gitignore
docs/superpowers/plans/2026-09-06-typed-flow-slice1.md
docs/typed-flow-commit-audit.md
docs/typed-flow-slice1.md
packages/flow-graph/README.md
packages/flow-graph/build.mjs
packages/flow-graph/package.json
packages/flow-graph/src/camera.ts
packages/flow-graph/src/editor.css
packages/flow-graph/src/index.ts
packages/flow-graph/tests/editor.test.mjs
packages/flow-graph/tsconfig.json
scripts/build-flow-graph.mjs
src/Filomena.App/Filomena.App.csproj
src/Filomena.App/LocalPromptPlayground.cs
src/Filomena.App/Playground/PlaygroundAssets.cs
src/Filomena.App/Playground/PlaygroundModelCatalog.cs
src/Filomena.App/Playground/PlaygroundProjectStore.cs
src/Filomena.App/Playground/TypedFlowEndpoints.cs
src/Filomena.App/Playground/flow-camera.mjs
src/Filomena.App/Playground/flow-catalog.mjs
src/Filomena.App/Playground/flow-contract.mjs
src/Filomena.App/Playground/flow-layout.mjs
src/Filomena.App/Playground/flow-model.mjs
src/Filomena.App/Playground/flow-typed-panel.mjs
src/Filomena.App/Playground/flow.css
src/Filomena.App/Playground/flow.html
src/Filomena.App/Playground/flow.js
src/Filomena.App/Playground/live-reload.js
src/Filomena.App/Playground/model-catalog.json
src/Filomena.App/Program.cs
src/Filomena.Core/Flows/FlowDomain.cs
src/Filomena.Core/Flows/TypedFlowCompiler.cs
src/Filomena.Core/Flows/TypedFlowRunner.cs
src/Filomena.Llm/LmStudio/FlowModelCompletion.cs
src/Filomena.Llm/LmStudio/LmStudioClient.cs
tests/Filomena.Core.Tests/Flows/FlowDomainTests.cs
tests/Filomena.Core.Tests/Flows/TypedFlowSafetyTests.cs
tests/Filomena.Core.Tests/Flows/TypedSliceTests.cs
tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj
tests/Filomena.Llm.Tests/FlowGraphAssetTests.cs
tests/Filomena.Llm.Tests/PlaygroundCatalogTests.cs
tests/Filomena.Llm.Tests/PlaygroundProjectTests.cs
tests/Filomena.Llm.Tests/PlaygroundReloadTests.cs
tests/Filomena.Llm.Tests/TypedFlowIntegrationTests.cs
tests/fixtures/typed-flow-slice1.json
tests/frontend/flow-camera.test.mjs
tests/frontend/flow-catalog.test.mjs
tests/frontend/flow-execution.test.mjs
tests/frontend/flow-layout.test.mjs
tests/frontend/flow-live.test.mjs
tests/frontend/flow-message.test.mjs
tests/frontend/flow-model.test.mjs
tests/frontend/flow-project.test.mjs
tests/frontend/flow-response-fields.test.mjs
tests/frontend/flow-typed-contract.test.mjs
tests/frontend/flow-typed-panel.test.mjs
tests/frontend/live-reload.test.mjs
```

## Verificação do índice, independente do worktree

Cópia materializada por `git checkout-index --all --prefix=.../candidate/`. Não contém arquivos não staged, nem alterações não selecionadas nos arquivos mistos. TypeScript instalado foi reutilizado por junction temporária no node_modules ignorado. Nenhum Gateway/canal/modelo foi iniciado.

Comandos no candidato:

```powershell
dotnet test Filomena.slnx --verbosity quiet
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Typed|FullyQualifiedName~FlowDomain" --verbosity quiet
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-restore --verbosity quiet
node --test packages/flow-graph/tests/*.test.mjs tests/frontend/*.test.mjs
dotnet build src/Filomena.App/Filomena.App.csproj --no-restore --verbosity quiet
dotnet format Filomena.slnx --verify-no-changes --no-restore --include <todos os C# staged> --verbosity quiet
git diff --cached --check
```

Resultados:
- Core completo do candidato: **146/146**.
- Core focado do slice: **22/22**.
- LLM/backend do candidato: **28/28**.
- JS/canvas do candidato: **59/59**.
- Build App/Core/LLM/OpenClaw referenciado + TypeScript: **0 warnings, 0 errors**.
- Formatação de **todos os arquivos C# staged**: exit 0.
- JS syntax: exit 0.
- Fixture real importada via openProject/saveProject: igualdade estrutural integral, graph v3, 4 fios de dados, 3 de execução.
- HTTP smoke `HttpFakeExecutionAndDraftRoundTripNeverCallExternalServices`: passou. Lê o fixture do disco, valida, executa responder→c3→done, não chama modelo/transporte, salva rascunho inválido e recarrega após restart do host temporário.
- Três ações e falhas de schema permanecem cobertas na suíte Core.
- Os números são menores que os 96 LLM/62 JS do worktree anterior porque testes/funcionalidades de live não necessários ficaram deliberadamente fora.

## Falhas globais preexistentes, comprovadas

### HEAD limpo (sem slice)

Reproduzido em `git archive HEAD`, parent `0a43230`:

`OpenClawInboundBridgeReceiverTests.InvalidActorIdentityFailsClosedInsteadOfThrowing`:
esperava `Invalid`, recebeu `Accepted` (linha 73). A mesma falha aparece na suíte global do candidato: **41 passaram / 1 falhou** em OpenClaw.Tests. Não foi corrigida e nenhum arquivo de transporte entrou no commit.

### HEAD + somente testes preexistentes do worktree

Produção permaneceu exatamente HEAD. Foram copiados somente os dois arquivos de testes Core já modificados antes do slice. Resultado: **127 passaram / 19 falharam**, demonstrando independência do recorte:

1. FlowBindingValidatorTests.RequiresPrivateSpaceOwnerCharacterToExist
2. FlowBindingValidatorTests.RequiresCharacterGraphReferenceToBeACharacterGraphOwnedByThatCharacter
3. FlowBindingValidatorTests.ValidatesUnusedGrantCharacterSpaceAndMediatedProtectorReferences
4. FlowBindingValidatorTests.RejectsDuplicateDestinationIdsWithinAnEnvironment
5. FlowBindingValidatorTests.RejectsIngressGraphsCarryingCharacterIds
6. FlowCompilerTests.OversizedImportIsRejectedBeforeAllocatingItsUtf8Payload
7–19. FlowCompilerTests.MalformedDirectObjectsReturnSafeIssues, parâmetros:
`environment, field, graph, dataEdge, source, node, destination, conversation, space, fieldPath, controlEdge, profile, character`.

Adicionando os testes preexistentes `FrontendBuildTests.cs` e `SettingsStoreTests.cs` à produção HEAD, OpenClaw.Tests reproduz CS0234/CS0246: namespaces `Filomena.App.Frontend`, `Filomena.App.Settings` e tipos FrontendAssets/FrontendBuildService/IFrontendBuildProcessRunner/FrontendBuildProcessRequest/FrontendBuildProcessResult ausentes. Esses experimentos ficaram fora.

Não houve exclusão de testes já pertencentes a HEAD para tornar o candidato verde; a falha global existente é reportada explicitamente.

## Alterações preservadas fora do commit

Estado residual esperado após commit (inclui hunks não selecionados e diferenças de formatação preservadas):

```text
 M docs/filomena-dotnet-phase-c.md
 M src/Filomena.App/Filomena.App.csproj
 M src/Filomena.App/FilomenaTurnWorker.cs
 M src/Filomena.App/LocalPromptPlayground.cs
 M src/Filomena.App/Playground/PlaygroundAssets.cs
 M src/Filomena.App/Playground/flow-model.mjs
 M src/Filomena.App/Playground/flow.html
 M src/Filomena.App/Playground/flow.js
 M src/Filomena.App/Program.cs
 M src/Filomena.App/appsettings.json
 M src/Filomena.Core/Arbitration/TurnArbitration.cs
 M src/Filomena.Core/Arbitration/TurnDecision.cs
 M src/Filomena.Core/Conversations/ConversationTurnManager.cs
 M src/Filomena.Core/Conversations/ConversationTurnOptions.cs
 M src/Filomena.Core/Conversations/ConversationTurnState.cs
 M src/Filomena.Core/Conversations/IConversationTurnExecutor.cs
 M src/Filomena.Llm/Agent/FilomenaAgent.cs
 M src/Filomena.Llm/Agent/FilomenaAgentOptions.cs
 M src/Filomena.Llm/Arbitration/ArbiterPrompt.cs
 M src/Filomena.Llm/Arbitration/GemmaTurnArbiter.cs
 M src/Filomena.Llm/LmStudio/ILmStudioChatClient.cs
 M src/Filomena.Llm/LmStudio/IResponseChatClient.cs
 M src/Filomena.Llm/LmStudio/LmStudioClient.cs
 M src/Filomena.Llm/LmStudio/LmStudioOptions.cs
 M src/Filomena.OpenClaw/Bridge/OpenClawInboundBridgeReceiver.cs
 M src/Filomena.OpenClaw/Mcp/OpenClawMcpSender.cs
 M tests/Filomena.Core.Tests/ConversationTurnManagerTests.cs
 M tests/Filomena.Core.Tests/Flows/FlowBindingValidatorTests.cs
 M tests/Filomena.Core.Tests/Flows/FlowCompilerTests.cs
 M tests/Filomena.Core.Tests/TurnArbitrationTests.cs
 M tests/Filomena.Llm.Tests/FilomenaAgentTests.cs
 M tests/Filomena.Llm.Tests/GemmaTurnArbiterTests.cs
 M tests/Filomena.Llm.Tests/LmStudioClientTests.cs
 M tests/Filomena.OpenClaw.Tests/OpenClawMcpSenderTests.cs
 M tests/Filomena.OpenClaw.Tests/PhaseBCompositionTests.cs
 M tests/Filomena.OpenClaw.Tests/PhaseCPipelineTests.cs
 M tests/frontend/flow-live.test.mjs
?? docs/dm-flow-canary.md
?? docs/local-prompt-playground.md
?? docs/superpowers/plans/2026-09-04-dm-adaptive-wait.md
?? docs/superpowers/plans/2026-09-05-local-flow-editor.md
?? docs/superpowers/plans/2026-09-06-dm-flow-live-task1-report.md
?? docs/superpowers/plans/2026-09-06-dm-flow-live-task2-report.md
?? docs/superpowers/plans/2026-09-06-dm-flow-live.md
?? docs/superpowers/plans/2026-09-06-flow-graph-library.md
?? docs/superpowers/specs/2026-09-06-dm-flow-live.md
?? packages/flow-graph/example.html
?? src/Filomena.App/FlowRuntime/
?? src/Filomena.App/OpenClawMcpWarmupService.cs
?? src/Filomena.App/Playground/LocalFlowRunner.cs
?? src/Filomena.App/Playground/index.html
?? src/Filomena.App/TimedLmStudioClient.cs
?? src/Filomena.Llm/Arbitration/ArbitrationReferences.cs
?? tests/Filomena.Llm.Tests/DmFlowExecutorTests.cs
?? tests/Filomena.Llm.Tests/DmFlowLiveTests.cs
?? tests/Filomena.Llm.Tests/LocalFlowTests.cs
?? tests/Filomena.Llm.Tests/LocalPromptPlaygroundTests.cs
?? tests/Filomena.OpenClaw.Tests/FrontendBuildTests.cs
?? tests/Filomena.OpenClaw.Tests/SettingsStoreTests.cs
?? tests/Filomena.OpenClaw.Tests/TimedLmStudioClientTests.cs
?? workspace/DREAMS.md
```

Nenhum token/credential/configuração local foi staged. O model-catalog incluído é apenas o seed público de IDs de modelos/provedores, sem endpoint ou chave. Artefatos temporários estão em .tmp-slice-commit, ignorados. Sem push, sem alterações de Gateway/Graphiti/DREAMS.
