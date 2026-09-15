# Slice 4.1 — calibration plan

Approved design: no FSM/runtime changes. Existing memory nodes run sequentially: exact active boundaries (4/700), then hybrid social categories (6/1200), then normal generation and simulation. Separate named inputs limites/memorias_sociais. Revise project core and generation prompt only. Keep slice4 fixtures intact.

Boundary delivery guarantee means scoped, active, separately budgeted delivery with failure distinct from empty. It does not guarantee exhaustive recall beyond the existing API/4-item budget.
Boundary compliance means observed model behavior, never a universal promise.

- [ ] Red JS contract tests: fixture has separate inputs and unchanged upstream nodes/FSM.
- [ ] Create slice41-project.mjs and generated memory/no-memory JSONs; revised core/source hierarchy.
- [ ] C# fake tests: separate inputs, active/category filters, canonical attribution, namespace fail-closed, independent budgets, failure/empty, injection, roundtrip/trace.
- [ ] Reuse simulation harness with fixture override. Fixed fake situation/presence, one real generation; no FSM calibration.
- [ ] Run H with boundary first, three generations reviewed manually one at a time. Stop on explicit violation, report without adding classifier/retry.
- [ ] Only if H passes, run A–N three times, plus H without boundary and I without nickname. Keep all complete outputs and content/voice assessments separate.
- [ ] Core/backend/editor/build/format/diff checks; report latency versus slice4, readiness and remaining limitations. No service startup, OpenClaw, config edits or memory writes.

Files: tests/fixtures/slice41-project.mjs, typed-flow-slice41*.json; tests/frontend/generation-calibration.test.mjs; tests/Filomena.Llm.Tests/GenerationCalibrationTests.cs, GenerationCalibrationLiveTests.cs; existing GenerationExperiments helper gains only an optional fixture override. Results and final report in docs/typed-flow-slice41*.

Baseline: 8c63be1, feature/typed-flow-slice41. Prior TestResults and Launcher files remain untouched/excluded. Execute inline; no delegated workflow.
