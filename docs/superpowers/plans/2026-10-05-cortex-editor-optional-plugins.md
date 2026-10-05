# Cortex Editor and Optional DLL Plugins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Execution stays inline in the current checkouts; no subagents or independent review are authorized.

**Goal:** Ship a Cortex-owned editor that opens the Depois do Slogan graph using shared local JSON settings and dynamically loads the optional video DLL only when enabled.

**Architecture:** Keep the existing graph v3, node definitions, typed compiler/runner and reusable canvas. A small Core plugin contract/loader supplies existing `FlowNodeHandler` instances from enabled DLLs; `Cortex.Video` owns the three existing media nodes. A local ASP.NET Core editor and the separate consumer use the same settings and installed-plugin metadata.

**Tech Stack:** .NET 10, ASP.NET Core, System.Text.Json, AssemblyLoadContext/AssemblyDependencyResolver, existing TypeScript/DOM canvas, xUnit v3; existing ffmpeg/ffprobe and whisper.cpp CUDA 12.4 v1.9.2 for real acceptance.

**Spec:** `docs/superpowers/specs/2026-10-05-cortex-editor-optional-plugins-design.md` (approved).

## Global Constraints

- No static `Cortex.Video` project/assembly reference from Core, Editor or Depois do Slogan production code.
- Default plugin state is disabled. Check that state before opening its DLL, resolving a type or constructing the plugin.
- Preserve kinds `videoFile`, `transcribe`, `transcript`, their ports, graph v3 and `{ text, segments: [{ start, end, text }] }`, with times in seconds.
- Use the existing `NodeDefinition`, `FlowNodeHandler`, `TypedFlowCompiler`, `TypedFlowRunner` and `createFlowGraphEditor`; no second execution engine/canvas.
- Shared default settings path: `%LOCALAPPDATA%/Cortex/editor/settings.json`; explicit path override for tests/installations.
- Apply plugin loading/configuration changes after restarting the editor; communicate pending changes.
- Load/register nodes without starting tools or loading model weights. Execute Transcribe locally in Portuguese and propagate cancellation.
- Preserve graphs with disabled/missing plugins, report unavailable dependencies and block execution.
- Bind the host to loopback. Mutating/execution APIs require the local session and same-origin requests.
- Graph/configuration edits are blocked while a run is active. Render graph/plugin/transcript content as text.
- One video, exactly the three media nodes. No agents, batches, transcription cache, cuts, remote model/transport or editor integrations.
- Preserve unrelated Cortex Personas/Filo edits. Use scoped local commits on main; no push or ACL/ownership changes.
- Keep settings, media, model binaries, profiles and run artifacts outside committed source.

## Review Focus

1. Installed but disabled DLL, including broken path/dependency: a cold process must not open or load it (tasks 2, 7).
2. Invalid settings JSON, unknown plugin entries and a changed working directory: report corruption, preserve user data and resolve paths consistently (tasks 1, 4).
3. Graph with unavailable nodes: retain every node, setting and edge across opening/saving while refusing execution (tasks 4, 5).
4. Concurrent run/edit/save/cancel: no mutation of active bindings or duplicate transcription; release busy state after failure/cancellation (tasks 4, 5).
5. Installation/graph/tool paths containing spaces or Portuguese characters: launcher and tool arguments remain distinct, and output timestamps remain accurate (tasks 3, 6, 7).

## File Ownership Map

