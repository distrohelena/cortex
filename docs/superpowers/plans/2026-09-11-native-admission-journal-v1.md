# Native Admission Journal V1 implementation plan

> Execution: inline with `superpowers:executing-plans` and TDD. No subagents,
> deployment, listener, Graphiti live, model calls, agent runs or outbound.

Goal: opt-in native admission and replayable observation in one SQLite commit,
followed by read-only dispatch inspection and fail-closed .NET preflight binding.

## Execution outcome

Native transaction, producer identity copy, local durable reader, pure static
inspection, .NET status validation and non-resumable run journal were implemented.
Final focused verification: 109 upstream tests + 73 .NET tests passed; core and
extension typechecks, generated schema verification, formatting and diff checks passed.
See `docs/native-admission-journal-v1-implementation.md` for evidence and limitations.

**Not completed / fail-closed:** binding the native source into the live adapter,
and obtaining a read-only effective configuration snapshot from a dynamic native
owner. The coordinator reports `unknown` rather than probing access-control or
claiming that its initial config is current. No live readiness, deployment or commit.
The task checklist below remains the original plan, not an assertion that every
integration/operational proof has been completed.

Baseline: isolated OpenClaw v2026.8.1, ea806575e6450e4d1efdfc72c19f04be982a1b9b.
User explicitly approved rollback of native admission on observation failure.
Filomena worktree and installed OpenClaw remain separate from this source checkout.

## Batch 1 — actual admission transaction

- [x] Locate native key validation, enqueue transaction, retries and tombstone owner.
- [x] Prepare isolated pinned dependencies; no install scripts or active Gateway changes.
- [x] Run baseline: `node scripts/run-vitest.mjs src/channels/message/ingress-queue.test.ts` (35 passed).
- [ ] Add `src/channels/message/native-observation.test.ts` at the real queue/database
  boundary. Assert capture disabled by default, successful native+observation commit,
  rollback of both sides on a failing SQLite observation INSERT trigger, and payload
  surviving `queue.complete()`.
- [ ] Observe the new regression failing before changing production code.
- [ ] Implement a host-owned non-serialized capture lifetime and additive journal
  in `src/channels/message/native-observation.ts`; invoke its append from the existing
  queue transaction only for the host-bound exact route. No post-hook append.
- [ ] Rerun new tests together with all baseline queue tests.

## Batch 2 — native producer and durable reader

- [ ] Capture self aliases/fromMe/native identity synchronously from the WhatsApp
  coordinator into admission, only when requested by the armed host capability.
  Pass identity through the generic ingress monitor, without async enrichment.
- [ ] Add real SQLite tests for native dedupe, epoch, repeated polling, restart,
  snapshot boundary, corruption/gaps, retention expiry and exhausted native retries.
- [ ] Implement consistent journal-backed snapshot/cursor reads and explicit
  status/error codes; never promote transcript history into sequenced observations.
- [ ] Add producer tests for self PN/LID, missing identity, other route, and default-off.

## Batch 3 — inspection and bootstrap

- [ ] Trace authoritative route/access/command/plugin resolution. Use only pure
  resolution and authoritative runtime snapshots, never execute access-control or
  hook callbacks to inspect. Unknown is a failing inspection, not inactive.
- [ ] Add inspection tests for active/inactive/unknown and uncovered early paths.
  Stop this component if an effective exclusion proof cannot be derived safely.
- [ ] Bind the versioned observation health/capabilities/status to the existing
  .NET preflight. Reject unknown version, missing capability and non-inactive dispatch.
- [ ] Formalize fresh non-resumable ShadowRun lifetime with durable processed-ID
  dedupe and fresh snapshot; do not restore prior executions or fabricate history.

## Final verification

- [ ] Targeted native, producer, inspection and .NET bootstrap tests.
- [ ] Formatting/type/SDK/schema checks appropriate to actual changed files.
- [ ] Review diff, privacy, default-off, no live entrypoint invocation, no brain changes.
- [ ] Report implemented vs blocked surfaces and actual test results separately.
  No live readiness claims from fixtures. No commit unless all relevant gates pass.
