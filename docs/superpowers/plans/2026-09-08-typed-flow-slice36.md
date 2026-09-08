# Slice 3.6 — FSM calibration

> Execution: superpowers:executing-plans, inline; no delegated agents or service lifecycle changes.

**Goal:** Observations belong to instructions; state transitions belong to visible flow wires.
**Architecture:** Preserve slice 3.5 fixtures/tests. New 3.6 fixtures use `acao`, `continuar_acompanhando` and `motivo`; no model-selected state. Complete the existing Branch's typed execution, not a new node. The default remains two calls/no memory; single-call and exact-memory designs are experiments.
**Tech stack:** existing C# typed/session runtime, editor descriptors, xUnit, Node tests, loaded Gemma via LM Studio.

## Decisions

- Boolean attention signal is sufficient. An interest enum duplicates `envolvimento` and offers no extra V1 transition.
- Inactive: silent false ends unchanged; silent true/wait → Attentive; respond → Engaged.
- Attentive: silent false → Inactive; silent true/wait end unchanged; respond → Engaged.
- Engaged: respond ends Engaged; silent/wait use situation involvement, none → Attentive, indirect/direct end unchanged.
- No transition on an unchanged path: preserve stateVersion/enteredAt. Explicit responder exit targets Engaged (also from Engaged).
- No arbitrary state IDs in model outputs, no Join, no automatic memory or scheduling changes.
- Graphiti style boundaries are not automatically participation boundaries. Keep distinction documented, not inferred/classified.

## Tasks

- [x] Baseline backend; preserve dirty Launcher, create feature/typed-flow-slice36 from feddb72.
- [x] Red test existing Branch with typed boolean A and configured B=true; reject object/optional/coercion, wire+local; execute one path with trace. Implement compiler/runner and editor descriptor/panel support; no personality logic in runtime.
- [x] Create slice36 generator plus principal, single-call, experimental-memory JSONs. Reuse exact original situation prompt/core and unchanged Atenta/Engajada guidance; strengthen Inativa. Stable explicit exit targets and generic Branch/Switch only.
- [x] Extend existing development harness with optional project override; add calibration fake model, all action/interest/involvement truth-table tests, no-send traces, session isolation, backend/editor roundtrip, invalid model fails closed.
- [x] Add opt-in repeated paired experiment (A/B order alternating), same scenarios/options, no memory in latency comparison, both additional attention scenarios. Record validated observations/actions, actual terminal state, correction calls, timing, Branch/Switch path. Separate optional-memory comparison.
- [x] Verify Core/backend/editor, build/format/diff; run loaded Gemma without starting/loading services. Preserve structural failures in report, don't assert personality enums as test goldens.
- [x] Document real results, latency/consistency/limitations and graph node/wire counts. Leave branch experimental, no push or main merge.

## Commands

`dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --verbosity quiet`

`dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --verbosity quiet`

`node --test tests/frontend/*.test.mjs`

`dotnet build Filomena.slnx --no-restore --verbosity quiet`

`dotnet format Filomena.slnx --no-restore --verify-no-changes --include <changed C# files>`

Live opt-in: `FILOMENA_TEST_CALIBRATION=1`, focused `ParticipationCalibrationLiveTests`; no deadlines that classify slow inference as failure, CancellationToken supported. Report missing service/model and stop live portion rather than start services.

## Scope guard

Executed: Core 220, backend 115, editor/library 81 passing; build and formatting passing. Real A/B completed all 42 evaluations: 41 valid, one single-call failed maxWords after retry. This failure remains visible (opt-in test exits red), not retried until green or masked. Browser unavailable, so interactive visual review remains unverified; graph metrics/roundtrip checked. No closing commit or merge. No subagents/independent review launched without opt-in.

No generation, sending, transport, Graphiti writes/Core, personal configs, Gateway, DREAMS, new node kinds, subgraphs or automatic project loading into the user's editor. Schema remains additive graph v3/project v1. Existing fixtures remain regressions.