| Area | Files/responsibility |
| --- | --- |
| Core plugin contract/settings | `src/Cortex.Core/Plugins/NodePluginContracts.cs`, `NodePluginManifest.cs`, `CortexEditorSettings.cs`, `CortexSettingsStore.cs` |
| Core loader | `src/Cortex.Core/Plugins/NodePluginLoader.cs`, `NodePluginLoadContext.cs`; fixture under `tests/Cortex.Plugin.TestFixture/`; loader/settings tests under `tests/Cortex.Core.Tests/Plugins/` |
| Video DLL | `src/Cortex.Video/Cortex.Video.csproj`, `VideoNodePlugin.cs`, `VideoNodes.cs`, `Transcript.cs`, `WhisperCppTranscriber.cs`, `cortex-video.plugin.json`; `tests/Cortex.Video.Tests/` |
| Editor backend | `src/Cortex.Editor/Cortex.Editor.csproj`, `Program.cs`, `EditorHost.cs`, `EditorSession.cs`, `EditorGraphStore.cs`, `EditorExecutionService.cs`, `EditorCatalog.cs`, `EditorEndpoints.cs`; `tests/Cortex.Editor.Tests/` |
| Editor frontend | `src/Cortex.Editor/wwwroot/index.html`, `editor.js`, `editor.css`; `tests/frontend/cortex-editor.test.mjs`; link built `packages/flow-graph/dist` assets |
| Installation/launch | `src/Cortex.Core/Plugins/CortexEditorInstallation.cs`, `CortexEditorLauncher.cs`; `tools/install-cortex-editor.ps1`, `tools/start-cortex-editor.ps1` |
| Consumer | `C:/dev/depois-do-slogan/src/DepoisDoSlogan/{SloganGraph.cs,Program.cs}`, its tests and `tools/{transcribe.ps1,open-editor.ps1}`; remove consumer-owned transcription code/types |
| Documentation/build integration | Owned additions to `Cortex.slnx`, `README.md`, `docs/cortex-editor.md`; product context and editor surface brief derived from approved intent/current canvas |

## Task 1: Shared Settings and Plugin Contract

**Files:** Core contract/settings/manifest files above; `tests/Cortex.Core.Tests/Plugins/CortexSettingsStoreTests.cs`.

**Interfaces:**
- `IFlowNodePlugin`: `int ContractVersion { get; }`; `IReadOnlyList<FlowNodeHandler> CreateHandlers(NodePluginContext context)`.
- `NodePluginContext(JsonElement Configuration, string ConfigurationDirectory, Action<string>? Diagnostic)`; contract version is `1`.
- `NodePluginManifest`: ID, display name/description, assembly path, entry type, contract version and lightweight string configuration-field metadata. It is JSON, not plugin code.
- `CortexEditorSettings`: schema version `1`, plugin entries with ID/enabled/configuration. Missing known entries are disabled; unknown entries are retained.
- `CortexSettingsStore.DefaultPath`; `ReadAsync(CancellationToken)` returns parsed settings or an explicit error with original content; `SaveAsync(CortexEditorSettings, CancellationToken)` validates and atomically replaces the file. Resolve relative manifest paths against manifest location and relative configured tool paths against settings location.

- [ ] Write tests: `DefaultPathDoesNotDependOnWorkingDirectory`, `NewProfileDisablesInstalledVideo`, `UnknownPluginConfigurationSurvivesSave`, `CorruptJsonIsReportedAndNotOverwrittenByRead`, `RelativePathsUseConfigurationDirectory`.
- [ ] Run `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --filter FullyQualifiedName~CortexSettingsStoreTests`; verify failure for the missing behavior.
- [ ] Implement the interfaces and store. Use a same-directory temporary file + atomic replacement. A user-requested repair save preserves the corrupt original as a private backup before replacement; never repair during read.
- [ ] Run the targeted tests; expect zero failures and unchanged corrupt source after read.
- [ ] Commit only the owned Core files/tests: `feat(plugins): define shared local settings and node plugin contract`.

## Task 2: Disabled-by-default DLL Loading

**Files:** Core loader/context, fixture project `tests/Cortex.Plugin.TestFixture/{Cortex.Plugin.TestFixture.csproj,FixtureNodePlugin.cs}`, tests `tests/Cortex.Core.Tests/Plugins/NodePluginLoaderTests.cs`, owned test-project fixture build integration.

