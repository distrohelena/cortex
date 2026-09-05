# Filomena Local Dashboard Implementation Plan

**SUPERSEDED:** Do not execute this plan. The product is now the multi-character flow engine described in `docs/superpowers/specs/2026-09-05-filomena-flow-engine-design.md`. Local hosting, safety and persistence requirements that remain applicable are carried into that specification. Preserve unfinished work for explicit reuse/review; it is not completed functionality.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Subagents require Helena's explicit approval. Steps use checkbox syntax for tracking.

**Goal:** Configure and monitor Filomena through a local Vue/TypeScript panel served by the existing .NET executable.

**Architecture:** A typed persistent settings store owns desired/effective configuration. Conversation turns use immutable revision snapshots; a separate final-send gate honors pause/revocation. The existing Kestrel host serves protected administration APIs, diagnostics and atomically built static assets.

**Tech Stack:** Existing .NET 10 SDK/global.json, ASP.NET Core, xUnit, Vue, TypeScript, Vite, Vitest and browser tests. Preserve the official MCP SDK and existing projects.

**Spec:** docs/superpowers/specs/2026-09-05-filomena-local-dashboard-design.md

## Global constraints

- Default origin: http://127.0.0.1:5147. Never bind wildcard or LAN addresses.
- No push. Commit only scoped artifacts; preserve the existing dirty worktree.
- Do not introduce an independent package or new database just for this dashboard.
- Preserve /v1/openclaw/events/inbound and passive observation.
- Do not start/stop/relink Gateway, call a live model, or send live messages in tests.
- Never modify workspace/DREAMS.md, Graphiti Core, namespace/UUID schemes or memory isolation.
- Preserve current response access: Helena DM and Grupo Teste only; Drift/Plex remain blocked.
- Read the complete spec before implementation. Use Release builds because Helena may be running Debug.

## Execution preparation

- [ ] Inspect git status and keep the preexisting uncommitted Phase-C changes intact. They are required dependencies, not disposable modifications. Read the worktree skill before execution; do not create a clean checkout that omits those dependencies. Helena previously requested main; ask before changing branch strategy.
- [ ] Run `dotnet test Filomena.slnx -c Release --no-restore` and record the baseline (last observed: 125 tests). Inventory current options and prompts from source, not old documentation.
- [ ] Check Node/npm versions. Before selecting Vue/Vite dependencies, verify supported versions against their official documentation; pin the selected versions and generate a lockfile. No global installs or new .NET SDK.

## File ownership and common contracts

Keep all new backend code in the existing assemblies:

| Area | Files | Responsibility |
| --- | --- | --- |
| Persistence | src/Filomena.App/Settings/{DashboardSettings,SettingsStore,SettingsValidator,SettingsCatalog}.cs | Validated document, revisions, durable writes, field metadata |
| Runtime | src/Filomena.Core/Conversations/{TurnSettingsSnapshot,ResponseGate}.cs | Provider-neutral per-turn settings and final authorization |
| Web | src/Filomena.App/Admin/{AdminEndpoints,AdminSecurity,AdminDtos}.cs | Local API, request protection and DTOs |
| Observability | src/Filomena.App/Diagnostics/{DiagnosticBuffer,DashboardStatus,ConversationCatalog}.cs | Bounded safe events and observed identities |
| Build | src/Filomena.App/Frontend/{FrontendBuildService,FrontendAssets,FrontendBuildOptions}.cs | Startup build, cancellation, cache, asset publication |
| UI | frontend/src/{api,views,components}/ | Typed client, forms, dashboard |

Boundary signatures (define these in the named task before use):

```csharp
// Task 1, Filomena.App.Settings
public sealed record SettingsEnvelope(long Revision, DashboardSettings Desired,
    DashboardSettings Effective, IReadOnlyList<string> RestartRequired,
    IReadOnlyList<string> ExternallyOverridden, string RecoveryStatus);
public sealed record SettingsUpdate(long ExpectedRevision, DashboardSettings Desired);
public sealed record SettingsIssue(string Path, string Code, string Message);
// SettingsStore: Current, InitializeAsync(CancellationToken),
// UpdateAsync(SettingsUpdate,CancellationToken) -> Task<SettingsEnvelope>.
// Validation/conflict failures use dedicated exceptions handled at API boundary.

// Task 2, Filomena.Core.Conversations. Concrete snapshot contains typed copies of
// scheduling and model/prompt values, not IConfiguration or mutable Options objects.
public interface IResponseGate
{
    long Epoch { get; }
    bool CanDispatch(string conversationNamespace, long turnEpoch);
}
```

