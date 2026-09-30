# Small test checkpoint — flow work paused

The flow design and existing work remain preserved, but further implementation is paused until Helena tests and approves the next small slice. This checkpoint is only an isolated prompt playground, not a flow editor or a production configuration UI.

From `C:\dev\filomena`:

```powershell
dotnet run --project src/Filomena.App/Filomena.App.csproj -- --playground
```

Open http://127.0.0.1:18793. Start the LM Studio HTTP server yourself with the existing model loaded. This mode reuses `LmStudio` options from appsettings/environment, sends only the page's system prompt and user message, and shows the result. Page edits are not persisted and do not change the production personality. No conversation history is accumulated between tests.

## Development live reload

Start the playground once with:

```powershell
dotnet run --project src/Filomena.App/Filomena.App.csproj -- --playground --live-reload
```

The startup log prints the source directory, `src/Filomena.App/Playground`. HTML/CSS/JS are then served directly from these source files. The page checks a C# content revision every second and reloads when it changes, without rebuilding or restarting the backend. Temporary file-save/server errors are retried. Only explicitly registered frontend files can be requested, not arbitrary files from that directory.

Reload waits for active prompt/flow execution to finish. It preserves the flow draft, selected node, input fields and layout in this tab's session storage; it does not rerun the model or restore execution traces/results. Saving a JSON file remains the durable way to keep a draft. No data is uploaded by reload. Storage failures defer reload rather than knowingly discard the draft.

Without `--live-reload`, including normal published use, frontend files are embedded and no reload endpoint/script is exposed. The flag requires the source tree and fails clearly if it cannot find it. C# changes still require rebuild/restart; this feature watches frontend content only. Do not restart the OpenClaw gateway for this workflow.

The page is embedded in the application. This explicit mode does not start the normal workers, connect to OpenClaw, invoke arbitration, touch Graphiti, or send channel messages. It does not call model-management/load APIs. Explicit per-node model selections are sent only when executing a flow; LM Studio may load the requested model according to its own server configuration. Ctrl+C stops only this playground process.

## Visual flow checkpoint

The same command now also serves **http://127.0.0.1:18793/flow**. The original prompt page remains at `/`.

- Start with DM received → prompt → screen output. Add prompts with **Add → Prompt**.
- Drag any non-port area of a node. Click a source **saída** then a target **entrada** to connect. A new wire replaces the existing outgoing wire; independent entries may converge. Click a wire to remove it.
- Hold the right or middle mouse button and drag anywhere inside the flow to pan the camera. Scroll zooms between 25% and 300% around the cursor. The left button still edits nodes and wires; native context menus/middle-click auto-scroll are suppressed only inside the viewport. Camera movement does not change saved node positions or invalidate execution results. The grid moves/scales with the world, and the X/Y axes remain one screen pixel thick across the viewport. The camera can look past either side of the origin; node-position limits remain unchanged. Camera position resets on page reload and is not stored in the graph JSON.
- On desktop, drag the divider below the editor to redistribute height between the graph and the test panels. Drag the vertical dividers to resize the inspector or the input/output panels. Focus a divider and use arrow keys (20px steps), Home or End for keyboard control. Minimum sizes keep panels usable. These layout sizes last only while the page is open and are not stored in the graph JSON. Narrow screens keep the stacked layout.
- Select a prompt to edit its system instruction. To chain two prompts, wire input→prompt1→prompt2→output. Screen position and node creation order do not decide execution.
- Type a test message and click **Executar flow**. Select any node afterwards to inspect its input, output, duration, and error. Trace is returned when execution finishes or fails, not streamed live. Cancel aborts the HTTP request and propagates cancellation to the model request; the provider may take time to stop its own computation.
- **Salvar JSON / Abrir JSON** exports/imports the graph and its positions. It does not include test messages or run traces. Files are plaintext; prompts may contain information you should inspect before sharing. Unsaved changes disappear on refresh.

The editor offers **Add → Entradas → DM recebida / Mensagem de grupo** and **Add → Prompt** instead of manual-entry nodes. Multiple event entries may share downstream prompts; each simulation executes only the selected entry's path, with fresh message state. Choose an entry under **Simular evento**, type its simulated message and click **Simular**. No provider, account, chat routing or live subscription is configured here. Legacy manual-entry JSON opens as a simulated DM entry.

### States and Exit — design only

**Projeto → States**, left of Add, lists the project's states. Create, rename or select a state to edit its independent flow. The toolbar's **Editando** label is an editor selection, not a live session state. New states start with independent DM → Prompt → Output drafts. An initial-state selector records the future starting state for sessions, without changing any actual conversation.

**Add → Controle de fluxo → Exit** adds a terminal node with an incoming connection, destination-state combobox and two modes: **Só trocar** (`switch`, next state waits for another message) or **Trocar e levar** (`carry`, future runtime forwards the current execution data). The destination is a state ID, not its display name, so renaming keeps references intact. The last terminal cannot be removed; once Exit is added, the old Output may be removed. An unset destination is allowed in drafts.

**Salvar JSON** exports `filomena-local-project`, version 1: up to 16 state definitions, initial state ID, `stateScope: session`, and each state's graph (up to 20 nodes). Project files are bounded to 1 MB; each graph retains its 64 KB bound. Old single-flow JSON opens inside **Inicial**, preserving its content. Live reload preserves all state drafts and the editor selection, but no active runtime state, conversation data or traces are exported. These definitions do not implement an FSM, scheduling, transitions or memory. Simulation is blocked for multi-state projects and flows containing Exit/Acumular to avoid pretending to execute the design partially.

