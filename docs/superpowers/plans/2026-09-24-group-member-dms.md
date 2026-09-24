# Group-member DMs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. No subagents or independent review without the user's separate approval.

**Goal:** Let current members of the authorized group initiate DMs with Filomena while the group keeps operating, with private per-conversation state.

**Architecture:** Extend the existing native account owner to manage exact conversation scopes and membership evidence. Reuse the existing .NET native live composition per scope under one supervisor; do not create another transport connection or brain.

**Tech Stack:** TypeScript, native WhatsApp provider, SQLite, Vitest, C#/.NET, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-24-group-member-dms-design.md` (approved).

## Workspaces

- `F`: current Filomena worktree, `feature/typed-flow-slice42`.
- `O`: sibling OpenClaw worktree, `feature/native-observation-v1`.
- Resolve both through the existing checkout locations, not the active installation.
- Read scoped AGENTS files before modifying their directories. Preserve unrelated untracked reports and private files.

## Global Constraints

- Responsive DM access only; no unsolicited private messages or stranger access.
- Membership comes from the native account, not names, text, project JSON or models.
- Preserve project/models, participation/FSM, generation, enforcement and memory semantics.
- No Graphiti writes or artificial canaries. Keep empty distinct from outage.
- Startup capture OFF; explicit host authorization only; raw evidence remains visible.
- Per-conversation inbox, cursor, outbound journal, history, state and memory namespace.
- Never automatically retry uncertain sends or restore an older cursor after delivery.
- No active changes until local tests, review, build and migration preparation pass.
- No push. Stage only named source/test/docs files, never operational credentials.

## Review Focus

1. A member removed during generation must not receive the pending unsent response (tasks 2, 4).
2. PN/LID aliases arriving concurrently must not create two DMs or cross-bind memory (tasks 2, 5).
3. Failure while initializing the first DM must not drop its inbound or release it to the legacy agent (tasks 1, 3).
4. Reconnect must not renew stale membership or approve a changed config/route (tasks 2, 4).
5. A new DM without a Person/canary must neither borrow another session's memory nor bypass readiness (task 5).

## Execution protocol

For every task: write the specified regression first, run it and record the expected failure, implement the owner-level change, rerun the focused tests, then the relevant broader suite. Record commands/results in an execution ledger beside this plan. Commit only green, coherent slices. Do not edit a native test/source file while its Vitest process is running.

Use the repository's installed wrappers without dependency reconciliation. Native focused command pattern: `node scripts/run-vitest.mjs <test-path>`. .NET focused pattern: `dotnet test <test-project> --filter FullyQualifiedName~<test-class>`. Read actual exits and summaries; do not infer success from compilation or individual passed tests.

## Task 1: Multiple exact scopes in the existing native owner

**Files (O):** `src/channels/message/native-inbound-isolation.ts`, `native-inbound-isolation.test.ts`, `native-observation-bootstrap.ts`, `native-observation-bootstrap.test.ts`, `src/gateway/observation-startup.ts`, `observation-startup.test.ts`.

**Interfaces:** retain the existing single-scope entrypoints. Introduce a host-only account owner with `protect(session)` and `close()`; `protect` returns the existing per-session isolation control. Receiver binding attaches to this owner before intake; subsequent trusted enrollments are owner operations, not replacement policies retrofitted into old bindings. Owner replacement invalidates proof, while draining receivers retain denial.

- [ ] Add a real-owner regression installing a group and two DMs on the same account, approving each against R1, and checking independence:

```ts
expect(groupProof.sessionRef).not.toBe(dmProof.sessionRef);
expect(binding.blocks(group)).toBe(true);
expect(binding.blocks(dm)).toBe(true);
expect(binding.blocks(stranger)).toBe(false);
```

- [ ] Verify RED on the current one-policy-per-account/one-bootstrap-per-store restriction. Add release/replacement, wrong account, R1-to-R2, forged binding and recovery tests.
- [ ] Change ownership granularity, not journal scope/schema. Bind inspection to the selected session. Reject duplicate exact scope ownership; closing one reader must leave other readers and capture states unchanged.
- [ ] Extend startup composition to enumerate configured exact scopes while preserving the single-session file format. Startup all OFF; partial startup failure cleans up only newly acquired resources.
- [ ] Run native isolation, exclusion, recovery, bootstrap, journal and gateway-startup suites; prove two armed scopes admit atomically and cannot exchange cursors. Commit the green native multi-scope slice.

