# Typed-flow slice 3.5 — implementation plan

**Goal:** Run observable situation/presence experiments through the existing session flow, with real Gemma and no sending.

**Architecture:** Project `personalityCore` and state `participationGuidance` are optional design strings captured by the compiled project snapshot. Prompt nodes explicitly opt in using `usePersonalityCore` and `useStateGuidance`; composition occurs at the typed instruction boundary. The experiment harness observes validated outputs, selected Switch paths and transitions; it has no transport dependency.

**Tech stack:** Existing .NET 10 session runtime, IFlowCompletion/FlowModelCompletion, Graphiti read adapter, JS editor, xUnit.

## Boundaries

- Keep project schema 1 / graph schema 3; additive optional fields preserve old projects.
- Each batch uses its original project/state guidance snapshot, not the latest session state.
- Situation reads only batch/history. Presence optionally reads exact memory (4 items / 700 chars).
- DM and group need separate accumulator branches for their different experimental thresholds. Duplicate the small branch rather than invent shared control joins or dynamic presets.
- Each action has its own terminal state-mapping node. The harness reports WOULD_RESPOND only from the validated responder Switch path, never sends and never creates outbound history.
- Runtime trace retains structural metadata; experiment reports explicitly include synthetic scenario outputs, not real chat transcripts or full prompts.

## Tasks / verification

- [x] Fast-forward main to 0d50bd9. Post-integration baseline: Core 198, backend 43 (10 opt-in skipped), JS 72.
- [x] Runtime tests first: opt-in composition, absent flags, malformed configuration, batch snapshot across publication; add immutable prompt context and compose once before retries.
- [x] Persistence/editor tests first: roundtrip additive fields, UI editors at project/state/prompt, safe preview, old project unchanged. Preserve explicit draft semantics.
- [x] Fixtures: three stable state IDs, two entry branches per state, variants without/with exact memory; supplied Portuguese prompts/core/guidance verbatim; explicit enum-to-ID mapping.
- [x] Harness/fake tests: schema validation, three Switch outcomes, state isolation, WOULD_RESPOND simulation only, objective node receives no personality/guidance/memory, separate batch/history/memory inputs, no hidden LLM calls.
- [x] Opt-in real experiments: eight scenarios and memory A/B comparison; record per-node time, actual call count including correction, typed outputs and transitions. Observe existing services only; do not load models or start services.
- [x] Evaluate modularity and editor wiring from evidence; do not implement consolidation/subgraphs.
- [x] Run Core/backend/JS, build, format, roundtrip and diff check. Commit scoped files only; preserve unrelated Launcher artifacts.

## Commands

```powershell
dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --no-restore
dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-restore
node --test packages/flow-graph/tests/*.test.mjs tests/frontend/*.test.mjs
dotnet build Filomena.slnx --no-restore
git diff --check
```

Existing OpenClaw identity regression is outside this slice. No Gateway, personal config, Graphiti writes/Core changes, or DREAMS changes.

## Outcome

- Implemented and verified without delegation (no opt-in reply received).
- Final experiment: 11 successful executions / 22 real model calls; results and caveats in `docs/typed-flow-slice35.md`.
- Generic local graph limit raised to 32 for the 22-node memory variant (18 without memory).
- Live discovery: maxWords descriptions must also reach the textual model contract. Added the same schema to the adapter prompt, retaining strict validation and one retry.
- Main integration remains exactly the live-validation commits. Slice 3.5 stays on its isolated branch to avoid including or overwriting the dirty main worktree.
