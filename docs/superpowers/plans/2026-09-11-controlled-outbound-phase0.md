# Controlled Outbound — Phase 0 implementation plan

Scope: disabled/shadow infrastructure only. No transport dispatch, live resources, model calls, Graphiti writes or production graph edits. Existing experimental work remains untouched.

## Architecture

Host-provided authorization pins a reviewed project document and role/node/port bindings. Private execution registry follows actual TypedValue object identity from trusted recall through compilation and generation/enforcement. Text equality is not authority. Enviar receives only mensagem/exec; origin comes from the session batch. A host-owned mode is disabled on construction and queried at the sink. Enabled requires explicit adapter capabilities and remains unavailable in Phase 0 even with complete capabilities.

Use a small durable exclusive file journal (no added database dependency): append records flushed to disk before returning shadowed, per-process exclusive ownership; malformed/torn records fail closed. Store opaque hashes/refs, never message content or JIDs. Separate shadow entries cannot become real pending sends. Deduplicate execution and trusted inbound identities across restart. Future pending/indeterminate states block session outbound and require explicit reconciliation, not retry.

## Tasks / TDD checkpoints

1. Add failing core tests under tests/Filomena.Core.Tests/Outbound for host modes, provenance identity, empty recall, replay and journal durability. Run focused tests RED before adding src/Filomena.Core/Outbound implementation. Then GREEN.
2. Add runner/compiler integration tests for send-only-to-origin, state suppression and actual recall/compiler/enforcement hooks. Extend TypedFlowRunner and SessionFlowRuntime optional host injection only. Preserve normal Instructions and current enforcement contracts. Run RED/GREEN.
3. Replace legacy recipient ports in flow-model/contract and show read-only Phase 0 semantics in the existing panel. Backend/project roundtrip rejects send configuration that attempts recipient/mode escalation. Add frontend tests RED/GREEN.
4. Update approved design: capability gate including chunking, shadow FSM limitation, reconciliation-required semantics, durable shadow namespace/restart. Run focused Core/backend/JS suites, build/format verification and diff checks. Do not commit unrelated dirty work.

## Handoff

Report local tests and zero transport calls. No Shadow Live start; host rearm/session allowlist and controlled live ingress need explicit operational readiness review. DM Live remains impossible.

## Execution checkpoints

- Core guard/journal RED (missing types), then GREEN. Windows exclusive writer lock correctly prevented concurrent reader with incompatible sharing; test now closes writer before inspecting bytes.
- Enviar compiler RED (unsupported node), then GREEN. Integration uses real runtime/recall/compiler/enforcement with fake I/O, including Block, outage, technical failure and copied candidate.
- Editor RED (legacy recipient/list contract), then GREEN. Existing legacy tests updated for deliberately removed destination/list inputs and explicit status exits.
- Backend roundtrip exposed missing acceptance of three already-existing composition fields. Added only persistence keys; no changes to detector contracts/policies/prompts/versions.
- Build and focused verification performed; detailed results and readiness boundary are in docs/controlled-outbound-phase0.md. No live resources started. Worktree retained without closing commit because it includes extensive pre-existing uncommitted experiments.
