# Typed conversational flow slice 2 — implementation plan

> **For agentic workers:** Use subagent-driven-development with disjoint file ownership, TDD, and verification-before-completion. The user approved the detailed slice-2 specification and three parallel implementers. One local integration commit after verification; no push.

**Goal:** Execute the existing typed graph per conversation with ephemeral state, real batches, bounded history snapshots, and explicit state transitions, using fake ingress/completion only.

**Architecture:** Keep graph v3 and project v1. Existing `initialStateId` is the persisted default-state ID (do not add a competing default field). Session keys include runtime/character identity, channel, account, and native conversation identity. Per-session admission/batching must remain available during awaited work; only continuations are serialized. Snapshot project/state at batch admission, never share mutable execution outputs. History uses structural admission sequence watermarks, not timestamp/text dedupe.

**Tech stack:** .NET 10, existing FlowType/TypedFlowCompiler/TypedFlowRunner, TimeProvider, xUnit, existing JS editor and reusable TypeScript canvas.

## Shared wire contracts

- Existing entry kinds `dm` and `group`, output `message`, execution output `then`.
- `accumulate`: input `message`, output `messages`, execution input `exec`, output `then`. Settings preserve existing names: `quietSeconds`, `maxMessages`, `maxWaitSeconds`; null disables a limit; at least one positive limit required.
- `history`: output `messages`, execution input `exec`, output `then`; settings `maxMessages`, nullable `maxAgeMinutes`, `maxCharacters`, `includeCharacterMessages`, `mode` (`before-current-batch` default, `through-current-batch` bounded by snapshot).
- `exit`: terminal execution input `exec`; `settings.mode = switch` only. Configured `targetStateId` OR enum input `state` with `settings.stateMapping` object mapping exact enum value to stable state ID. No name lookup. Carry is invalid executable configuration; preserve drafts.
- Store original message identity/timestamp/direction in runtime envelope; reuse FlowDomain.Message for graph value. Do not expose raw transport metadata to models.
- Project save accepts invalid executable defaults/targets and disabled limits as drafts; executable project compiler rejects them.

## Ownership and steps

### 1. Runtime / Sol

Files: `src/Filomena.Core/Flows/TypedFlowCompiler.cs`, `TypedFlowRunner.cs`, new `Flows/Sessions/*.cs`; new focused runtime unit tests only.

- [ ] Write failing batch/state tests, then define project snapshots, session key/store, message envelopes, batch provenance and trace contracts. Publish API signatures to integrator/test worker before expanding implementation.
- [ ] Implement project executable validation including default/targets, entry selection, enum mappings, and slice-1 compatibility. Multiple visual entries may exist; select by incoming conversation kind without duplicating core.
- [ ] Implement node/session/version-keyed batching with TimeProvider timers, one immutable release and a serial continuation queue per session. Release first threshold once; later arrivals form a separate batch.
- [ ] Integrate history and terminal transition through the typed runner (no textual compiler). Update state/version/enteredAt only on successful terminal execution. Bound history to current batch watermarks; keep message identity and original provenance outside graph design.
- [ ] Reject unsupported carry/send/Graphiti/etc.; fail execution without partial transition; expose structural node/batch trace with no message text or prompts.
- [ ] Test cancellation, duplicates, timer cancellation, concurrent sessions, administrative snapshot updates, and restart default reset.

### 2. Editor / Terra

Files: `src/Filomena.App/Playground/flow-model.mjs`, `flow-contract.mjs`, `flow.js`, `flow.html`, `flow-typed-panel.mjs` if needed, `PlaygroundProjectStore.cs`; new `tests/frontend/flow-session.test.mjs`.

- [ ] Add failing model tests for new node ports/settings, draft roundtrip and enum transition mappings.
- [ ] Add compact history node and accumulator summaries/panels, preserve nullable limits and all invalid wires/configs in drafts.
- [ ] Add explicit enum-to-state-ID mapping editor for exit; disable carry execution without silently deleting old draft values.
- [ ] Show editing state as design-time label; preserve project JSON without runtime values. Keep existing initial/default ID representation.
- [ ] Run focused node/editor tests and preserve slice-1 dynamic port compatibility.

### 3. Acceptance / Terra

Files: new `tests/Filomena.Core.Tests/Flows/SessionSliceAcceptanceTests.cs`, helpers alongside; `tests/fixtures/typed-flow-slice2.json`; `docs/typed-flow-slice2.md`. Coordinate runtime API with Sol; no production ownership.

- [ ] Build real fixture with separate inativa/atenta/engajada stable state IDs, DM/group entries, Acumular, Histórico, named typed fake Instrução, Switch, enum-mapped Trocar Estado.
- [ ] Add tests covering Drift/Plex/DM isolation and next graph selection, restart, fake-clock quiet/maxCount/maxWait/first-wins, one continuation per batch, per-node buffers, history exclusion and frozen watermark, async session serialization and cross-session progress.
- [ ] Verify trace metadata does not contain message text/prompts; identity-based duplicates never rely on text.
- [ ] Document fixture import and fake ingress API steps, actual limits and future gaps. Do not invent test outcomes.

### 4. Integrator / main

Files: new `src/Filomena.App/Playground/SessionFlowEndpoints.cs`, `TypedFlowEndpoints.cs` only to register; new `tests/Filomena.Llm.Tests/SessionFlowIntegrationTests.cs` and fixture csproj inclusion if needed.

- [ ] Establish clean committed-baseline Core/LLM/JS tests in isolated worktree; list preexisting failures without fixing unrelated code.
- [ ] Write fake local API integration tests first, implement configurable fake session harness with project validation, receive, inspect outcomes/state/trace and graceful cleanup. Never call external services or persist runtime in project store.
- [ ] Exercise fixture through real temporary playground HTTP host and project save/reload/import/export roundtrip. Do not replace user's saved project or restart their host.
- [ ] Review implementation against all 35 specification sections, then correctness/quality. No independent review session without separate user approval.
- [ ] Run `dotnet test tests/Filomena.Core.Tests`, relevant LLM/backend tests, frontend tests, App build, scoped `dotnet format --verify-no-changes`, and `git diff --check`.
- [ ] Stage only slice-2 changes, verify staged diff, commit locally, integrate into original checkout preserving all preexisting changes. Report precise tests, known failures, commit and remaining slice-3 gaps.

## Approved completion rule (2026-09-07)

Operational state transitions affect routing only at batch boundaries. An already-open batch retains state/graph/version and accepts subsequent messages until one of its own thresholds closes it, even when the session store already reports a newer state. The next batch uses the current state. Capture both batch state/version and current session state/version in structural trace. Do not extend this rule to administrative project replacement. Add a count-only three-message regression covering an intervening transition, exactly one old continuation, preserved stateVersion, immediate store visibility and next-batch selection.

## Non-goals

No Graphiti, live transport, external LLM, sending, personality prompts, persistent runtime state, auto-adaptation, state timers, carry, loops/fan-out/join/subgraphs, or changes to `workspace/DREAMS.md` and personal configuration.

## Delivery checkpoint

Runtime, editor, fake HTTP ingress and acceptance fixture are implemented. Final executed verification is recorded in `docs/typed-flow-slice2-verification.md`; the task checklists above are the original plan. Delivery is a single local commit on `feature/typed-flow-slice2` in the isolated worktree. The original dirty checkout is preserved; applying/merging this commit there is a separate operation, not an implicit discard or staging of preexisting work.
