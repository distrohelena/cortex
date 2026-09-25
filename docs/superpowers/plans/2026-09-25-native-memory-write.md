# Native Memory Write Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. Execute inline; no subagents or independent review without Helena's approval.

**Goal:** Persist eligible native human inbound through the existing authorized Graphiti recorder, independently of conversational participation, with durable and honest ingestion status.

**Architecture:** The native host stages memory work before advancing its read position, then calls a narrowly scoped host-only plugin route. The route reuses message normalization, identity and the recorder; neither legacy hooks nor agent dispatch are reopened. A durable receipt distinguishes backend acceptance from observed persistence and authorization completion.

**Tech Stack:** Existing .NET host/test projects; TypeScript OpenClaw plugin; Vitest; existing Graphiti and SpiceDB clients; host-private atomic file stores following existing local-store conventions.

**Spec:** `docs/superpowers/specs/2026-09-25-native-memory-write-design.md` (approach approved in conversation; implementation and deployment are separate gates).

## Global Constraints

- Filomena remains offline. No active configuration edits, deployment, live writes, backfill, sends or service startup.
- No changes to brain, FSM, participation, generation, Boundary semantics or transport isolation.
- First slice covers human inbound only. Manual/shadow harnesses have no writer authority by default.
- Preserve SpiceDB authorization; unavailable/denied never becomes allow.
- Snapshot history is context, not new ingestion. Future activation requires an explicit start boundary.
- Same native identity and payload is idempotent; conflicting payload is rejected without overwrite.
- `queued` is not `persisted`. Stable UUID alone is not persistence evidence.
- Tests use synthetic data and injected backends. They prove plumbing, not extraction quality.
- Preserve unrelated worktree changes; commit only named slice files after verification. No push.

## Review Focus

1. Crash after backend acceptance but before local receipt: reconcile by exact episode identity; do not blindly resubmit uncertain writes.
2. Two accounts yielding the same legacy deterministic episode UUID: reject conflicting provenance rather than silently merge.
3. Namespace ownership change or revoked authorization during recovery: reauthorize before mutation or disclosure.
4. Large or malicious input, redirects and unauthenticated status reads: reject before recorder/backend invocation; never log content or credentials.
5. Silent conversational outcome and runtime failure: neither drops staged memory nor makes a memory retry execute the brain again.

## File Map

New plugin files:
- `plugins/openclaw-memory-graphiti/native-ingest.ts`: versioned validation, host binding and service.
- `plugins/openclaw-memory-graphiti/native-ingest-store.ts`: durable request/receipt state and ownership lock.
- `plugins/openclaw-memory-graphiti/native-ingest-http.ts`: authenticated, exact-match POST/GET routes.
- Matching `*.test.ts` files: service, crash and route proofs.

Existing plugin files touched narrowly:
- `message-lifecycle.ts`: reusable explicit authorized submission/confirmation API; retain legacy API behavior.
- `graphiti.ts`: exact receipt lookup using existing read contract; do not mistake `resolvedUuid` for completion.
- `index.ts`, `openclaw-plugin-sdk.d.ts`, `package.json`: opt-in route composition and packaging; SDK declaration must match the installed OpenClaw type.

New host files:
- `src/Filomena.App/NativeMemoryIngest.cs`: wire DTOs, trusted host binding, strict HTTP client.
- `src/Filomena.App/NativeMemoryOutbox.cs`: durable pending memory records, independent of execution/outbound journals.
- `tests/Filomena.Llm.Tests/NativeMemoryIngestTests.cs`: protocol and fail-closed tests.
- `tests/Filomena.Llm.Tests/NativeMemoryCompositionTests.cs`: host ordering, restart, silent outcomes and recall proof.

Existing host seam: `src/Filomena.App/NativeShadowCommand.cs`, just before `AcceptAsync(mapped, token)` and before `readPosition.Save`. Memory configuration is separate host-owned input, disabled by default; do not add authority to project JSON or alter Observation contracts.

## Task 1: Trusted request and durable plugin admission

**Interfaces:**
- `NativeIngestRequestV1`: `version: "native-memory-ingest:v1"`, `bindingRef`, `message: NormalizedInboundMessage` on the service boundary. HTTP timestamp is an ISO string decoded strictly to Date.
- `NativeIngestStatusV1`: `version`, `requestRef`, `episodeUuid`, `state: "pending" | "queued" | "persisted" | "rejected" | "failed"`, optional controlled `diagnostic` and `retryDisposition: "safe" | "reconcile" | "none"`.
- `NativeIngestService.submit(request): Promise<NativeIngestStatusV1>` and `getStatus(bindingRef, requestRef): Promise<NativeIngestStatusV1>`.
- Host binding is loaded locally, not accepted from request content: exact channel/account/conversation/kind/namespace, allowed canonical identity rules and start boundary. Resolve/validate actor structurally; display name is never authority.