## Task 2: Trusted group-member authorization

**Files (O):** `extensions/whatsapp/src/inbound/group-metadata-cache.ts`, its existing tests, new `group-member-dm-policy.ts` and `.test.ts`, `message-delivery.ts`, `src/channels/message/native-observation-owner.ts` and the existing SDK contract that exposes its receiver binding.

**Interfaces:** plugin-owned policy provides `resolveDm(peerJid)` with a discriminated result: `allowed` carrying canonical PN, exact native aliases and a live membership lease; `denied`; or `unavailable` with a controlled reason. The lease has a synchronous `assertCurrent()` owned by the provider generation. It is never reconstructed from HTTP fields or model output.

- [ ] Add failing tests using the real metadata owner and a fake provider: member PN/LID allowed; stranger denied; self denied; missing identity and conflicting aliases unavailable; wrong account/group denied.
- [ ] Add race tests: block metadata hydration, emit a participant update, then release hydration; old evidence must not become current. Reconnect, cache expiry and close revoke old leases. A missing roster must not become an empty successful roster.
- [ ] Add a membership-specific projection instead of authorizing from the display/mention list. Reuse existing native alias resolution and invalidation. Validate provider data before minting the lease.
- [ ] Attach the narrow provider-owned membership operation to the existing registered account owner. No broad Gateway allowlist change and no public model tool. If the SDK export budget actually grows, report the exact required delta before changing its guard.
- [ ] Verify all policy and metadata-cache regressions, including foreign group/account and removed-member assertions. Commit the green membership slice.

## Task 3: First-DM admission and discoverable session enrollment

**Files (O):** `extensions/whatsapp/src/inbound/message-delivery.ts`, `durable-receive.ts`, their native isolation/replay tests, `src/channels/message/native-observation-bootstrap.ts`, `native-observation-reader.ts` and their tests.

**Interfaces:** account owner `ensureDm(peer, membershipLease)` serializes enrollment for the canonical peer and returns the exact session descriptor only after isolation/capture readiness. Descriptor includes opaque session reference, native route binding, canonical actor, epoch and initial cursor. Authenticated host discovery lists only enrolled scopes; read credentials cannot authorize capture or broaden scope.

- [ ] Add boundary tests with two simultaneous first messages for one peer, one message for a second peer and a group message. Assert one enrollment per canonical DM, no legacy callbacks and no lost native IDs.
- [ ] Inject readiness failure and process restart before first admission completes. Persist/recover pending work through the existing durable inbound owner; do not mark it completed or rely solely on an in-memory promise. Assert zero legacy dispatch and zero duplicate observation records.
- [ ] Place membership/enrollment before approval/pairing and legacy normalization paths. Preserve the initial native ID and atomic final admission. If current queue phases cannot express a durable pending admission, record and implement the smallest owner-level phase extension; do not change journal semantics or treat historical backlog as new input.
- [ ] Keep the epoch's initial cursor before the first newly captured DM so supervisor startup cannot discard that first event as initial context. Require explicit same-epoch continuation with the durable inbox registry.
- [ ] Expose authenticated discovery/status and explicit pending/failure diagnostics. Test reader/control credential separation, unknown version rejection, scope substitution and partial enrollment cleanup.
- [ ] Run normal/replay/retry/recovery and atomic journal suites. Commit the green enrollment slice.

## Task 4: Membership fence at the real send boundary

**Files (O):** `extensions/whatsapp/src/inbound/bound-text-send.ts`, `.test.ts`, and its actual native caller; account owner from task 2. **Files (F):** `src/Filomena.OpenClaw/Mcp/OpenClawBoundMcpTransport.cs` and tests only if the wire contract needs an additive explicit authority identifier.

**Interfaces:** group-member DM sends redeem the native current membership/scope lease inside the existing synchronous `assertAuthorized` boundary. The native sender already calls this fence after awaited readiness and immediately before `socket.sendMessage`; preserve that ordering.

- [ ] Add a test delaying `assertReady`, removing the member, then completing readiness:

```ts
await expect(pendingSend).rejects.toThrow();
expect(socket.sendMessage).not.toHaveBeenCalled();
```

- [ ] Add counterproof: unchanged live member succeeds once. Stale lease, wrong recipient/account, disconnected owner, changed gate/config and duplicate attempt fail. Group delivery retains its existing authorization.
- [ ] Compose membership with, never instead of, route/isolation/send-attempt guards. No await between final validation and provider submission. Do not turn rejection or uncertain delivery into an automatic retry.
- [ ] Run all bound-sender/MCP transport tests and native owner regressions. Commit the green physical-send fence.

