# Flow Graph TypeScript — Implementation Plan

**Goal:** Extract the existing canvas, camera, node/port rendering and wire interactions into a reusable local TypeScript library without changing execution.

**Approved design:** Plain TypeScript, no Vue requirement; Filomena supplies node descriptions and editing callbacks. Projects/FSM, prompts, provider catalog, HTTP/MCP, memory and execution remain outside. Preserve current visuals and saved graph IDs/ports.

**Architecture:** `packages/flow-graph` owns generic DOM rendering and interaction. The host passes its existing graph and callbacks through `createFlowGraphEditor`. Keep `flow-camera.mjs` as a compatibility facade for existing consumers/tests. Compile ESM and declarations with installed TypeScript 5.9.3, embed generated assets in the existing .NET app. No global dependency installation.

## Constraints

- No Gateway start/stop, channel login, real messages, Graphiti or DREAMS changes.
- No new execution runtime, persistence, global identity, framework migration or npm publication.
- Work in existing dirty checkout, preserving unrelated changes; no push or combined commit.

## Task 1 — Generic package

Files: `packages/flow-graph/{package.json,tsconfig.json,build.mjs,src/index.ts,src/camera.ts,src/editor.css,tests/editor.test.mjs}`.

- [x] Write tests against generic node kinds (`source`, `transform`, `sink`), proving selection, separate data/control connection callbacks, node movement and disposal without any Filomena types.
- [x] Implement exported `GraphNode`, `GraphEdge`, `GraphDocument`, `PortView`, `NodeView`, `EditorOptions` contracts. Node content uses textContent, never interpreted HTML. IDs remain opaque.
- [x] Move camera math/events into typed source with `destroy()` cleanup; preserve middle/right pan, wheel cursor anchor, zoom limits and node-drag exclusion.
- [x] Implement `createFlowGraphEditor(options)` returning `render`, `redrawWires`, `clearConnection`, `getCamera`, `destroy`. Host owns data mutations, node schema and policy. No imports from host code.
- [x] Build ESM/declarations and test via Node. Standalone package build uses its own installed TypeScript; repository build can explicitly supply the existing compiler.

## Task 2 — Filomena adapter and embedded assets

Files: `Playground/flow.js`, `flow-camera.mjs`, `flow.html`, `PlaygroundAssets.cs`, `Filomena.App.csproj`, host tests.

- [x] Replace DOM/camera/wire implementation in flow.js with a node-descriptor adapter and callbacks to existing `connect`/`connectExecution`/state handlers. Keep sidebar/FSM/catalog/API logic in flow.js.
- [x] Register explicit routes for library ESM and CSS; embed assets using stable logical names. Development reads generated package files; package build plus existing live reload handles TS edits.
- [x] Add local .NET build target that compiles package before embedding, failing clearly when compiler is unavailable. Published executable serves embedded assets without Node at runtime.
- [x] Keep standalone framework-free example and README documenting mounting, styles, callbacks and disposal.

## Task 3 — Verification

- [x] Run package build/typecheck/tests, existing frontend tests, JS syntax checks, .NET App/LLM tests and `git diff --check`.
- [x] Check package imports for application dependencies and ensure retained camera tests exercise library implementation through facade.
- [x] Document any preexisting suite blockers separately; do not hide them. No real inference or outbound canary.

Execution authorized inline by user (“go ahead”). Plan recorded before implementation; approval scope is extraction, not redesign.

## Executed verification

- `node scripts/build-flow-graph.mjs`: TypeScript strict build and declarations passed.
- `node --test tests/frontend/*.test.mjs packages/flow-graph/tests/*.test.mjs`: 50 passed.
- `node --check src/Filomena.App/Playground/flow.js`: passed.
- `dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-restore -p:OutputPath=C:/dev/filomena/.tmp-flow-library-validation/ --verbosity minimal`: App/dependencies built; 93 tests passed, including embedded asset HTTP routes.
- `git diff --check`: passed (line-ending conversion warnings only).
- Normal App output was locked by the user's running process; validation used a separate output directory. No process was stopped.
- Browser visual verification pending user App restart; no Gateway, Graphiti, model or channel operations performed.
- No commit or push. Unrelated dirty files preserved.
