# Evaluation workbench: offline development checkpoint

The host is being migrated. Docker/PostgreSQL operations are suspended by user instruction.
Filomena is not activated by this work. No chat bootstrap, sender, Graphiti writer or production
configuration is used by the evaluation project.

## Implemented and tested without services

- Immutable configuration profiles and canonical revision hashing.
- Host-resolved evaluation roles (manifest data cannot grant evaluation authority).
- Provider factory using the existing OpenAI, LM Studio and SystemOne adapters.
- Pinned prompt/schema/model/settings; mismatched requests are rejected.
- Single-completion evaluation target verifies input/configuration revision hashes and project
  scope before invoking the existing adapters. Controlled HTTP tests cover Luna and Jev, cancellation,
  unauthorized execution and outage without retry. Its raw output is not a quality verdict; durable
  attempts/artifact persistence and full multi-step execution remain coordinator work.
- Host-bound endpoint aliases, with no candidate-provided arbitrary endpoint.
- Typed decision plus separately pinned generative summary; summary failure cannot become partial success.
- Per-physical-call HTTP accounting boundary: reserve before send, finish after response,
  no automatic retry after ambiguous transport or persistence failure.
- Request hash and bounded response usage observation, without recording authorization headers
  or remote exception payloads.
- Cost projection using an explicit historical price snapshot. Missing usage, unknown cache
  accounting and incomplete prices remain unavailable, not zero. Reasoning tokens are not counted twice.
- Paired comparison kernel: incomplete/unscored/critical/error cases fail eligibility; reused
  physical attempts cannot count as independent repetitions. Unknown cost or different currencies
  cannot establish financial dominance. Reports include correctness, descriptive Wilson intervals,
  median/p95 latency and an observed Pareto set; this is not a statistical superiority claim.
- Synthetic conversation mapper uses the runtime's `FlowDomain.ForModel` serializer for
  current messages, history and quotes. All 25 development cases map. Grading metadata is
  excluded, current self-events are rejected, missing quote content is not reconstructed.
  These are situation-step inputs, not a fabricated complete generation/runtime execution.
- Deterministic experiment schedule preparation from pinned manifest/case revisions and seed,
  including paired interleaving and a minimum physical-call budget check. This prepares work;
  it does not reserve money or authorize execution.
- Pure admission policy checks running state, cancellation, current owner/generation and unresolved
  attempts before calculating a bounded reservation. It is not a database lock or durability proof.
- Migration catalog verifies the complete applied checksum chain, rejects unknown versions/gaps,
  and selects only pending migrations. Migration 002 adds execution accounting fields, refusing
  to infer historical accounting for databases that already contain attempts. No migration ran here.

## Verification

`dotnet test tests/Filomena.Evaluation.Tests -c NativeMemory --no-restore --filter 'Category!=Postgres&FullyQualifiedName!~DatabaseLifecycleTests'`

Result at this checkpoint: **191 passed**, zero failures (including offline authority/intent validation
for the attempt ledger). These tests use controlled HTTP,
not paid inference. Database lifecycle tests were explicitly excluded, not certified.

`dotnet build src/Filomena.Evaluation.Cli -c NativeMemory --no-restore`

Build passed with zero warnings/errors. The offline CLI supports revision hashing, structural
profile validation, cost projection and comparison previews. A built-process smoke check proved
hashing succeeds and `experiment.run` is rejected with exit code 2. Durable/live CLI commands
remain incomplete; this is not an end-to-end experiment acceptance result.

## Still pending

1. PostgreSQL store and migration code now compile, with scoped references, immutable revisions,
   original revision bytes, checksum validation, assessment provenance/supersession and audited
   artifact redaction. **All PostgreSQL integration tests remain unexecuted** during migration;
   compilation and static definition checks do not prove SQL correctness or transaction behavior.
   `AttemptLedger.Begin/Finish` are now implemented with experiment/trial lock ordering, fenced owner,
   atomic reservation plus started-attempt insertion, retained reservations and no retry/refund.
   Indeterminate outcomes or observed token overruns stop new experiment calls. Cancellation does
   not suppress accounting for an already-sent call. Four opt-in PostgreSQL tests cover last-call
   contention, unresolved attempt re-entry, cancelled/duplicate completion and obsolete generation.
   **Those tests are compiled but unexecuted**, and are not a process-kill crash test. This journal
   is not wired into the live CLI; concurrency/durability acceptance remains pending.
2. Connect the accounting interface to that durable ledger. The test in-memory journal is
   **not** suitable for paid execution. The live HTTP transport must disable redirects and retries.
3. Experiment scheduling, persisted comparisons/assessments, paired bootstrap uncertainty
   and complete CLI/application operations. The pure comparison kernel is implemented.
4. Real model execution for the five behavior areas and local Gemma/Laya capability checks.
5. Database integration, backup/restore verification after migration.

No model quality ranking, database durability, complete workbench or production readiness is claimed.

The deterministic assessment kernel and `assessment.preview` now grade enum/boolean acceptance,
preserve critical failures and leave manual semantic/voice criteria unscored. A compiled CLI smoke
returned a critical participation failure plus unscored voice without persistence or model execution.
These are grading-tool tests, not Luna/Jev quality results.

`instruction.describe` exports effective instruction text and schema through the existing runtime
compiler/composer without instantiating a runtime. This prevents benchmarks accidentally omitting
enabled personality/state/interest context. It is a read-only helper; prompts, FSM, model assignments
and live configurations were not edited. `profile.prepare` now binds explicit candidate settings to
these snapshots. An offline test prepares 15 combinations across the existing three-state project's
situation/presence/generation steps: Luna for all, Jev+summary for situation, Jev alone for presence.
Jev/Laya free generation is rejected. This validates profile construction, not live model fitness.
Persisted experiment bundles and real upstream outputs for downstream cases are still pending.
