# Slice 2 — verification ledger

## Baseline before implementation

Isolated worktree: `feature/typed-flow-slice2`, parent `a429170`.
The original checkout and its uncommitted transport/personal work were not copied into this baseline.

Executed 2026-09-07:

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --verbosity quiet
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --verbosity quiet
node scripts/build-flow-graph.mjs
node --test packages/flow-graph/tests/*.test.mjs tests/frontend/*.test.mjs
dotnet test tests/Filomena.OpenClaw.Tests/Filomena.OpenClaw.Tests.csproj --verbosity quiet
```

- Core: 146 passed.
- LLM/backend: 28 passed.
- JS/canvas: 59 passed.
- OpenClaw: 41 passed, one preexisting failure:
  `OpenClawInboundBridgeReceiverTests.InvalidActorIdentityFailsClosedInsteadOfThrowing`.
  This is the same existing failure documented in `typed-flow-commit-audit.md`.
- Initial sandbox denied `obj` writes. Re-running authorized builds outside that sandbox restriction resolved this environmental failure.
- Isolated worktree initially had no TypeScript dependency/build outputs. Reused installed TypeScript in ignored `packages/flow-graph/node_modules`, then rebuilt assets. No dependency/version or global SDK changes.

## Red evidence

Before endpoint implementation, both `SessionFlowIntegrationTests` failed as intended:

- fake session creation returned 404 (endpoint absent);
- invalid-default draft save returned 400 (old store conflated some executable checks with design storage).

No live transport, external model, Graphiti, personal project replacement or Gateway operation was used.

## Integration checkpoint (not completion)

Executed against the isolated worktree on 2026-09-07:

- Core: latest complete run 176 passed, including stale timer, premature quiet callback, reentrant disposal, redacted trace errors, history watermark and disconnected invalid graph regressions. An earlier full run failed `StateSpecificAccumulatorBuffersDoNotMixAcrossATransition`; isolated replay and subsequent full repetitions passed. No speculative production fix was made from that failure alone.
- LLM/backend: 32 passed, including four real loopback HTTP tests using fake completions and temporary project storage. They cover session ingress, duplicate delivery, transitions, envelope identity rejection, draft validation, project save/load and host restart.
- JS/editor/canvas: 68 passed, including exact import/export round-trips for both typed-flow fixtures.
- `dotnet build Filomena.slnx --verbosity quiet`: passed, zero warnings/errors.
- Backend/persistence scoped `dotnet format --verify-no-changes`: passed.
- `git diff --check`: passed (only existing CRLF conversion notices).
- OpenClaw regression: 41 passed / the same one preexisting failure listed above. No new transport changes.

Completion is pending the explicit decision for an incomplete count-only batch when another execution changes the state or the project is republished. The implementation does not silently flush, carry, discard or migrate that batch. Final runtime safety regressions and final verification will be recorded before a commit. No commit/push has been made at this checkpoint.

## Final verification after the approved batch-boundary rule

The user approved operational state changes taking effect at batch boundaries, explicitly excluding administrative project replacement. This supersedes the pending operational-state decision above.

Executed on the stabilized tree:

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --verbosity quiet
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --verbosity quiet
node scripts/build-flow-graph.mjs
node --test packages/flow-graph/tests/*.test.mjs tests/frontend/*.test.mjs
dotnet build Filomena.slnx --verbosity quiet
$sliceCsFiles = @(git diff --name-only -- '*.cs') + @(git ls-files --others --exclude-standard -- '*.cs')
dotnet format Filomena.slnx --no-restore --verify-no-changes --include $sliceCsFiles --verbosity quiet
git diff --check
```

- Core: **179/179**.
- LLM/backend: **32/32**, including real temporary loopback HTTP host, fake model, project save/load and restart.
- JS/editor/canvas: **68/68**, including exact slice-1/slice-2 JSON round-trips.
- Solution build: **0 warnings / 0 errors**.
- Formatting across all changed/new C# files: passed.
- Diff whitespace check: passed.
- OpenClaw rerun: **41 passed / 1 same preexisting failure**, `InvalidActorIdentityFailsClosedInsteadOfThrowing`. No transport edits or live services.

The count-only regression first failed (expected three executions, obtained two). It now proves an Inativa/version-0 batch completes after the store already reports Atenta/version-1; messages enter exactly once, there is one old continuation, and the next batch uses Atenta. Another test proves missing entry on the new state cannot strand the old batch. Administrative publication stays separate. The HTTP trace regression first failed because the new keys were absent and now verifies both batch/current state pairs.

The earlier watermark test used the superseded operational routing assumption; it was rewritten to use two administrative publications so it continues to prove watermark isolation without contradicting batch pinning. Final full Core is green after edits stopped.

Read-only integration review of buffer selection, timer identity, unique continuation and trace capture found no important issue. No independent-review session was launched.

## Remaining boundaries

- Project schema remains **1**, graph schema **3**; `initialStateId` is the existing default field.
- One accumulator directly after the selected entry in this slice; no arbitrary multiple accumulation stages.
- Runtime state/history are ephemeral. Session simulation uses the documented fake HTTP API, not the standalone slice-1 Run button.
- Old count-only buffers across administrative replacement remain a separate lifecycle policy; they are not silently joined, flushed, migrated or discarded by operational state pinning.
- No Graphiti, sending, live ingress, personality, carry or persistent session state.
- Commit contains only slice-2 runtime/editor/fake API/tests/docs. Original checkout, personal configuration and `workspace/DREAMS.md` remain untouched. Local feature-branch delivery only; no push or implicit merge into the dirty original checkout.
