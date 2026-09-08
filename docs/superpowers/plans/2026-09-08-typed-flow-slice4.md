# Slice 4 implementation plan

**Goal:** Real response generation in simulation, no transport or memory writes.

**Approved design:** Keep the existing two-instruction FSM. Only responder reaches hybrid person-scoped memory (batch senders + situation summary, 6 items/1200 characters), then a normal Instruction with raw text output named texto. A generic awaited instruction-output callback lets the simulator accept validated text before the next control edge/state transition. No new node kind or implicit recall.

## Integration prerequisite

- [x] Fast-forward main to 3d573d7, selective six-file stash ad1c3727516f9970cc730c49fea5b942e05f570f retained.
- [x] Resolve two independent insertion conflicts by retaining both contributions. All 171 old changed lines preserved; index empty. DREAMS SHA256 unchanged: D9A28EA2A4537CBE1CE1E9875F5152816CB53B952CECE062F3866FCFAC51E133.
- [x] Run 65 focused tests on restored main via isolated build artifacts (normal bin was locked by running App; do not stop it).

## Implementation (TDD, inline)

- [x] Fixture generator `tests/fixtures/slice4-project.mjs`: memory and no-memory variants in all three states; generation prompt/core/guidance refs and named separate inputs. Preserve old fixtures.
- [x] Fake tests demonstrate raw text schema, named output, empty rejection, explicit context, memory ordering/attribution, and transition ordering/failures. Run tests red before implementing.
- [x] Add minimal `outputMode: text|structured` support to Instruction compiler/runner/persistence/editor; missing mode preserves old behavior. Text mode must have exactly one text output. Publish only nonempty validated text.
- [x] Add optional async session instruction-output observer. Await after validation and before continuation; failures stop the path. Simulator identifies output ports explicitly, records WOULD_SEND in memory, never transport/history.
- [x] Add fake and real model harness with ten personality cases, two repetitions, plus memory/no-memory comparisons and multi-person attribution. Paired generation-only comparisons hold both upstream nodes at explicitly fake fixed signals; actual model calls are reported separately from full real-FSM runs.
- [x] Read-only Graphiti availability probe through existing reader; if usable run a separate real-memory scenario without inventing/ingesting memories. No service startup.
- [x] Run Core/backend/editor suites, build, formatting, roundtrip, simulation fixture. Live model output is evaluated qualitatively, not a personality score.
- [x] Report stage latencies and monotonic batch-close-to-WOULD_SEND, real text samples, limits, graph counts. Commit only slice4 files on feature branch; no push or unrelated files.

## Semantics and scope

State guidance during generation is the batch's original state, not Engajada prematurely. The control path switches only after successful simulation acceptance. Memory failure is not empty; generation/sink failure cannot produce the post-response transition. Output callback is optional generic infrastructure, not a Filomena-specific node. Runtime trace is metadata-only by default; synthetic report explicitly includes generated text for evaluation.

No Enviar, OpenClaw, Graphiti Core, global memory, auto-editing, subgraphs or production activation. Personality core is reused verbatim from the project, never copied into state prompts.