**Interfaces:**
- Consumes task 1 and existing `FlowNodeHandler`.
- `NodePluginLoader.Load(settings, manifests, configurationDirectory, diagnostic)` returns a runtime containing `Handlers`, per-plugin `PluginLoadReport` and disposal of loader resources.
- Load reports contain ID, enabled/loaded state and error; exceptions for one plugin do not erase another plugin's handlers.
- `NodePluginLoadContext` uses `AssemblyDependencyResolver`, shares the already-loaded Core contract assembly and framework assemblies, and resolves other dependencies from the plugin installation. Restart is the user-visible unloading boundary.

- [ ] Write tests: `DisabledMissingDllIsNeverOpened`, `EnabledFixtureReturnsHandlersAssignableToHostCore`, `UnsupportedContractIsReported`, `DuplicateKindsAreRejected`, `OneBrokenPluginDoesNotRemoveAnother`. Fixture outputs a literal through a real registered handler; Core tests must not reference its types statically.
- [ ] Build the fixture as a test artifact with `ReferenceOutputAssembly=false`; run loader tests and verify red.
- [ ] Implement loading strictly inside the enabled branch, validate interface/version/kind collisions, and report missing file, bad image/type/dependency errors. Copy fixture DLL/deps to a test-only installation folder; no fixture dependency in production.
- [ ] Run the targeted tests; execute the fixture graph with `TypedFlowRunner` and assert its typed output.
- [ ] Commit: `feat(plugins): load enabled DLL nodes through shared Cortex contracts`.

## Task 3: Move the Three Media Nodes into Cortex.Video

**Files:** Video module/manifest/tests from the ownership map; owned solution registrations and architecture check `tests/architecture/optional-video-plugin.test.mjs`.

**Interfaces:**
- `Cortex.Video.VideoNodePlugin : IFlowNodePlugin`, contract version `1`.
- `VideoNodes.CreateHandlers(IVideoTranscriber)` supplies the existing three handlers; `VideoType` and `TranscriptType` keep their existing shapes.
- Move `TranscriptResult`, `TranscriptSegment`, `IVideoTranscriber`, `TranscriptionTools`, `WhisperCppTranscriber` from the consumer and keep `TranscribeAsync(string, CancellationToken)`/`ParseTranscript(JsonElement)` behavior.
- Manifest configuration fields: `whisperExecutable`, `modelFile`, `ffmpegExecutable`, `ffprobeExecutable`. Paths/configuration bind without probing or executing the tools at plugin creation.

- [ ] Move/adapt the existing six behavioral tests to `Cortex.Video.Tests`; add `RegisteringVideoNodesDoesNotRequireToolFilesOrStartProcesses`. Architecture test rejects Video references from Core/Editor/consumer production projects.
- [ ] Run `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj`; verify the new registration behavior fails before implementing it.
- [ ] Implement plugin and metadata with no eager filesystem/tool/model initialization. Keep timestamps in seconds and validate invalid/unordered offsets. Preserve process argument-list handling and cancellation of the process tree.
- [ ] Run plugin tests and `node --test tests/architecture/optional-video-plugin.test.mjs`; expect all green. Load the actual Video DLL through task 2 and compile the existing saved graph without invoking Whisper.
- [ ] Stage solution additions independently of existing Personas changes; commit: `feat(video): ship the existing media nodes as an optional DLL plugin`.

## Task 4: Local Editor Host, Catalog and Run Lifecycle

**Files:** Editor backend files/tests listed above, Core installation descriptor, owned solution entries.

