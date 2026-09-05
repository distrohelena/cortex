# Filomena local dashboard

Status: superseded by 2026-09-05-filomena-flow-engine-design.md. Historical reference only; do not implement as the current product specification.
Date: 2026-09-05.

## Scope and existing implementation

Add a Vue/TypeScript administration UI served by Filomena.App itself, locally.
Do not create an independent frontend server or a separate social-memory package.
The app already hosts Kestrel in InboundBridgeHttpServer, alongside Generic Host
background services. Preserve the existing inbound route and its acceptance semantics.

Current options are bound once and injected into singleton services. Editing JSON
alone would therefore not implement live settings. Generation prompts already live
in FilomenaAgentOptions; arbitration prompts are constants in ArbiterPrompt.
The dashboard must show effective defaults, including options absent from appsettings.

Do not change Gateway ownership, channel login, Graphiti Core, namespaces, UUIDs,
memory isolation, model selection, or workspace/DREAMS.md. Graphiti is not currently
orchestrated by C#: do not present plugin settings as controls that this app applies.

## Hosting and frontend build

- Default origin: http://127.0.0.1:5147. Never bind wildcard or LAN addresses.
- Serve the SPA at /, administration API at /api/admin, and preserve
  /v1/openclaw/events/inbound. SPA fallback must not swallow API/bridge errors.
- Keep the web listener in the same executable/process; share the app's settings,
  status, diagnostics and receiver instances, not duplicate service singletons.
- Frontend sources: frontend/, Vue + TypeScript + Vite. No permanent Vite process.
- At normal app startup, run the frontend build if its source/lockfile/build-settings
  fingerprint changed. Publish successful assets atomically into a writable cache
  outside bin. Serve immutable completed builds, never a partially written bundle.
- A short-lived Node/npm child process is needed for a source build; this does not
  become another web server. Execute fixed build commands using argument lists,
  not shell text assembled from HTTP input. Use a lockfile and bounded startup build.
- Install dependencies explicitly during project setup; startup does not silently
  run npm install or download packages. Missing dependencies produce a clear error.
- Run build preparation in a background service so observation is not blocked by
  npm. Until ready, serve the last successful build; without one, serve a minimal
  local build-status page and keep the backend/bridge available.
- Cancel the child build process tree during app shutdown. Serialize builds and
  prevent two app instances from corrupting the cache. No build-on-every-request.
- A published deployment may include prebuilt assets and run without Node or source.
  Same executable hosting does not mean rewriting embedded resources at runtime.

## UI

Portuguese UI, clear labels/units, responsive layout and keyboard-accessible forms.
Initial navigation:

1. Overview: app mode, response pause, MCP state, last model-call outcome, UI build
   status, pending restart changes, current configuration revision.
2. Conversations: discovered native IDs, display names, namespaces, response
   authorization, DM allowlist and observation-only status. Names never identify
   a conversation. Discover through admitted events and read-only MCP listing.
3. Model: endpoint/model, independent arbitration/generation tokens and temperatures,
   request timeout. Changing model text never automatically loads a second model.
4. Behavior: debounce, burst caps, wait/recheck/follow-up timing and context budgets.
5. Prompts: core personality, DM, group, follow-up, arbiter DM/group; explicit save,
   preview of editable text and protected contract, restore defaults with confirmation.
6. Diagnostics: bounded live stage events, durations, validation errors and settings
   revisions. No prompts, reasoning, conversation bodies, credentials or raw dumps.
7. Advanced settings: mode, MCP command/arguments and timeouts, probe targets,
   local listener/bridge routing and frontend build configuration. Label inactive
   probe-only fields and restart-required settings honestly.

An advanced typed view covers every supported app option; no arbitrary-file editor,
shell console, credentials browser, or editor for the entire OpenClaw configuration.
Keep overlapping context limits distinct and explain which stage each one limits.

## Settings ownership and persistence

Introduce a versioned, typed settings document and a single SettingsStore.
Use %LOCALAPPDATA%/Filomena/settings.json by default, separate from assets and bin.
The location is a bootstrap option, not movable by an in-process web update.

On first use, seed from the existing effective configuration, including code defaults,
without modifying appsettings.json. Preserve the current allowlist exactly:

- whatsapp-dm-5511944558218-s-whatsapp-net
- whatsapp-group-120363413037344295-g-us (Grupo Teste)

Drift and Plex stay observation-only. Do not seed global/group wildcard permission.

Separate shipped defaults, persistent desired settings and effective runtime settings.
CLI/environment overrides have explicit precedence and are labeled as externally
overridden in the UI; saving must not claim such an override changed the runtime.
Whole arrays replace arrays, avoiding indexed configuration merge surprises.

Every update:

1. Require the revision the browser edited (optimistic concurrency).
2. Validate the entire candidate document server-side, including cross-field ranges,
   finite numeric values, known modes, loopback-only bindings and canonical identities.
3. Write a temporary file in the same directory, flush, then atomically replace with
   a bounded previous-version backup. Never truncate the live settings file first.
4. Publish an immutable validated snapshot only after successful persistence.
5. Return desired/effective revisions and a list of restart-required fields.

Return field-level validation errors without changing the previous state. Reject stale
edits with HTTP 409. On corrupt persisted JSON, preserve it, report recovery status
and use a valid backup; without a valid backup, fail closed for outbound responses
while keeping the local repair UI available. Do not silently grant default access.

