# Slice 3.7 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline, with test-first verification.

**Goal:** Close participation semantics using explicit interests and a strict reason enum, without generation or sending.

**Architecture:** Extend the existing immutable InstructionContext and administrative project fields. Keep the 3.6 graph-owned FSM unchanged. New fixtures preserve the historical experiments and run only the two-instruction architecture.

**Tech Stack:** .NET 10, xUnit, existing JS editor, LM Studio JSON Schema.

- [x] Extend `InstructionContextTests.cs` with explicit interest selection, invalid configuration and batch snapshot tests; run `dotnet test tests/Filomena.Core.Tests --filter FullyQualifiedName~InstructionContextTests` and observe missing-interest failures.
- [x] Add `interestProfile` to project compilation and `useInterestProfile` to instruction validation/composition. Preserve existing project compatibility and inject only on opt-in.
- [x] Extend `personality-context.test.mjs`, editor project persistence and instruction checkboxes/preview. Run `node --test tests/frontend/personality-context.test.mjs`; verify interest/core/guidance roundtrip independently.
- [x] Add `slice37-project.mjs` and JSON fixture: objective involvement definitions, static interests, boolean follow signal and enum reasons. Existing 3.6 FSM edges remain identical; no edge reads `motivo`.
- [x] Add fake backend acceptance tests: strict reason codes, no profile in situation, profile in presence, safe trace, project roundtrip. Keep all old fake tests green.
- [x] Add an opt-in two-call matrix (10 scenarios x 3). Collect API token usage in a test-only HTTP handler, elapsed times and corrections without logging raw requests. Do not start services; confirm loaded Gemma before running.
- [x] Report every result including failures. Compare latency/retries to the recorded 3.6 baseline; explicitly mark historical token counts unavailable rather than inventing a reduction.
- [x] Run Core/backend/editor suites, build, formatting, fixture roundtrip and diff checks. Inspect unrelated Launcher changes and main-worktree overlaps before staging only slice files.
- [ ] Commit locally if verified. Integrate with fast-forward only when unrelated work can be preserved safely; never overwrite main-worktree edits, configs or DREAMS.