**Interfaces:**
- `EditorLaunchOptions`: graph path/entry ID, settings override, installation directory, loopback port, browser-open option and diagnostics mode for cold-process verification.
- `EditorHost.Build(EditorLaunchOptions)` creates a local WebApplication; Program starts it and opens its URL when requested.
- `EditorGraphStore`: open only an explicitly supplied graph or uploaded JSON; retain source/layout/unknown nodes; save an explicitly opened target atomically or return downloadable JSON.
- `EditorCatalog`: JSON descriptors from loaded `NodeDefinition`/ports/config fields plus generic fallback descriptors inferred from saved edges for unavailable nodes. No Video-type branches.
- `EditorExecutionService`: validate/compile with selected entry and active handlers, start one cancellable run, return real `TypedRunResult` as JSON values + schemas, retain only its local result and diagnostics. No retry/cache.
- APIs: `GET /api/state`, `GET /api/settings`, `PUT /api/settings`, `POST /api/graph/open`, `PUT /api/graph`, `POST /api/validate`, `POST /api/run`, `POST /api/run/cancel`, `GET /api/run`, `POST /api/graph/save`.
- Session bootstrap uses a per-process nonce in the initial URL fragment; browser requests carry a session header. Mutations also require same Origin. Do not persist nonce in settings/files.

- [ ] Write unit tests for unavailable-node preservation, snapshot/result isolation, cancellation/failure releasing busy state, and run/edit/settings exclusion. Write API tests for missing session, foreign Origin, valid session and cancellation; mark socket tests `Category=LocalSocket` and use fresh temporary profiles/ephemeral ports.
- [ ] Run targeted unit/API tests; verify red. API tests create their own host and never stop an existing editor/Playground.
- [ ] Implement backend using the shared store/loader/runner. On corrupt settings, show the original error with plugins disabled and allow explicit repair; do not overwrite on startup. Saving settings records pending-restart state, while the current handler snapshot stays unchanged.
- [ ] Run `dotnet test tests/Cortex.Editor.Tests/Cortex.Editor.Tests.csproj --filter Category!=LocalSocket`, then explicitly run `--filter Category=LocalSocket`; expect all green. Diagnostics mode reports actual loaded assembly names for a new process without starting any media tool.
- [ ] Commit: `feat(editor): host generic graphs with shared settings and local execution`.

## Task 5: Cortex-owned Editor UI

**Files:** Editor frontend/assets/tests, `PRODUCT.md`, editor surface brief and documentation of the existing canvas visual system.

**Interfaces:** Consumes task 4's JSON APIs and existing `createFlowGraphEditor` callbacks. Keep request/session handling in one helper and graph editing in one state owner; server is authoritative for validation/execution.

- [ ] Record confirmed product truth from the approved brief; inherit the existing canvas's visual identity. Read Impeccable craft-floor immediately before UI edits. Scope is Operate: open graph → inspect/edit parameters → validate/run → inspect result, plus Plugins settings.
- [ ] Write meaningful frontend tests: unavailable graph survives round trip; pending-restart display reflects saved/current state; no graph/settings mutation while busy; text-like HTML in transcript/labels remains text; failed validation never invokes run; cancel keeps result status accurate.
- [ ] Run `node --test tests/frontend/cortex-editor.test.mjs`; verify red before behavior implementation.
- [ ] Build the complete screen: top file/run/settings actions, available-node palette, reusable canvas, selected-node config fields and output/diagnostic inspector. Render plugin names/options from metadata; no `videoFile`/`transcribe`/`transcript` special cases in editor code. Empty/unavailable states point to Configurações → Plugins. Settings show saved versus active state and restart instructions.
- [ ] Link/copy existing built flow-graph assets through the Editor build/publish process; run frontend/editor tests. Inspect desktop and compact viewport in one round, fix functional/layout/accessibility defects together, then confirm once. No unrelated visual redesign or extra agent workflow.
- [ ] Commit: `feat(editor): add the standalone graph and plugin settings interface`.

## Task 6: Shared Installation and Consumer Opening

**Files:** Core launcher/installation, install/start scripts, consumer SloganGraph/Program/tests/scripts/README. Remove consumer-owned `WhisperCppTranscriber.cs`, media DTOs/interfaces/node handlers after migration.

