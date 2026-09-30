# Local Flow Editor Implementation Plan

> **For agentic workers:** Execute this approved small slice inline, using TDD. Do not resume the larger compiler roadmap or delegate additional work.

**Goal:** Draw and execute a local manual-input → one or more prompts → screen-output flow.

**Architecture:** Extend the isolated playground host with a bounded sequential executor and embedded visual editor. Explicit edges, not node array order or screen position, define execution. The existing release compiler remains unchanged; local drafts have their own versioned envelope and are not portable releases.

**Tech Stack:** Existing .NET 10 / LM Studio client, embedded HTML/CSS/JavaScript with SVG wires; no new package or build chain.

**Spec:** User-approved design in chat: draggable/connectable nodes, editable prompts, sequential chaining, per-node input/output/duration/error inspection, local save/open. No memory, channels, parallelism, or publishing.

## Global Constraints

- Keep the gateway and Graphiti untouched; no real model calls during automated tests.
- Preserve dirty work and existing UUID/compiler contracts; work on the user-selected main branch.
- Only one input, one output, 1–8 prompts, no branches or cycles. Reject the entire invalid graph before inference.
- JSON drafts preserve positions and prompts only; do not save run inputs/results by default.
- Model comes from existing backend configuration. No model loading or model-management APIs.

## 1. Local executor and HTTP boundary

Files: `src/Filomena.App/Playground/LocalFlowRunner.cs`, existing `LocalPromptPlayground.cs`, `tests/Filomena.Llm.Tests/LocalFlowTests.cs`.

- [ ] RED: POST `/api/flow/run` with shuffled node array and explicit input→p2→p1→output edges. Assert resulting trace order, second model input equals first model output, final output equals second response.
- [ ] RED: invalid cycles, disconnected nodes, forks, missing/duplicate IDs, blank prompts, oversized inputs, unknown types return 400 with zero model calls.
- [ ] Implement `LocalFlowDraft(version,nodes,edges)` and `LocalFlowRunner.Validate/RunAsync`, returning ordered traces and safe error at the failing node. Cancellation propagates; later nodes do not execute after failure.
- [ ] Reuse host origin/host/header checks for `/api/flow/validate` and `/api/flow/run`. Keep `/api/test` intact.
- [ ] GREEN: run focused tests and full LLM suite with mocked provider HTTP.

## 2. Visual editor

Files: `Playground/flow.html`, `flow.css`, `flow.js`, `flow-model.mjs`, App embedded resource registration, `tests/frontend/flow-model.test.mjs`.

- [ ] RED: test adding/removing connections, deleting a node removes its wires, draft save/open round-trip preserves positions, invalid import is rejected without destroying current state.
- [ ] Implement canvas with draggable nodes, clickable output/input connectors, removable wires, prompt inspector, run input, result traces. Start with a connected input/prompt/output example.
- [ ] Wire execution to C#; freeze edits during execution, expose cancel, render user/model text through `textContent` only.
- [ ] Save/open bounded JSON files with an explicit local-draft marker and backend validation before execution. Opening unfinished drafts is permitted if structurally safe.
- [ ] Browser smoke test adding a second prompt, rewiring, dragging, editing, saving/opening, and observing validation without invoking real LM Studio.

## 3. Stop checkpoint

- [ ] Build App; run LLM suite, frontend tests, `git diff --check`; report unrelated pre-existing suite limitations rather than fixing paused flow work.
- [ ] Document `/flow` and the same `--playground` command. Hand off for user testing; do not continue the roadmap automatically.

## Execution record

Implemented the executor, validation, embedded editor, JSON save/open, trace inspector, and cancellation. Existing compiler/release code was not modified. The missing API tests first failed with 404; the editor module tests first failed because the module did not exist.

Final verification: `dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj -c Release --no-restore` passed 58 tests; `node --test tests/frontend/flow-model.test.mjs` passed 3 tests; both editor modules passed `node --check`; App Release build passed with zero warnings/errors; `git diff --check` exited 0 (line-ending warnings only).

Browser smoke testing remains unchecked: the browser runtime reported no available browsers and discovery returned an empty list. No substitute browser automation was used. Real LM Studio inference and visual interaction are the user's next acceptance test. Larger paused compiler tests and previously unfinished dashboard tests were not part of this checkpoint's verification. No gateway operation, Graphiti write, commit, or push was performed.