Core cannot depend on App/Llm assemblies. Put provider-neutral snapshot value records
in Core; map Llm/App option objects at composition boundaries. Preserve existing
constructors with fixed-setting adapters where needed for consumers and tests.

### Task 1: Durable settings with revisioned desired/effective values

**Files:** Create Settings files above; modify FilomenaHostConfiguration.cs and Program.cs; add tests/Filomena.OpenClaw.Tests/SettingsStoreTests.cs.

**Interfaces:** SettingsStore exposes an immutable Current envelope. DashboardSettings contains schemaVersion, response pause, Turns, model values, generation prompts, arbiter prompts, Probe, bridge and frontend options. SettingsCatalog provides path/type/unit/min/max/enum/applicationMode for every supported field.

- [ ] Write tests using temporary directories: first-use exact allowlist, save/reload, stale expected revision, invalid finite/range values, full array replacement, corruption with/without backup, failed replacement and unknown schemaVersion. Example acceptance:

```csharp
var before = store.Current;
await Assert.ThrowsAsync<SettingsConflictException>(() =>
    store.UpdateAsync(new SettingsUpdate(before.Revision - 1, before.Desired), token));
Assert.Equal(before.Revision, store.Current.Revision);
```

- [ ] Run `dotnet test tests/Filomena.OpenClaw.Tests -c Release --filter SettingsStoreTests`; observe missing behavior fail.
- [ ] Implement `%LOCALAPPDATA%/Filomena/settings.json`; temporary writes on the same filesystem, flush, File.Replace for existing file with a previous-version backup, File.Move for first create. Publish state only after success. Hold a store-local update gate and exclusive file lease, never a global conversation lock.
- [ ] Load defaults then persistent desired values, then explicit environment/CLI overrides with whole-array replacement. Record override source/paths. Compare restart-only fields against startup-effective values. Reject unknown fields/schema versions, NaN/infinity, invalid timing order, noncanonical conversation entries and nonloopback listener settings.
- [ ] Bound editable prompts to 32,768 characters each and API bodies to 512 KiB. Keep defaults restorable. If no persisted/backup document validates, disable outbound and expose repair status; do not destroy corrupt files.
- [ ] Run tests and full baseline; commit only task-owned changes, without sweeping unrelated staged files.

### Task 2: Per-turn snapshots and immediate response suspension

**Files:** Create Core snapshot/gate files; modify ConversationTurnManager.cs, IConversationTurnExecutor.cs, FilomenaAgent.cs, ArbiterPrompt.cs, LmStudioClient.cs, OpenClawMcpSender.cs and Program.cs; extend Core/Llm/OpenClaw tests.

**Interfaces:** Snapshot carries revision and authorization epoch through arbitration and generation requests. SettingsStore maps to a snapshot factory; ResponseGate uses current pause/allowlist and monotonically increasing revocation epoch.

- [ ] Add fake-clock/gated-model tests: changing settings during an active burst keeps its revision; next burst takes new revision; pause or revoke during model/preflight suppresses dispatch; resuming never revives that draft; another conversation keeps independent history.

```csharp
var epoch = gate.Epoch;
gate.SetPaused(true);
gate.SetPaused(false);
Assert.False(gate.CanDispatch(testNamespace, epoch)); // no stale draft resurrection
```

- [ ] Run focused tests and observe failure before changing the scheduling code.
- [ ] Snapshot at first message of each new burst. Store on the pending turn, not the global singleton. Rechecks and continuations reuse it. Pass typed per-request model options without mutating HttpClient.BaseAddress in-flight; endpoint changes remain restart-only.
- [ ] Move behavioral arbiter DM/group strings to settings defaults; append immutable output contract and dynamic schema afterwards. Show protected text read-only in UI. Retain short-ref translation and validation tests.
- [ ] Apply response pause/permission before new arbitration/generation and again after MCP readiness immediately before messages_send. Do not queue suppressed responses. Record gate outcome safely. Already dispatched messages cannot be recalled.
- [ ] Make grants effective for future turn epochs only. Serialize gate update and final dispatch admission sufficiently to define which operation won; never hold that gate across a provider network await.
- [ ] Run all .NET tests; commit task changes only.