**Interfaces:**
- `CortexEditorInstallation`: editor executable/DLL and plugin-manifest directory persisted under the same local Cortex editor directory; explicit executable/installation override supported.
- `CortexEditorLauncher.CreateStartInfo(string graphPath, string? entryId, string? editorExecutableOverride)` and `Open(...)` use ProcessStartInfo.ArgumentList, never shell interpolation, and find the shared installation independently of cwd.
- `install-cortex-editor.ps1` publishes Editor, builds/installs Video DLL/deps/manifest separately and records installation metadata. Fresh profile remains disabled. It preserves existing settings; demonstration activation is a separate explicit operation.
- Depois do Slogan keeps graph composition; `--editor`/`tools/open-editor.ps1` opens its graph through the common launcher. CLI uses the common settings/loader instead of local handlers. Existing explicit tool flags may override enabled plugin options in memory; they do not silently enable disabled plugins.

- [ ] Write tests: launch arguments preserve spaces/Portuguese characters; changing cwd retains shared installation/profile; consumer graph keeps exactly three nodes and both wire kinds; disabled module blocks consumer execution; enabled fixture/plugin runs via the common contract.
- [ ] Run targeted launcher/consumer tests; verify red.
- [ ] Implement installation and migration. Move media behavior tests into the plugin rather than keeping duplicated node implementations. Keep consumer CLI outputs (`graph.json`, `run.json`, `transcript.json`, diagnostics) local and compatible.
- [ ] Run `dotnet test C:/dev/depois-do-slogan/DepoisDoSlogan.slnx` and launcher tests. Verify Editor/consumer deps and project references contain no static Video reference. Use command-local `git -c safe.directory=C:/dev/depois-do-slogan` for that checkout; do not change ownership/ACL/global Git configuration.
- [ ] Commit owned files in each repository: `feat(editor): install and launch the shared Cortex editor` / `refactor: open and execute the graph through shared Cortex plugins`.

## Task 7: Cold Loading, Real Video and Final Acceptance

**Files:** Cold-process integration tests, owned docs/README and private verification artifacts only.

- [ ] Add cold-process tests with Video installed: diagnostics show no `Cortex.Video` assembly for default/disabled settings and show it only for enabled settings. Reopen saved graph after disable/restart and assert byte-equivalent nodes/settings/edges remain. Test corruption reporting without replacing settings; test one successful profile reopen from another cwd.
- [ ] Run cold tests in fresh processes and explicitly run Editor LocalSocket acceptance. Keep diagnostics output free of session tokens/private transcripts.
- [ ] Install the editor locally. Prepare the approved demonstration profile with Video enabled and the already-verified tools under `C:/dev/depois-do-slogan/.local/tools/`; preserve any preexisting unrelated settings. Open through the consumer and run the full 100.22-second `.local/media/commons-tutorial.webm` in the actual editor.
- [ ] Verify completed steps `video`, `transcribe`, `transcript`, typed text/segments and backend `using CUDA0 backend` on RTX 4090. Inspect actual canvas, properties, transcript and Plugins settings; retain local screenshots/result/log paths and source/license attribution. Do not correct recognition output automatically.
- [ ] Run `dotnet build Cortex.slnx`; offline suite `dotnet test Cortex.slnx --no-build --filter "Category!=Postgres&Category!=LocalSocket&FullyQualifiedName!~DatabaseLifecycleTests"`; consumer tests; architecture/editor/frontend Node tests. Run the existing Filo graph integration tests with `--artifacts-path` to avoid DLLs held by its open Playground. Repeat only failures or checks affected by subsequent fixes.
- [ ] Check spec coverage, scoped diffs and no staged private assets. Commit owned docs/final fixes; verify both branches/status. Leave the requested editor available and report URL, shared settings path, results, test evidence and commit IDs. No independent review is launched without separate authorization.

## Handoff

Execution method already established: implement inline, preserving the current checkouts and unrelated work, with scoped main commits. This plan requires user review before implementation under `writing-plans`; it does not authorize additional agents or a fresh independent-review session.