## Applying changes safely

| Setting class | Application point |
| --- | --- |
| Behavioral prompts, token/temperature budgets, context and scheduling limits | Next conversation turn/burst snapshot |
| Pause responses or revoke conversation permission | Authoritative gate immediately before dispatch |
| Grant conversation permission or resume responses | Future turns; never replay old suppressed responses |
| Listener port/path, MCP process command/arguments, app mode, endpoint/client lifecycle and build paths | Saved as restart-required; user restarts App |

Snapshot settings when a conversation starts a new burst and carry its revision
through debounce, rechecks, arbitration and generation. Messages joining that burst
use the same snapshot. Do not change timers mid-wait or mix prompts from two revisions.
The next burst uses the latest snapshot, without resetting per-conversation histories.

Response pause is a separate authoritative gate: observation continues; avoid starting
new model work while paused. Check pause/authorization again immediately before the
transport dispatch, including after slow preflight. A message already handed to the
provider cannot be recalled. On resume, do not send accumulated stale drafts.

The immutable actor/reference/schema contract stays in code and is appended to the
editable arbitration instructions. The dashboard does not expose switches to disable
identity validation, guess addressees or perform cross-conversation recall.

## Local security

Loopback binding is required but not sufficient. Validate Host against the configured
local authority and reject foreign browser origins. Disable cross-origin access.
Mutations require JSON and a per-process anti-CSRF token obtained through a same-origin
bootstrap endpoint, with strict Origin checking. Do not put tokens in URLs or logs.
Use no-store for bootstrap/config APIs and a restrictive CSP for the SPA. Scope browser
admin protections separately from the existing machine-to-machine bridge contract.

Apply request-size limits, reject path traversal and unsafe file paths, bound prompt
sizes and diagnostic retention. Never return tokens or command-argument secrets in
diagnostics. This is protection from other websites, not from malicious local software
running as the same Windows user. No LAN access, public hosting or account system.

## API and diagnostics

Minimal endpoints: bootstrap, settings/schema, settings GET/PUT, pause/resume,
conversations, health and read-only diagnostic SSE stream with bounded recent history.
Use validation/problem responses and revisions rather than untyped JSON passthrough.
No endpoint starts/stops/relinks the Gateway or sends arbitrary test messages.

Health distinguishes connected MCP, last successful preflight, provider status unknown,
model idle/last-call success/failure, and frontend build state. Do not equate tools/list
success with WhatsApp delivery readiness or idle LM Studio with failure.

Structured diagnostic events carry timestamps, elapsed milliseconds, stage/call IDs,
conversation routing identity where known, settings revision and safe error codes.
Keep the console output working. Use a bounded ring buffer; a slow browser must not
block observation or sending. SSE disconnect/reconnect must release resources.

## Expected code changes

- frontend/: UI, API client, typed forms, build scripts and frontend tests.
- Filomena.App: web-host composition, admin routes, settings store, build service,
  status/diagnostic stream, typed settings mapping and runtime snapshot wiring.
- Filomena.Core: per-turn settings snapshot plumbing and response permission gate,
  preserving conversation isolation and existing timing semantics.
- Filomena.Llm: configurable behavioral arbitration prompts and per-request options;
  keep protected schema/ref validation intact.
- Filomena.OpenClaw: lifecycle-safe configuration boundaries, existing shared sender,
  and a final permission gate without adding provider-aware scheduling.
- csproj/build configuration: source build/publish asset handling, no new server app.
- Tests and docs: user setup, Node prerequisite, settings location/recovery, restart
  semantics, and inherited operational restrictions.

Do not introduce an independent package or new database just for this dashboard.

## Delivery sequence and acceptance

1. Configuration store/snapshots and regression tests before UI mutations exist.
2. Local admin API/security and integration tests with fake transport/model.
3. Vue forms and diagnostics, build/cache integration in the same executable.
4. Runtime application/pause behavior, recovery tests and browser verification.

Acceptance tests cover:

- All existing .NET/golden identity tests continue passing.
- First-run settings match current effective values and exact response allowlist.
- Save/restart/rebuild preserves edits; invalid edits and I/O failures preserve prior
  settings; concurrent stale revisions cannot overwrite each other.
- Snapshot revisions remain consistent during an in-flight generation and recheck;
  different conversations remain independent.
- Pause/revoke during generation or preflight prevents a not-yet-dispatched response;
  inbound observation continues and resume does not replay old drafts.
- Both DM/group arbiter prompts are editable, but schema/identity invariants cannot
  be removed through the UI.
- Every supported option has typed UI coverage, effective value and application mode.
- Foreign Origin/Host and missing mutation token are rejected; secrets absent from
  responses/diagnostics; static fallback never catches API or bridge routes.
- Changed source triggers build; unchanged source reuses it; missing Node/build failure
  retains last good assets and exposes an actionable state; shutdown cancels build.
- Diagnostics timings, reconnection, bounded retention and slow-client handling.
- Vue typecheck/build/component tests; backend tests/build; git diff --check.
- Browser tests run against an isolated configuration with fake/disconnected transports,
  never against live send routes. No Gateway lifecycle changes or live model calls.

No push. Commit only scoped artifacts; preserve the existing dirty worktree.