- [ ] Add failing tests: `wrong_binding_never_calls_recorder`, `unresolved_actor_rejected`, `same_id_same_payload_reuses_receipt`, `same_id_changed_payload_rejected`, `cross_account_episode_collision_rejected`, `concurrent_submit_has_one_owner`. Assert backend count 0 for rejects and 1 for duplicate valid admission.
- [ ] Run `node node_modules/vitest/vitest.mjs run native-ingest.test.ts` from plugin directory; confirm failures are missing behavior, not setup errors.
- [ ] Implement strict validation and deterministic digest over immutable message provenance/content. Persist receipt atomically before submission; acquire single-writer ownership of the store and fail closed on contention/corruption. Keep message content only in private pending records, never normal diagnostics.
- [ ] Re-run tests, including reopen after interrupted atomic replacement and wrong-scope status lookup. Expected all pass and original receipt remains intact.
- [ ] Commit only this task's files after reviewing diff.

## Task 2: Reuse recorder with truthful completion

**Interfaces:**
- Recorder adds `submitInbound(message: NormalizedInboundMessage): Promise<{ capture: MessageCapture; state: "queued" }>` with typed failures; existing `recordInbound` stays compatible.
- `confirmInbound(capture: MessageCapture): Promise<"pending" | "persisted">` validates exact episode UUID, namespace and source provenance, then awaits required fragment authorization relationships before `persisted`.
- `NativeIngestService.reconcile(requestRef): Promise<NativeIngestStatusV1>` performs bounded single checks, not a busy polling loop. It never calls brain or sender.

- [ ] Add failing tests for queued-only receipt, missing episode, foreign episode, authorization denied/unavailable, fragment-relationship failure, episode present with zero facts, uncertain submission and restart reconciliation. Assert `state != persisted` until both backend evidence and authorization succeed.
- [ ] Run focused Vitest tests and confirm red.
- [ ] Refactor recorder minimally to expose these operations; keep extraction instructions unchanged. Existing `resolvedUuid` is an identity hint only. Use `getEpisodes` initially; absence from a bounded result is pending, not proof of failure or permission to resubmit. On uncertain submission preserve the work and require reconciliation; no automatic repeated extraction.
- [ ] Verify same UUID/content does not produce a second add call after restart. Verify permission revocation is checked again. Preserve existing lifecycle, passive-capture and outbound-capture tests.
- [ ] Commit scoped changes after green tests.

## Task 3: Narrow host-only route composition

**Interfaces:**
- `POST /plugins/memory-graphiti/native-ingest/v1` accepts the versioned request.
- `GET /plugins/memory-graphiti/native-ingest/v1/status?bindingRef=...&requestRef=...` returns only authorized status, not message content.
- `registerNativeIngestRoutes(api, service, hostAuthority): void` uses the existing `registerHttpRoute` API. Match exact paths; retain gateway authentication and require dedicated host-only capability from local host configuration. Do not register tools accessible to models.
- Route registration defaults OFF. Lack of authority or binding prevents registration; no automatic credential generation or active credential edits in this slice.

- [ ] Add route tests: missing/invalid capability, general gateway credential without host capability, wrong method, malformed body, body over 64 KiB, wrong binding/status scope. Assert recorder, model and sender invocation counts all zero for rejected input.
- [ ] Run route tests to establish red; inspect real installed `src/plugins/plugin-registration.types.ts` and `plugin-api.types.ts` before adding the matching narrow local SDK declaration.
- [ ] Implement exact routing, strict body/schema validation, constant-time capability comparison and no redirects. Authenticate before decoding content. No secrets/content in errors.
- [ ] Run service composition with fake Graphiti/SpiceDB and real recorder. Assert suppression ON still initializes locally without startup probes; ingestion is explicit and not triggered by status/startup. Test default OFF.
- [ ] Run plugin typecheck and unit tests; commit scoped files.

## Task 4: Durable native-host wiring, not a brain feature