## Task 5: Existing .NET runtime per authorized DM

**Files (F):** `src/Filomena.App/NativeLiveCommand.cs`, `NativeShadowCommand.cs`, `NativeObservationHostBinding.cs`, `NativeLiveReadCursorStore.cs`; new `NativeConversationSupervisor.cs`; observation adapter; `src/Filomena.Memory/ShadowGraphitiProbe.cs`; matching App/OpenClaw/Memory tests.

**Interfaces:** supervisor accepts host-pinned group/account/project and authenticated native session discovery. It owns one live session task per canonical conversation and supplies the existing per-session composition with an exact route, private directory and membership revalidation. Add explicit continuous-DM host/hashed evidence support; never reinterpret `ContinuousGroup` as unrestricted authority.

- [ ] Add authorization tests mirroring `NativeLiveCommandTests`: host flag, CLI authorization and hashed evidence must agree for continuous DM; existing bounded DM/group options remain unchanged.
- [ ] Add supervisor tests with two DMs and a group. Assert distinct histories, state, cursors, outbound journals and namespace; concurrent aliases resolve to one session. A failed DM does not cancel the group. Shutdown drains each independently.
- [ ] Refactor the existing per-session composition just enough to be called by the supervisor. Persist/read continuation using each exact session's current epoch. Supply one serialized poll/admission path per session; no competing manual loop.
- [ ] Add membership rechecks before execution and within the existing transport pre-send callback. The native task-4 fence remains the final authority; an HTTP check alone does not close the race.
- [ ] Add a discriminated memory-readiness mode for a new namespace, separate from the established canary mode. Reuse `GraphitiResponseValidation.Facts(..., expectedParticipant)` to validate `entity-neighborhood-absence:v1`. It proves only the exact subject's absence, not arbitrary namespace emptiness. Require validated Boundary/social recall and namespace evidence too; insufficient backend provenance leaves that DM not ready.
- [ ] Tests: exact scoped absence accepted only in the new mode; missing metadata, wrong subject/namespace, error and timeout rejected; a positive foreign canary remains a hard failure. Established-DM canary tests unchanged. No data writes or fake Person creation.
- [ ] Run Core, OpenClaw and Llm local suites with live experiments disabled, build and diff checks. Commit the green .NET composition.

## Task 6: Full local composition and controlled migration

**Files:** new focused multi-conversation integration tests in both worktrees; `F/docs/group-member-dms.md`; an operator script beside the existing deployment scripts, following their private-backup and graceful-stop procedure.

- [ ] Start the compiled native host in isolated state with fake WhatsApp transport, plus real .NET session composition with test model/Graphiti boundaries. Drive group, DM A, DM B and stranger messages; assert expected route-specific replies, no unsolicited sends, no legacy run, and private memory requests.
- [ ] Repeat with member removal during generation, reconnect, process restart, duplicate input, enrollment failure and indeterminate send. Assert preserved pending input, no replayed successful send and group continuity.
- [ ] Run relevant native regression suites, core/extension typechecks, lint, both builds, compiled smoke and .NET suites. Review diff for authority widening, stale proof, cross-session stores and credentials.
- [ ] Prepare private rollback of only changed active artifacts/config. Record hashes and current checkpoints. Do not copy old journals over current journals.
- [ ] Migrate only after all local gates pass: graceful idle stop, deploy, startup capture OFF, readiness, exact-account/member proof, per-scope dispatch inactive, explicit arm, restore group checkpoint, start supervisor.
- [ ] Ask a group member for one real DM if needed. Do not manufacture or send an unsolicited canary. Declare DM live only after native inbound, one runtime execution and provider-confirmed correct-DM reply are evidenced. Otherwise report the precise remaining operational requirement.
- [ ] Keep the authorized service running only when healthy. On failure, drain and follow rollback while retaining newest journals and uncertain-send evidence. No memory writes, credential changes or push.

## Review and handoff

Written design approved. Implementation plan awaits review. Recommend inline execution by the current agent because the tasks depend on the same ownership/proof contracts; no substantial multi-agent workflow is needed. Any independent reviewer still requires separate user approval.

This plan does not claim DMs enabled. Local proof and live proof are distinct delivery gates.
