# Boundary compilation implementation plan

## Revised V1 checkpoint — deterministic parser

Approved change supersedes the LLM-interpreter release gate below. Historical
benchmarks and their raw failures remain evidence, not runtime dependencies.

- [x] Runtime: no completion service in compiler path; same admission, trust registry and policies.
- [x] Original spans, narrow grammar, atomic set, null/forgery/cross-session failures tested.
- [x] Rename display to Compilar Limites; remove provider configuration from panel and persisted node.
- [x] Legacy field migration preserves IDs/wires and draft save/load.
- [x] H2/H6 test harness uses real session runtime and short GateMemory source, never manually expanded policy.
- [x] Live calls restricted to Luna medium; no Terra, Gemma generation or service changes.
- [ ] Live gate FAILED at H2-crise R1: expected bloquear, Luna permitir. H6 not reached.
- [ ] Hard-set and H×10 blocked; no sending or closing commit.

See `docs/boundary-parser-v1.md`. The older plan/checkpoint below is retained as history.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Track each independently testable increment below.

**Goal:** Compile trusted, execution-local memory boundaries without expanding or weakening their source.

**Architecture:** A small admission grammar independently consumes the entire normative clause. A configurable semantic interpreter proposes exact evidence; a deterministic compiler admits only matching proposals and selects immutable policies. Execution-private capabilities retain source snapshots and prevent JSON forgery.

**Tech Stack:** .NET 10 / existing typed-flow contracts, xUnit, existing JavaScript editor and node:test, existing IFlowCompletion providers.

**Spec:** Helena's approved Boundary implementation request in this conversation, 2026-09-08.

## Global constraints

- Only `no_teasing_about_topic`; no conditions, exceptions, multiple clauses, other kinds or cache.
- No production DAG changes, H×10, sending, Graphiti writes, source changes, Gateway operations, personal configuration edits or DREAMS changes.
- Preserve the existing uncommitted slices and benchmark evidence. No closing commit with an open broadening/weakening error.
- Remote benchmarks use synthetic sources only; no private source content in default trace.
- `compiled` from a model is a proposal, never a trusted artifact.

## 1. Admission/compiler, independently testable

Files: `src/Filomena.Core/Boundaries/BoundaryAdmission.cs`, `BoundaryContracts.cs`, `BoundaryPolicies.cs`; `tests/Filomena.Core.Tests/Boundaries/BoundaryAdmissionTests.cs`.

- [ ] Write table-driven tests with literal source/evidence/status expectations: qualified topic, original offsets, supported direct and GateMemory forms, other kinds, unresolved referents, conditions, multiple clauses, duplicate evidence, invented/abbreviated evidence and injection.
- [ ] Run `dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj --filter FullyQualifiedName~BoundaryAdmission` and observe missing functionality.
- [ ] Implement `BoundaryAdmission.Analyze(string)` returning status, diagnostic code and original spans; implement `Admit(source, proposal)` checking unique exact evidence against the independently parsed full clause.
- [ ] Rerun the focused tests. Policies are fixed versioned code, not interpreter-generated text.

## 2. Execution provenance and atomic interpretation

Files: `Boundaries/BoundaryExecutionRegistry.cs`, `BoundaryInterpreter.cs`, existing `MemoryValues`, `TypedFlowRunner`, session memory callback; matching Core tests.

- [ ] Test that forged handles, cross-execution/namespace sources, stale sources, missing/noncanonical subjects and partial sets cannot publish Boundaries.
- [ ] Retain immutable MemoryItem snapshots in one registry per execution; only the read adapter callback can register sources. Resolve capabilities before compilation and LLM Boundary serialization.
- [ ] Implement one-memory strict interpreter request with nullable enum/evidence, max one structural retry, sequential atomic publication, no call on empty input. No retry for abstention/admission failure.
- [ ] Assert payload omits IDs and source handles, subject remains structural, policies preserve attribution and original evidence. Assert diagnostic metadata without private text.

## 3. Typed node/editor/persistence

Files: `FlowDomain`, `TypedFlowCompiler`, `SessionFlowProjectCompiler`, `TypedFlowRunner`; playground contract/model/panel/canvas; Core and frontend tests.

- [ ] Add tests for Boundary/Boundary[] ports, node executable validation, stable ports, draft save and roundtrip, no normal Instruction fabricating Boundary.
- [ ] Add operational `interpretBoundaries` node with memories input, boundaries output and then pin; panel provider/model/effort/budget and pinned versions/read-only policy preview.
- [ ] Keep general nested output editor and production fixture unchanged.

## 4. Interpreter benchmark, then compiled-policy E2E

Files: synthetic interpreter corpus, LLM opt-in tests and report; standalone compiled-policy fixture (not production DAG).

- [ ] Run all deterministic Core/backend/editor tests before remote calls.
- [ ] Compare Luna/Terra medium on identical synthetic corpus, prompt/schema/configuration and repetitions. Record raw-proposal mistakes separately from admission rejections, false abstention, evidence, schema, instability, latency and actual usage/cost.
- [ ] Stop for broadening/weakening. Select interpreter from evidence, not validator preference.
- [ ] Pass short sources through actual interpreter/compiler to Luna validator for historical hard set and H2/H6. No manually expanded fixture policy, no H×10.

## 5. Verification/report

- [ ] Focused Core, backend, JS/editor; builds; touched-file formatting; fixture roundtrip; `git diff --check`.
- [ ] Report grammar, policies/versions, trust boundaries, metrics, abstention, extra latency and blockers. Explicitly distinguish deterministic admission/delivery from probabilistic interpretation and candidate validation.

## Source inspection

Read-only inspection of Drift, Plex and DM on 2026-09-08 found no RELATES_TO facts with the three boundary relationship names consumed by the adapter. No real private source was sent remotely. The currently executed synthetic GateMemory source is exactly `Helena pediu para Filomena não brincar nem provocar sobre trabalho.`. Do not claim this fixture is a persisted Graphiti fact.

## Execution checkpoint

- [x] Admission, original spans and fixed policies implemented with fake tests.
- [x] Execution-private provenance, atomic publication, structural retry, source expiry recheck and diagnostics implemented.
- [x] Typed node/editor/save-load integrated without changing production DAG.
- [x] Deterministic suites green; Luna/Terra baseline completed (96 calls).
- [ ] Interpreter approval: BLOCKED. Both models returned incorrect operation spans for the short production source in all three repetitions. Raw false compiled proposals were also rejected by admission.
- [ ] Hard-set compiler E2E / H2-H6: not started because interpreter gate did not pass.
- [x] Final build, formatting, Core/backend/frontend and report completed.

See `docs/boundary-compilation.md` and `docs/boundary-interpreter-results.json`.
No closing commit and no H×10 until the interpreter contract/conformance issue is resolved.