### Task 3: Local admin API, secure mutations and diagnostics

**Files:** Create Admin and Diagnostics files above; extend InboundBridgeHttpServer.cs, FilomenaTurnWorker.cs, TimedLmStudioClient.cs and Program.cs; create AdminEndpointTests.cs and DiagnosticBufferTests.cs under tests/Filomena.OpenClaw.Tests.

**Interfaces:** MapAdminEndpoints takes existing singleton SettingsStore, gate, status/catalog/buffer. No second copy of runtime services in the nested web host. Diagnostic entries contain sequence/time/stage/callId/namespace/revision/elapsedMs/code, no content.

- [ ] Write real loopback HTTP tests on an ephemeral port with fake transport/model: foreign Host/Origin, missing token, invalid JSON, stale revision, oversize body, pause while receiver accepts inbound, unknown API returning 404 rather than HTML.

```csharp
request.Headers.Add("Origin", "https://foreign.example");
var response = await http.SendAsync(request, token);
Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
Assert.Equal(oldRevision, store.Current.Revision);
```

- [ ] Run focused failing tests. Implement `GET /api/admin/bootstrap`, `/settings`, `/settings/schema`, `/health`, `/conversations`, `/diagnostics`; `PUT /settings`; `POST /responses/pause` with `{paused,expectedRevision}`. Validate before persistence. Return field errors as 400, revision conflict as 409, payload overflow as 413.
- [ ] Bootstrap returns per-process random anti-CSRF token. Require exact local Host, same-origin Origin, JSON and token for mutations. No CORS, token URLs or secret-bearing logs. Set no-store on admin responses and restrictive CSP/frame restrictions on HTML. Do not require browser tokens for the existing machine bridge route.
- [ ] Add a 500-entry safe diagnostic ring; SSE consumes sequence snapshots without unbounded per-browser queues. Report gaps after old cursors and release cancellation resources. Redact by allowlisted fields, not regex over arbitrary exception/provider payloads.
- [ ] Populate conversations from admitted events and bounded read-only MCP listing. Merge only by normalized conversation identity. Do not scrape transcript names. Display unavailable listing as unavailable; never invent a JID.
- [ ] Distinguish MCP liveness from WhatsApp delivery and model idle from failed. Emit structured timing events alongside existing console logs. Run tests and commit.

### Task 4: Vue forms and live status

**Files:** Create frontend/package.json, package-lock.json, tsconfig.json, vite.config.ts, index.html, src/{main.ts,App.vue,api/client.ts,api/types.ts}, views/{Overview,Conversations,Model,Behavior,Prompts,Diagnostics,Advanced}.vue and component tests.

**Interfaces:** Typed SettingsEnvelope/Update DTOs matching backend camelCase. Forms consume SettingsCatalog field metadata for units and application mode; save a complete validated draft with expectedRevision.

- [ ] Pin dependencies verified in preparation; install once to produce lockfile. Add npm scripts: `typecheck` = `vue-tsc --noEmit`, `build` = `vue-tsc --noEmit && vite build`, `test` = `vitest run`. Do not add remote CDN scripts/fonts.
- [ ] Write component tests before implementing save/pause: unsaved draft survives validation error, 409 requests refresh without overwrite, restart badges visible, pause button reports server state, restore prompts asks confirmation, all catalog paths have controls.

```ts
const result = await saveSettings({ expectedRevision: 7, desired: draft });
expect(result.revision).toBe(8);
expect(result.restartRequired).toContain('model.endpoint');
```