**Interfaces:**
- `NativeMemoryIngestClient.SubmitAsync(NativeMemoryRequest request, CancellationToken token)` and `GetStatusAsync(string requestRef, CancellationToken token)` return `NativeMemoryReceipt` matching Task 1.
- `NativeMemoryOutbox.Stage(NativeMemoryRequest request)` returns durable requestRef; same identity/different digest throws.
- `NativeMemoryOutbox.ReconcileAsync(NativeMemoryIngestClient client, CancellationToken token)` only advances ingestion receipts. No execution or transport dependencies.
- Dedicated `NativeMemoryHostBinding` contains authenticated local endpoint, bindingRef, scope and explicit observation start boundary; absent binding means disabled, not silently operational.

- [ ] Add failing tests: eligible human event staged before read-cursor advance; silence still staged; self/wrong scope/snapshot/pre-boundary/manual-shadow not staged; crash reload preserves pending; backend outage does not lose work; memory retry invokes runtime/sender zero times.
- [ ] Run `dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --filter "FullyQualifiedName~NativeMemory"` with live credentials/opt-ins removed from the child test environment; confirm red.
- [ ] Implement private atomic outbox following existing store conventions, with single-writer guard. Stage after trusted event validation and before conversational acceptance. Staging failure stops cursor advancement; remote outage remains an explicit durable memory failure/pending state, not a reason to repeat already executed conversation.
- [ ] Wire only the native operational host with explicit memory opt-in. Preserve existing cursor, journal, brain and sender semantics. Add masked per-batch ingestion diagnostics; never report learning-ready when authorization is unavailable.
- [ ] Re-run tests including runtime failure after staging, duplicate event after restart and content conflict. Confirm duplicate handling across plugin and host boundaries; commit only scoped files.

## Task 5: Local end-to-end proof and operating instructions

**Interfaces:** Uses real host admission-to-outbox mapping, actual HTTP/service/recorder code and real `GraphitiMemoryReader`; injected backend/authorization replaces external services only.

- [ ] Add failing cross-component tests for eligible inbound -> queued -> observed episode -> persisted -> expected namespace/subject recall; wrong namespace/subject rejected by reader; successful episode without facts is a valid empty recall; backend exception is not empty.
- [ ] Add controlled extraction fixtures for explicit preference, joke, third-party allegation, correction and banal content. Verify evidence/subject plumbing; label these tests explicitly as controlled extraction, not proof of LLM selection quality.
- [ ] Run tests red, finish only missing integration wiring, then green. Assert legacy hook/agent/sender/model counters zero throughout. Record that a future real Graphiti ingestion will invoke backend extraction and needs a separate operational window.
- [ ] Run full plugin unit suite (`node node_modules/vitest/vitest.mjs run --exclude '**/*.e2e.test.ts'`) and `node node_modules/typescript/bin/tsc --noEmit`; inspect test configuration to ensure network cases remain opted out.
- [ ] Run fresh .NET build, focused memory/native-host suites and repository formatting checks on changed files. Use existing local commands; do not install dependencies or run live suites to satisfy the gate. Run `git diff --check` and audit scope.
- [ ] Update `docs/graphiti-write-gap-diagnosis-2026-09-25.md` with implemented evidence and remaining live prerequisites. Document host binding, private store location/retention, failure reconciliation, no automatic backfill, default OFF, rollback and queued-versus-persisted meanings.
- [ ] Self-review authority, crash windows, wrong-subject attribution, idempotency, cursor ordering and no legacy dispatch. No independent reviewer without explicit approval. Commit only slice files after green verification.

## Completion / Future Operational Gate

Local completion means the isolated pipeline passes, not that Filomena is learning live. Report test evidence, remaining authorization/backend readiness (SpiceDB was unavailable), and any untested semantic extraction behavior. Live activation, service startup, model-backed extraction and historical backfill remain separate; do not reactivate Filomena as part of this plan.

## Plan Self-Review

The five review risks are assigned to Tasks 1–4; round-trip and selection coverage are in Task 5. Existing deterministic IDs are retained with collision detection rather than changed silently. Finite episode lookup cannot establish loss, so uncertain work remains pending/reconcile. The plan intentionally does not promise exactly-once remote extraction or semantic quality from mocked outputs.

## Execution result (2026-09-25)

Local implementation and verification complete; no activation/deploy. See
`../../native-memory-ingestion.md` for test counts, changes from this plan and
remaining operational/semantic validation. Notable justified changes: native SQLite
receipts instead of hand-rolled file locking; one cancellable host maintenance task;
full-consistency permission checks without auto-granting membership; explicit POST
receipt reconciliation. Controlled source cases verify attribution/plumbing, not
model selection quality. Existing unrelated worktree edits were preserved.