This checkpoint supports up to 20 nodes, one output and one outgoing wire per node. Multiple incoming wires do not join executions or aggregate messages. All entries must reach the output; cycles, forks, duplicate IDs, disconnected nodes and empty prompts are rejected before inference. Each text input/system prompt is limited to 16,000 characters and requests/files to 64 KB. A failure stops later nodes and preserves earlier trace entries. The test panel runs one simulation at a time. There is no history accumulation, memory, channel send or publication. Restart/rebuild only the playground after this C# change; the Gateway remains under Helena's control.

Each prompt's inspector has closed **Provider** and **Modelo** comboboxes. Options come exclusively from the user's `model-catalog.json`, not from loaded models or provider discovery. Select a provider to see only its catalog models. Entries cannot be typed or created inside a node. Both selections are preserved in JSON and reload snapshots; older JSON without them still works. Empty values retain the existing inheritance of `lmstudio` and the backend model. Old selections removed from the catalog remain visible as disabled “fora do catálogo” options and are not silently replaced; changing provider explicitly clears that node's model selection. Provider/model identifiers are limited to 120/256 characters; no credentials or endpoint URLs are configured here.

With `--live-reload`, edit `src/Filomena.App/Playground/model-catalog.json`; saving it triggers reload. In normal/published mode, the editable file is `model-catalog.json` beside the executable; reload the page after editing. The starter file contains LM Studio with Gemma 4 E4B and Qwen3 4B 2507 examples, plus OpenAI/Anthropic provider entries with empty model lists for the user to populate. The complete user file replaces these defaults, never merges with them. An explicitly empty provider list remains empty; malformed catalogs display an error rather than silently restoring starter options. If the file is absent, embedded starter defaults are available.

Catalog shape:

```json
{
  "version": 1,
  "providers": [
    { "id": "lmstudio", "name": "LM Studio", "models": [
      { "id": "google/gemma-4-e4b", "name": "Gemma 4 E4B" }
    ] }
  ]
}
```

Catalog editing is file-based in this small slice; there is no separate catalog-management screen yet. Adding a catalog entry describes a design option, not an installed provider integration.

Any provider can be designed and saved. The local executor currently supports only `lmstudio`; it rejects unsupported providers with the node ID before any inference. Each explicit LM Studio model is sent in that node's request without mutating shared defaults. An unavailable model fails at execution rather than preventing graph design. Other providers require future runtime integration; the editor does not imply that those integrations already exist.

Local files use `format: filomena-local-flow` plus a `graph` with `version: 1`, `nodes`, and `edges`. This is a small editor draft, **not** the existing compiler's portable release format. The larger contracts and unfinished compiler work remain preserved and unmodified.

Next step: user tests the graph. No automatic continuation of the flow-engine roadmap.

## Opt-in real DM flow

The separate `--playground --flow-live` mode now supports only the typed DM → instruction → send path after explicit activation. Ordinary `--playground` still sends nothing to channels. See [the DM canary procedure](dm-flow-canary.md) for configuration, wiring, restrictions, and deactivation. This does not enable Acumular, Branch, or state transitions.

## Structured-message design revision

The editor now uses graph version 2. DM and group remain separate input nodes, but both expose only `mensagem (Mensagem)`: a design-level object containing `texto`, `remetente: Pessoa`, and `canal: Canal` (the DM or group conversation). No separate sender/group output remains. This object has no runtime implementation yet; it is not silently converted to text or used as a recipient.

As explicitly requested, loading version 1 clears all wires in every state, preserving nodes, positions, prompts, and settings. Original JSON files are not overwritten. Fresh graphs start disconnected; version 2 saves preserve new wires normally. The old live compiler is unchanged and rejects version 2, so these new designs cannot silently run through the former text-only pipeline. An already active server snapshot is separate from this draft and is not changed by editing it.

## Execution wires (design only)

Titlebar execution connectors use separate `controlEdges` with `{source, sourcePort, target, targetPort}`. Input nodes expose `then`; Exit exposes only `exec`; Branch exposes `true` and `false`; other nodes expose `exec` and `then` (including Send). Each execution output has one successor; different outputs can converge on the same input. Data remains in `edges` and never creates execution wires automatically.

Execution wires are white, separately removable, and persist through project saving and reload. Branch outcomes appear in the titlebar instead of the data area. Selecting an execution connector cannot connect to a data connector. No scheduling semantics were implemented; local simulation refuses graphs with execution wires.

The reusable TypeScript editor is in `packages/flow-graph`; transport, persistence and execution remain outside it.

## Backend project saving

`Projeto → Salvar` posts the design document to `/api/flow/project`. GET on the same route loads it (204 before the first save). New editor tabs load the last saved project; live-reload recovery retains the unsaved tab draft instead of overwriting it with an older server copy.

Storage: `%LOCALAPPDATA%/Filomena/playground/project.json`, independent of build output. Writes replace the destination only after writing a temporary file. Requests use the existing loopback/Host/Origin/header protection and a 1 MB limit. Only design properties are accepted; saving does not activate a flow, store simulated messages/traces, or call a model. Existing JSON buttons remain import/export.

Restart Filomena.App with a build to expose the new routes. Export the current browser draft as a precaution before the first restart, then use Projeto → Salvar. No gateway restart is required.