- [ ] Implement sidebar navigation and Portuguese forms with explicit units, keyboard focus, error association and dirty-state navigation warning. No auto-save on keystrokes. Arrays use structured editors; prompts use multiline editors with preview/default restore.
- [ ] Conversation switches use native namespace keys; show display name separately. Explain observation-only vs response authorization and preserve disabled groups.
- [ ] API client uses same-origin relative URLs, JSON, bootstrap token header, revision conflicts and typed errors. Diagnostics use SSE with a bounded client list; no v-html rendering of configuration/log content.
- [ ] Run `npm --prefix frontend run typecheck`, `npm --prefix frontend test`, `npm --prefix frontend run build`; commit UI plus lockfile only, not node_modules/dist.

### Task 5: Automatic build and same-process static serving

**Files:** Create Frontend files above; modify InboundBridgeHttpServer.cs, Filomena.App.csproj, Program.cs and .gitignore; create FrontendBuildTests.cs.

**Interfaces:** FrontendAssets exposes selected complete asset directory plus build state. FrontendBuildService runs once at startup, not once per request. Cache and lock live under LocalAppData/Filomena/frontend.

- [ ] Write tests using a fake child runner: cache hit skips build, changed source rebuilds, failed build keeps previous pointer, no previous build gives status page, cancellation kills child tree, concurrent startup serializes publication, traversal cannot escape asset directory.

```csharp
await builder.BuildIfChangedAsync(token);
var good = assets.CurrentBuild;
runner.FailNext = true;
await builder.BuildIfChangedAsync(token);
Assert.Equal(good, assets.CurrentBuild);
Assert.Equal("failed", assets.Status);
```

- [ ] Hash tracked frontend input bytes/relative paths, lockfile and build settings, excluding dist/node_modules/cache. Execute installed Node with the checked-in build entry, fixed arguments and working directory. Build into a unique staging directory and update the current pointer only after success. Default timeout 120 seconds; output is bounded and sanitized.
- [ ] Keep immutable build directories long enough for open pages' hashed assets. Serve fingerprinted assets with immutable caching and HTML with revalidation. Prefer published prebuilt assets when source is absent; do not require Node in that mode.
- [ ] Build runs in the background. Publish health/status even if dependencies are absent; never install them implicitly. Shutdown cancels and terminates the owned child process tree without touching unrelated Node/Gateway processes.
- [ ] SPA fallback applies only to non-API browser routes. Add publish handling for prebuilt assets; test `dotnet publish` offline after frontend build. Run tests and commit.

### Task 6: Integrated verification and handoff

**Files:** Add frontend browser tests and docs/filomena-local-dashboard.md; extend settings/API/build tests where integration exposes missing coverage.

- [ ] Run the dashboard only with a temporary settings directory, isolated ephemeral loopback listener, response gate paused and fake/disconnected external transports. Never launch the production App configuration to test UI.
- [ ] Browser checks: open each page; edit/save prompt and token budget; reload and confirm persistence; invalid save reports field error; stale tab gets conflict; pause preserves inbound acceptance; resize/mobile layout and keyboard-only navigation; no secret/message content in diagnostic stream.
- [ ] Verify environment override badge, corrupted-config repair path, missing frontend dependencies and last-good build behavior. Verify protected arbiter text remains present after editable prompts change.
- [ ] Run `dotnet test Filomena.slnx -c Release --no-restore`, `dotnet build Filomena.slnx -c Release --no-restore`, frontend typecheck/test/build, browser suite, and `git diff --check`. Report exact counts and any skipped live checks.
- [ ] Document Node setup command, local URL, settings/backups/cache locations, build cache, restart-only settings, pause semantics and limits of local security. Do not claim a MCP health check proves provider delivery.
- [ ] Review diffs for identity changes, mixed snapshots, gate bypass, secret exposure, unbounded queues and accidental Gateway actions. Preserve all prior unrelated changes. Request Helena's approval before any standalone independent review.
- [ ] Commit scoped completed work locally only after verification. Hand off the local URL and launch instructions; Helena controls any production restart and live canary.

## Plan self-review

Coverage: persistence/recovery/precedence (1), snapshots/prompts/authorization (2),
local security/diagnostics/conversation discovery (3), all UI fields (4), startup
build/publish/fallback (5), full regression/browser/docs (6). No live services are
required for acceptance. Interface adapters preserve existing consumers; new runtime
types belong to Core and settings/web types belong to App, avoiding circular references.
