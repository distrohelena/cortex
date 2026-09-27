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

Result at this checkpoint: **205 passed**, zero failures (including offline authority/intent validation
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

Typed corpus expectations now map to explicit per-step criteria with a separate list of dimensions
not evaluated by that step. All 25 cases are covered by this plumbing test; five voice-only cases remain
unscored. This does not claim that any model has answered the cases correctly.

The PostgreSQL experiment coordinator now implements explicit-ID plan persistence, idempotent
re-submission of the same plan, pinned candidate/case schedule rows, pending-only fenced claims,
and cancellation. Migration 003 leaves old unplanned rows unclaimable instead of inventing a schedule.
The coordinator integration test (idempotence, concurrent distinct claims, no automatic reclaim,
cancellation) is compiled but excluded with all PostgreSQL tests. Only its pre-DB authority/scope
rejections were executed here. Trial result association is now implemented: matching profile/input
revision, current fenced claim, intact stored output and observed attempts are required; a repeated
completion can only reference the same artifact. The PostgreSQL test includes missing/unresolved
attempt rejection and same/different-result re-entry, but remains **unexecuted**. Migration 004 stores
the output reference; retention redaction remains operator-controlled. Reconciliation and live CLI
remain incomplete. No durable correctness claim is made from the 205 offline tests.

Host-side provider composition now binds dedicated HTTP clients to `AttemptLedger` for the
claimed experiment/trial/owner/generation. Redirects and cookies are disabled; application retries
are not added. Construction validates evaluation authority, both revision scopes, authorized endpoint,
finite request timeout and positive paid-call cost reservation without reading credentials or opening
connections. All clients are disposed on construction failure or owner disposal.
The six new tests were observed failing before implementation, then passing. The offline suite now
passes **211 tests**, and the evaluation CLI builds with zero warnings/errors. PostgreSQL and Docker
lifecycle tests remain explicitly excluded; these construction tests do not prove database durability
or native HTTP transport retry behavior. No paid execution or chat activation was performed.

`CompletionTrialExecutor.ExecuteNextAsync` now connects pending-only claim acquisition, stored target/
profile/input loading, bound providers, output artifact registration and fenced completion. The target
is a pinned `completion-target` revision containing `nodeId`, not an unrecorded runtime argument.
`CompletionTrialMaterial` verifies scope, kind/version, all three hashes and profile round-trip identity.
Thirteen material reconstruction/rejection tests and three executor pre-database rejection tests were
observed RED then GREEN; **227 offline tests pass**. The executor's successful PostgreSQL path is
implemented but **not yet integration-tested or enabled in the CLI**. Errors retain running claims for
explicit reconciliation; no automatic replay/reset occurs. A null result means no pending trial, not
experiment success. Revision-backed inputs are development corpus; private holdout execution is not
supported by this path. Migration acceptance, failure/reconciliation workflow and end-to-end database
tests remain required before real paid execution.

The executor now attempts a fenced `StopFailedTrialAsync` after any post-claim failure or cancellation.
It locks experiment then trial, preserves all attempts/reservations, records only a fixed diagnostic
code plus opaque trial ID, marks reconciliation required and blocks subsequent claims. Completed or
foreign/stale claims cannot be overwritten. A failure to persist that stop is explicitly reported as
`stopRecorded=false`; the executor neither fabricates success nor retries. This does not recover a
process crash or an unavailable database automatically.
Seven pre-database rejection tests pass (offline suite: **234 tests**); a PostgreSQL executor failure
test is compiled but **not executed**. CLI build has zero warnings/errors. Real cancellation races,
transaction behavior and durable restart semantics still require the database acceptance suite.

`case.prepare.synthetic` now prepares pinned step inputs and separate synthetic provenance. All 25
development cases map to the actual project's situation input names without exposing rubric fields.
Downstream inputs require explicit fixtures; missing values are not guessed, conversation fields cannot
be overwritten, and fixtures cannot masquerade as runtime replay. This does not yet execute the five
areas or validate supplemental port value types. **241 offline tests pass**, CLI build is clean, and
the compiled command smoke returned `persisted=false` and `executionAuthorized=false`. No models,
database connections, Docker commands or chat activation were used.

An adapter composition regression was reproduced: LM Studio receives a base URL and posts to
`chat/completions`, while the accounting handler previously required the base URL itself. The handler
now resolves only that provider's exact completion URI; other providers retain their exact destination.
Two tests use the real local adapter over a controlled HTTP handler (base URL with/without trailing
slash), prove reservation-before-dispatch and observed completion, and passed after failing on the
original destination check. No LM Studio server or model was used. Offline suite: **243 tests**.
The second composition issue is now corrected separately: `LmStudioClient` changes the supplied
`HttpClient.Timeout` to infinite and owns a profile timeout. Evaluation restores the original host
deadline after constructing that adapter, including constructor failure, without editing the shared
LM Studio client. Its regression failed with expected seven seconds versus actual infinite before
the fix and passed afterward. The profile's cancellation deadline remains independent; whichever
deadline expires first applies. **244 offline tests pass**, and the evaluation CLI builds without
warnings/errors. This is not an online Gemma/Luna/Jev benchmark or database acceptance result.

The complete trial acceptance test is now prepared as an opt-in PostgreSQL test. It persists a plan,
runs the actual local adapter against a one-response loopback fixture, checks the committed reservation
from a separate database connection before returning a response, verifies stored output hashes and
completion, and rejects a second execution of the completed experiment. **It has not run** while
PostgreSQL operations are suspended. The HTTP fixture alone was exercised through the evaluation
handler and real adapter, with an explicitly test-only journal and no model. The fixture initially
rejected chunked input when tested without the evaluation handler; using the actual buffered transport
composition resolved that test mismatch. **245 offline tests pass**; no database acceptance is claimed.
