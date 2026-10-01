# Evaluation CLI

The standalone binary defaults to the **offline**, stateless, non-persisting surface. It does
not bootstrap Docker, PostgreSQL, models, Graphiti or the conversation application.
Host-bound persisted and execution surfaces exist as library composition; they are not
automatically enabled by invoking this binary.

## Host-bound execution composition

### Cancellation and uncertain effects

Cancellation before admission must not create an attempt or invoke transport.
After transport submission, cancellation does not prove that the provider did no
work: the handler records `indeterminate` with `request_cancelled`, without
inventing usage or retrying. Final accounting uses a token independent of the
cancelled request. If persistence fails, the earlier reservation remains uncertain.

Two in-memory handler cases currently verify this distinction. The separate
PostgreSQL acceptance theory checks retained reservation, stopped trial and
rejection after reopening the application; it is prepared but **not executed**.
Neither cancellation nor a fresh application grants permission to replay work.

### History across comparison reports

`issues.history` accepts `{ "reportIds": ["<uuid>", "<uuid>"] }` for 1–20 distinct
reports. Only host `Evaluate` access is accepted. The reader uses a single read-only
repeatable-read transaction and the same scoped source-availability/integrity validation as
`comparison.show`. A missing/foreign report fails the request rather than silently reducing
the denominator. Redacted/unavailable reports remain explicit in `reports`; no derived items
are returned for those reports. Reads observe a database snapshot, not a lease against later
redaction of data already returned.

Groups require identical experiment-plan hash, comparison-policy revision and input bindings.
The plan hash includes the manifest and ordered inputs, so targets, datasets, evaluators and
different execution designs are not merged merely because their candidates have similar names.
This deliberately conservative rule supports repeated runs of the same design. Cross-design
normalization and statistical pooling are not implemented.

Within a group, the same trial in multiple stored reports counts once. Independent experiments
can contribute distinct trials for the same case/repetition. Changing a judgment, measurement
or experiment binding for an existing trial produces `conflicting_evidence`, not latest/best
wins. Reusing an assessment/physical attempt across trials or conflicting criticality/cell
identity also withholds the group's metrics. No assessment is rewritten or superseded by reading.

Top-level status is `partial` if any requested report is unavailable/invalid or any group has
conflicting evidence. Otherwise it is `available`. There is no global ranking across cohorts;
each group retains the existing versioned issue-priority semantics. Failure costs are estimates
for unique failed trials, not savings or billing. Criteria/cohorts may overlap: do not sum them
as independent global spend or observations. Report references allow tracing the provenance.

The projection is pure but does not grant authority; only the host reader resolves stored
evidence. No model, retry, reconciliation mutation or profile activation is performed. Offline
tests passed; PostgreSQL duplicate-report, cross-scope, withholding and read-only acceptance
were prepared/compiled, not executed.

### Corpus import

The host-bound persisted surface accepts two explicit operations:

* `artifact.import`: `{ "kind": "case", "isHoldout": false, "content": "<base64 bytes>" }`.
  All fields are required. Content is supplied inline; the host does not open a caller-supplied
  file path or URL. The 1 MiB request envelope also bounds encoded content, so the largest
  accepted binary payload here is smaller than the store's separate 1 MiB artifact limit.
  Success returns only an artifact ID and SHA-256, not its content. Same scoped bytes and
  metadata reuse the existing reference; different/redacted metadata cannot be overwritten.
* `case.import`: `{ "caseKey": "synthetic-001", "artifact": { "id": "<uuid>", "sha256": "<hash>" } }`.
  Success returns `caseId`. The store resolves the artifact in the host scope and checks its
  availability and holdout access before inserting the reference. Keys are unique within
  that scope; this command does not overwrite cases or promise idempotent retries.

Both require `RegisterCase`; operator alone cannot import a corpus. Only an evaluator can
import or reference holdout artifacts. The request's classification is corpus data, not proof
of independent holdout quality. Unknown/omitted fields and invalid references fail closed.
The two commands are separate transactions: an artifact may exist without a case if the second
operation fails. It remains reusable evidence, not a completed import. An ambiguous result
requires inspection rather than automatic replay. These operations neither stage conversational
memory nor activate a model; corpus setup alone is not evaluation evidence.

Offline rejection tests passed. Roundtrip, cross-project/workspace, holdout-reference denial
and redaction acceptance are prepared as PostgreSQL tests, not executed in this window.

### One trial per execution command

`EvaluationApplication(PersistedEvaluationTools)` supports stored operations but rejects
`experiment.run` with `execution_binding_required`. The host may instead construct
`EvaluationApplication(NpgsqlDataSource, IReadOnlyList<EvaluationEndpointSettings>)` and
pass it with trusted `EvaluationAccess` to `CommandDispatcher`. Both stored tools and the
existing `CompletionTrialExecutor` then use the same datasource. Endpoint settings are
snapshotted at construction; credentials are not read by construction.

The only accepted run payload is `{ "experimentId": "<uuid>" }`, under the existing
version-1 envelope with `operation: "experiment.run"`. It requires `Evaluate`; an operator
budget approval is not inference authority. The request cannot select endpoints, credentials,
scope, role, profile overrides or budgets. Durable admission and monetary approval remain
the executor's responsibility, not an implication of constructing the application.

Each command attempts **one pending trial**, not the entire experiment. Success returns
`trial_completed` with its artifact reference, or `no_pending_trial`; the latter does not
certify experiment completion. This is not request-level idempotency: another explicit run
may claim the next pending trial. Do not automatically repeat an ambiguous command. Claimed
execution failures return sanitized trial identity, `stopRecorded`, and
`reconciliationRequired=true`, with persistence `unconfirmed`; they do not release reserves
or invent non-execution. No automatic retry, worker loop or production activation is added.

The standalone entrypoint remains unbound. PostgreSQL/controlled-HTTP acceptance of this
composition is prepared and compiled, **not executed** in the current offline window.

Build:

```powershell
dotnet build src/Cortex.Evaluation.Cli -c NativeMemory
```

Send one JSON request on standard input:

```powershell
'{"contractVersion":1,"operation":"revision.hash","payload":{"contractVersion":1,"kind":"example","payload":{"a":1}}}' |
  dotnet src/Cortex.Evaluation.Cli/bin/NativeMemory/net10.0/Cortex.Evaluation.Cli.dll offline
```

Stdout is one JSON response. Exit code 0 means the requested calculation completed; 2 means
the request failed. A successful calculation is not model-quality approval or production readiness.

## Implemented operations

| Operation | Payload | Result |
|---|---|---|
| `revision.hash` | `RevisionDocument` | Canonical SHA-256 |
| `profile.validate` | `ConfigurationProfile` | Structural issues and hash when valid |
| `profile.prepare` | `{ instruction: FlowInstructionSnapshot, settings: ConfigurationProfile }` | Profile bound to inspected prompt/schema, hash, `executionAuthorized=false` |
| `cost.project` | `{ usage: ProviderTokenUsage, price: PriceSnapshot }` | Nullable list-price estimate |
| `comparison.preview` | `{ candidates: string[], cells: EvaluationCell[], trials: ComparisonTrial[] }` | Descriptive summary and observed Pareto set |
| `assessment.preview` | `{ output: string, criteria: FieldCriterion[], manualCriteria: string[] }` | Enum/boolean grading and explicit unscored manual criteria |
| `instruction.describe` | `{ project: SessionFlowProject, stateId: string, entryKind: "dm" or "group", nodeId: string }` | Effective system text, schema, inputs and node configuration, using the runtime compiler/composer without execution |
| `experiment.preview` | `{ manifest: ExperimentManifest, cases: ScopedRevisionRef[] }` | Deterministic schedule, minimum call count; no dispatch authority |

Contracts live in `Cortex.Evaluation.Contracts`, `.Execution` and `.Comparison`. Profile
validation does not verify credentials, endpoint availability or remote model identity.
Comparison inputs must already refer to the same dataset, evaluation policy and paired schedule;
the preview is a calculator over caller-supplied data, **not** a trusted stored-evidence report.
No private database records are loaded or exported.

Assessment preview accepts criteria such as
`{ "id": "participation", "field": "acao", "acceptedValues": ["calar", "esperar"], "critical": true }`.
Values are compared without coercion: boolean `false` is not string `"false"`. Missing fields,
ambiguous duplicate JSON keys and malformed typed output fail the specified deterministic criteria.
An empty rubric cannot certify a response. Put voice, summary fidelity and other semantic checks
in `manualCriteria`; they remain `unscored` until separately reviewed. This does not validate the
entire provider schema or prove all five conversation-quality dimensions. Criteria are caller-supplied
preview data, not trusted persisted assessments or authority to promote a model.

Instruction inspection requires the complete project document in the request. It never opens a
file path supplied by the caller, constructs a session runtime or dispatches a message. Context flags
are honored by the same `InstructionContext.Compose` used in session execution: enabled personality,
state guidance and interests are included; disabled sections are not added. The selected node must be
a prompt reachable from the requested DM/group entry. This surface exposes local project text, so its
output belongs in experimental artifacts, not ordinary production logs. It does not generate the
upstream model outputs required as inputs to later steps, or prove provider capability/quality.

Profile preparation replaces the template's primary prompt/schema with the inspected instruction
and records project revision/state/node provenance. Model, endpoint alias, prices (external), explicit
parameters and choice overrides are not silently selected or changed. For Jev/Laya, only a closed
required enum/boolean schema is eligible. Free `resumo` needs an explicit `typed-with-summary` recipe
and summary model/prompt; its schema is derived from the original summary field. Other free generation
is rejected, not imitated. Voice therefore requires a generative candidate. Choice-question overrides
remain explicit and can replace per-field instructions; do not call such a comparison prompt-equivalent.
Preparation uses caller-supplied snapshots; persistence must still verify scope/references before any
execution. It is not deployment, endpoint readiness, token-fit verification or benchmark evidence.

Schedule preview uses the pinned seed and revision references, pairs baseline/candidates,
rejects foreign-scope or duplicate cases, and checks the minimum one-call-per-trial budget.
Composite recipes can need more calls; preview does not replace per-physical-call reservation.

Every response includes `contractVersion`, `success`, `persisted=false`, `result` and `errorCode`.
Unknown operations/fields, duplicate JSON keys, unsupported versions and requests above 1 MiB
are rejected. Errors do not echo inputs. A role supplied in JSON grants no authority.

`experiment.run`, `deploy`, `promote`, arbitrary SQL and shell execution are not exposed.
Durable experiment/application commands remain pending the store/ledger implementation and
integration checks after the host migration. Do not use these previews as a substitute for that gate.
# Synthetic step input preparation

## Host application persistence boundary (not yet exposed by CLI)

`PersistedEvaluationTools.InvokeAsync` receives host-created `EvaluationAccess` and a host datasource;
neither can be supplied in JSON. It supports `revision.import`, `revision.show`, `experiment.plan`
and `experiment.cancel`, plus `experiment.status`, `experiment.quarantine`, `assessment.add` and `case.list`. Requests use the existing version-1 envelope. Plan payload is
`{experimentId, manifest, cases}`; cancel payload is `{experimentId}`. Revision operations take the
revision document/reference directly. Roles, unknown fields and duplicate JSON keys are rejected.
`persistenceStatus` distinguishes `not_attempted`, `confirmed`, `read_only` and `unconfirmed`.
An unconfirmed result does not authorize retry or claim no write happened. Errors contain fixed codes,
not connection strings or backend exception text. This surface has no model execution or deployment
operation. Its database integration test remains pending during migration; CLI still accepts only
the offline mode described below.

`experiment.status` takes `{experimentId}`. The host grants `ReadExperiment` to evaluator
and operator roles, not the proposer. The operation executes one scoped SELECT and returns
stored state, cancellation, trial/attempt counts, retained reservations and accounting flags.
Unknown states remain visible and require reconciliation. Multiple attempts on one trial cannot
hide another completed trial without observed attempts. An in-flight call is pending, not evidence
of a crash by itself. No leases, reservations or work are mutated by this read.
`observedCost` remains null: reserved cost is not a bill. `qualityStatus=not_assessed` regardless
of accounting completeness. No manifest, prompt, outcome payload or holdout schedule is returned.

`assessment.add` accepts the existing `Assessment` payload; only the host-resolved evaluator
role can append it. All reference shapes are validated before connecting, then existing scoped
foreign keys bind evidence, case, trial, evaluator revision and supersession within the transaction.
`evaluator_principal` comes from the host, not JSON. Evidence arrays are copied before the first
await. The result returns `assessmentId`; it neither scores a model automatically nor activates it.
An uncertain write must not be blindly repeated; this append operation does not yet provide an
idempotency key. Both operations' PostgreSQL acceptance tests are compiled but not executed.

`case.list` accepts `{limit, after?}`, where `limit` is 1–100 and `after` is an optional
nonempty case UUID. It returns `{items, nextAfter}` with case metadata and artifact references,
never artifact bytes. Scope and holdout permission come from host access on every page;
JSON cannot override either. Redacted and unauthorized holdout rows are filtered before the
page limit, without an all-corpus count that would expose hidden cases. The operator role
does not gain corpus access. A cursor is only a sorting position, not an access grant.
Pagination uses PostgreSQL UUID ordering and is not a cross-request snapshot/export guarantee:
concurrent changes may alter later pages. Reading an artifact still revalidates access/redaction.
The PostgreSQL pagination/isolation test is compiled but not yet executed.

### Explicit recovery quarantine

`experiment.quarantine` is operator-only and accepts
`{experimentId, trialId, expectedOwner, expectedGeneration, decisionId, evidence:{id,sha256}}`.
Evidence must already exist in the same scope, be unredacted and have kind `recovery-evidence`.
The operation references metadata only; it grants no holdout-content access to the operator.
This is an operator-authorized decision to fence work, not automatic verification of a provider receipt.

Under parent-then-trial row locks it checks the exact owner/generation and a started/uncertain trial,
increments its generation, clears its owner, marks trial/experiment indeterminate, requests cancellation
and appends a scoped audit record atomically. It never changes attempts, outcomes, reservations,
stored outputs or pending trials. It does not reset work to pending or invoke a provider.
Already-admitted remote work may still finish externally; fencing cannot undo submission or charges.
The obsolete worker cannot book another attempt or finalize the fenced claim in the ledger.

`decisionId` is explicit and idempotent for identical bindings/evidence/principal. Different data with
the same identity is rejected. `alreadyRecorded=true` acknowledges the earlier decision, not current
liveness/permission to run. Query status separately. An uncertain commit can be reconciled by repeating
the same decision identity; never manufacture a new identity as an automatic retry.

This is the **quarantine stage**, not full accounting settlement: reconciliation remains required,
cost remains unknown where unknown, and no reservation is refunded. Provider-evidence settlement,
authorized re-execution and final closure remain separate pending work. Database acceptance for
concurrent identical decisions, reopening, stale-owner denial and retained work is prepared but unrun.

`comparison.preview` also returns `cases`: one entry per scheduled case revision, containing each
candidate's observed/expected counts, gates, correctness, descriptive interval, latency and known cost.
Cases with no observations remain visible as incomplete, with unknown metrics. This breakdown does
not change the overall gates or authorize promotion; it helps locate regressions hidden by averages.

The optional `pairedPolicy` field on `comparison.preview` enables paired case-cluster bootstrap:

```json
{"contractVersion":1,"baselineRevision":"<candidate revision>","seed":42,"samples":1000,"minimumCases":5}
```

These values are example preview inputs, not an approved experiment policy. Persist/pin the
policy with the comparison evidence before relying on it for a decision. Contract 1 permits
100–10,000 bootstrap samples and requires at least two cases as a structural floor; that floor
does not establish statistical adequacy. The host's chosen policy can require more cases.
Work products above 10,000,000 sample × case × candidate units are rejected.

`pairedDifferences` reports candidate-minus-baseline correctness, balanced equally across cases.
Repetitions are averaged within a case; both candidates share each sampled case. This differs
from the existing trial-weighted overall accuracy when cases have different repetition counts.
The interval uses nearest-rank 2.5/97.5 percentiles of cluster bootstrap means. Cases are sorted
ordinally before sampling. The result records policy and method
`paired-case-percentile95-system-random-net10-v1`; exact reproduction also requires retaining
inputs, results and the .NET runtime version, not merely the seed across future runtime changes.

Incomplete/unscored/error/critical evidence yields `ineligible_evidence` and no interval.
Too few distinct cases yields `insufficient_cases`; additional repeats do not increase this count.
The interval is descriptive, not a claim of independent holdout, population representativeness,
multiple-comparison correction or model superiority. It does not replace eligibility gates,
change Pareto selection, invent missing costs, persist results or activate a configuration.
Requests without `pairedPolicy` retain their previous behavior and return no paired differences.

Offline operation `case.prepare.synthetic` accepts `caseId`, `context`, `selfActorRef`,
`instruction` (from `instruction.describe`) and an explicit `supplementalFixtures` object.
It returns `prepared.input` (`completion-input` revision), `prepared.provenance` and
`executionAuthorized=false`; nothing is persisted or dispatched.

Conversation fields use the existing corpus mapper and runtime model serialization. Only declared
input names are included. Missing downstream inputs require explicit synthetic fixtures; the tool
never derives them from rubric answers. Fixtures cannot overwrite conversation fields, contain
duplicates or add undeclared fields. Provenance records their names and marks `runtimeReplay=false`.
This is preparation, not validation of port value types or a substitute for real upstream execution.
The public development corpus must not be presented as a private holdout.

### Persisted investigative decisions (host library only)

`PersistedEvaluationTools` accepts `decision.add` for a host-resolved evaluator.
The offline CLI does not expose it or resolve database credentials. Payload:
`contractVersion=1`, nonempty `decisionId` and `experimentId`, `outcome`,
`rationale`, `reopenCondition`, and 1–64 unique evidence artifact references
(`id`, `sha256`). Outcomes are `no_change`, `investigate`, or `recommend_candidate`;
none authorizes activation, execution, retry, or budget settlement.

The experiment and unredacted evidence must exist in the host scope. Evidence
references are validated and ordered; author identity comes only from host access.
Reasons and reopening conditions are private decision content, not ordinary logs.
The decision and metadata-only audit entry are appended transactionally. Repeating
the same decision ID, author and normalized content returns a historical receipt;
different content conflicts. This receipt does not renew evidence availability
(which may have been redacted later) or authorize applying the recommendation.
Uncertain persistence remains `unconfirmed`; retain the same ID for reconciliation.

Offline authorization/validation tests pass. PostgreSQL concurrency, isolation,
idempotency, redaction and no-activation acceptance tests are compiled but unrun.
Issues prioritization and persisted comparisons remain separate unfinished work;
this operation is not completion of the workbench.

`decision.list` is evaluator-only and accepts `limit` (1–100), optional `after`
UUID and optional `experimentId`. Scope/role never come from its payload. UUID
keyset pagination is a traversal order, not chronological ordering or a stable
snapshot across pages. Each page uses one scoped SQL statement, including current
artifact availability checks. No payload from the evidence artifacts is read.

Entries include decision/experiment IDs, author, `evidenceStatus` and an optional
decision. When references are missing, foreign, hash-mismatched or redacted,
`evidenceStatus=unavailable` and `decision=null`: rationale, reopening condition
and evidence refs are withheld. Unrecognized/malformed decision records also
withhold content (`invalid_record` where parsing is reached). Oversized records
are unavailable. An available result is only availability at the query snapshot,
not approval of its reasoning, continued access, or permission to activate.
Proposers and operators cannot obtain holdout-derived conclusions through this
surface. This withholding is not erasure of the private append-only decision or
copies previously returned; retention/export policy remains separate work.

Read-side PostgreSQL scope/filter/pagination/redaction tests are prepared but
unrun; no transactional/privacy acceptance claim follows from compilation.

### Explicit corpus-to-input association (host library only)

`case.bind_input` accepts `bindingId`, `caseId`, `caseArtifact` (`id`, `sha256`),
`inputRevision` and `provenanceRevision`. Only a host evaluator can register it.
Migration 005 adds immutable, scope-bound records with composite foreign keys;
it is prepared in source, not applied in this session. Existing migrations remain
unchanged. No legacy case is automatically associated by name, order or content.

The service verifies the case's exact artifact, availability and revision hashes.
Input kind must be `completion-input`; provenance kind must be
`completion-input-provenance` and its `inputSha256` must match. The origin is always
`evaluator_declaration`: this records an explicit association, not proof that a
preparer ran or that source content semantically entails the prepared input.
Imported provenance cannot claim greater authority through its own text.

Same ID/author/content returns a historical receipt; conflicts are not overwritten.
Separate associations use separate IDs; consumers must select one explicitly and
must reject ambiguity rather than infer a default. Consumers must revalidate the
current case/artifact relationship and redaction before reading or comparing.
This registration alone does not authorize a trial, certify a comparison or enable
execution. Prepared PostgreSQL tests cover concurrent repetition, foreign scope,
wrong artifact, immutable records and redaction; they have not been executed.

### Persisted trial measurements (host library only)

`trial.measurement` takes `experimentId` and `trialId`, requires a host evaluator,
and reads trial/result/attempts in a read-only repeatable-read transaction. It does
not claim work, dispatch a model or mutate accounting. Foreign/mismatched IDs
return `not_found`. All physical attempts are included, not one representative call.

The result returns state, artifact availability, measurements and physical-call
metadata; it does not expose completion output or request bodies. Pipeline elapsed
is used only for a completed trial with a hash/revision-validated result artifact.
Missing elapsed is invalid, not zero. Redacted/missing result bytes leave elapsed
unknown. Completed trials without complete physical-call accounting are rejected.

Usage totals remain unknown if any component is missing. Every attempt is counted,
including errors/indeterminate calls. No price is inferred from reservation or a
provider name: this reader currently returns unknown monetary cost and preserves
usage for a future explicitly pinned price projection. The pure `TrialMeasurement`
aggregator supports pre-resolved estimates but does not establish their model/tier
compatibility or prove a query supplied every attempt. Neither layer judges quality.

PostgreSQL acceptance covers two physical calls, pending work, exact scope and
experiment, unchanged accounting, private-output withholding and redaction. It is
compiled but unrun; the consistent-snapshot guarantee still requires DB acceptance.

### Trial assessment selection (host library only)

`trial.assessment` accepts explicit `experimentId`, `trialId`, `bindingId` and
`criterion`; only a host evaluator may call it. The reader revalidates the
case-input binding against the trial's input revision and the case's current,
unredacted artifact. The evaluator revision comes from the stored experiment,
not the request. A missing/foreign/mismatched binding returns `not_found`.

Selection method `unique-terminal-supersession:v1` uses the complete history for
that case/trial/criterion (including evaluator revisions, not only the expected
one). Exactly one terminal assessment must remain. Multiple terminals are
`ambiguous`; missing predecessors are `incomplete_lineage`; cycles are
`invalid_lineage`. An unsupported terminal evaluator is
`evaluator_revision_mismatch`. No timestamp, favorable outcome or fallback to an
older assessment resolves these states. A history larger than 10,000 entries is
rejected, never silently truncated into an apparent unique terminal.

The selected assessment must explicitly reference the trial's result artifact,
and all its evidence must remain available. Otherwise selection is
`evidence_unavailable`. A resolved outcome may still be `unscored` or `fail`;
severity is preserved, not converted to a hard-coded critical threshold.
Unresolved selections return no outcome. Output contains IDs/selection metadata,
not artifact or completion contents. Binding origin stays `evaluator_declaration`.

This is one criterion's evidence selection, not policy/rubric completeness, an
execution-completion check, a persisted comparison or permission to activate.
The pure `TrialComparisonAssembler` now pins a criterion policy and combines
required criteria with execution/measurement gates. It is not yet a persisted
comparison operation. PostgreSQL integration remains unrun.

Decision acceptance must use the restricted `evaluation_app` connection, not the
migration owner. Evidence checks do not take row locks or grant artifact UPDATE.
Concurrent redaction may remove evidence after validation; the decision records
past observed evidence, not a retention lease. History reads withhold derived
content when evidence is unavailable. The role regression is prepared but unrun.

### Policy-bound comparison assembly (library only)

`TrialComparisonAssembler.Assemble` combines already-resolved trial measurements
and assessment selections with an explicit expected schedule and a hashed
`trial-comparison-policy` revision. The policy pins the evaluator revision and
enumerates required criteria, including which failures are critical. It does
not infer criticality from severity or choose the most favorable assessment.

Each assessment must identify the same experiment/trial, explicit case-input
binding and criterion. Missing, ambiguous or mismatched evidence prevents trial
eligibility. Incomplete trials remain visible in diagnostics and in the expected
denominator; omitting them from statistical rows cannot make a candidate pass.
Criterion outcomes/severity and the recalculated measurement remain in the report,
including for gated trials. Completion text and artifact content are not included.

Logical trials, every physical attempt and selected assessments cannot be reused
as independent evidence. Multiple calls and multiple criteria still produce one
statistical observation per trial. Unknown cost/currency remain unknown, not zero
or a default currency. Existing scalar-attempt previews remain compatible, but
only validate the evidence they were given; they do not prove source completeness.

This assembler does not authorize access or execute queries, providers or effects.
The library operation below now composes those readers under one transaction.
The library now offers the saved-report operations below. PostgreSQL acceptance
and the operational CLI transport remain incomplete; automatic configuration
activation is not supported.

### `experiment.compare` (scoped read-only library operation)

Request fields are `experimentId`, `policyRevision` and ordered `inputs`, each with
an `inputRevision` and an explicit `bindingId`. Only the host evaluator role may
call it. The original ordered input revisions plus the stored manifest must
reproduce the experiment's `plan_sha256`; the complete stored trial schedule must
then match regenerated ordinals, keys, candidate/input revisions and repetitions.
The operation never guesses the original input order from trial order. Historical
plans without sufficient explicit data remain unavailable for comparison.

The comparison policy is an explicit revision separate from the manifest's
execution policy. It must use the experiment's pinned evaluator. Its source bytes
and hash are verified, as is the supported typed policy representation.

`ExperimentComparisonReader` uses one read-only repeatable-read transaction for
the manifest, policy, schedule, measurements, case bindings and assessments.
Internal reader composition does not open additional connections or transactions.
Foreign/missing/redacted case bindings return `not_found`; unavailable result or
assessment evidence prevents eligibility. The response contains measurements and
criterion outcomes, never private completion/case payloads. It performs no writes,
inference, activation or lease claim. Monetary cost remains unavailable without
applicable explicit price bindings; reservations are not treated as expense.

This is implemented and offline-tested source, not PostgreSQL acceptance. The
prepared integration test has not run. In particular, concurrent redaction and
transactional isolation have not been empirically certified. A read snapshot is
past observed evidence, not a retention lease or permission to reuse it forever.

### Explicit price projections

`experiment.compare` optionally accepts `prices`: pairs of `attemptId` and
`priceRevision`. Bindings are unique per physical attempt. Missing bindings leave
cost unknown; bindings to attempts outside this experiment are rejected, not
silently ignored. Each scoped revision must contain hashed `attempt-price-terms`.
These are explicit evaluator declarations, not prices discovered from model names
or billed amounts. The provider field identifies the recorded adapter, not proof
of the vendor/account behind an OpenAI-compatible endpoint.

Terms bind provider, observed model, exact request hash, validity period
`[ValidFrom, ValidUntil)`, inclusive input-token range, currency, source URI,
capture time and rates. A non-null service tier requires matching observation;
a null tier explicitly declares tier-independent rates and never invents a
standard tier. `CoversAllBillableComponents=false` prevents a numeric total.
No URL is fetched. These declarations do not grant execution or budget authority.

The reader resolves price revisions inside the same read-only transaction as the
experiment evidence. Projections reuse `CostProjection` for token arithmetic;
HTTP failures/unresolved calls, missing provenance/usage, mismatched terms or
incomplete billing components remain unavailable. Local processing is not priced
at zero merely because no remote provider billed it.

Response `priceProjections` retains attempt ID, full price revision, estimate and
`origin=evaluator_declaration`. Repricing returns a new analytical result; it does
not update attempts, reservations, previous reports or provider billing. A failed
projection's diagnostic is retained; cost/currency aggregation remains conservative.
PostgreSQL acceptance for the new composed projection is prepared but unrun.

### `comparison.save` / `comparison.show` (library only)

Migration `006_comparison_reports.sql` is prepared and embedded, **not applied**.
Existing migration contents/checksums are unchanged. It adds scoped immutable
report rows and composite foreign-key links to source artifacts. Application
privileges are SELECT/INSERT only on these new tables; no artifact UPDATE grant.

`comparison.save` accepts `reportId` and `comparison` (the same explicit request as
`experiment.compare`), not caller-supplied report content. The host evaluator
generates the report using one repeatable-read transaction and stores the report
and source links atomically in that transaction. No inference or activation is
reachable. Body size is limited to 1 MiB. Sources include case artifacts, trial
results and all assessment-history evidence consulted, not only winning judgments.

Same scoped ID, author and normalized request returns the original historical
receipt. A different request/author is rejected. To capture changed assessments or
prices, use a new report ID. Concurrent insertion/commit ambiguity is not retried
automatically; real concurrency acceptance is pending. A receipt does not renew
content access after redaction.

`comparison.show` requires a host evaluator and explicit report ID. It rechecks
all linked source artifacts in one scoped query, then verifies payload hash,
envelope version/method, experiment/policy binding and the exact dependency list.
Missing/redacted sources yield `evidence_unavailable` with no report content;
invalid payloads yield `invalid_record`. Foreign IDs return `not_found`.
Reports are not generic artifacts, so `GetArtifactAsync` is not an alternate path
around those dependency checks.

Saved conclusions describe evidence observed at creation, not current correctness,
a retention lease or permission to activate a candidate. Withholding content is
not physical erasure of private stored report bytes or copies already exported.
Dedicated purge/retention operations remain future work; no purge is enabled here.
PostgreSQL roundtrip, redaction, transaction and role acceptance is prepared and
compiled, but **not executed**. Offline green tests do not certify these guarantees.

### `issues.list` (one explicitly selected saved report)

An evaluator supplies `reportId`. The operation reuses `comparison.show` and its
current evidence-access checks; unavailable/invalid reports retain that status
with no issue content. It never silently converts unavailable evidence to a
successful empty analysis. No artifacts, full prompts or completion text are returned.

Summaries are separate for each candidate revision and criterion. They show
assessed/failed trials, distinct failed input cases, critical failures, unscored
and unresolved counts, maximum observed failure severity, and references to failed
trial/assessment/case/repetition evidence. Repetitions are not independent cases.
The descriptive failure rate uses only pass/fail assessments; unscored/unresolved
counts remain visible. This is historical analysis of that report, not current
re-evaluation of all project evidence or an independent holdout claim.

Ordering method `critical-failures,cases,failures,severity,identity:v1` sorts by
those four descending factors, then candidate/criterion ordinal identity. There
is no opaque composite score or automatically scheduled fix. Maximum severity
ignores unavailable values; an absent severity is not zero. Cost is the estimated
cost of failed trials where fully known in one currency, not projected savings,
fix cost or monetary impact of the error. Criteria can overlap on the same trials,
so costs across issue rows must not be summed as an experiment total.

This first query uses one explicit report to avoid mixing policies, repricings or
duplicated observations. Cross-report investigation history requires explicit
compatibility/deduplication before aggregation; it is not implemented here.
Existing `decision.add/list` records subsequent human/evaluator judgment and
reopening conditions. This query neither edits those decisions nor activates a
configuration. PostgreSQL end-to-end acceptance remains unrun.

### `bundle.export` (private summary, library only)

An evaluator supplies one `reportId`. The operation first performs the same scoped
and source-availability read as `comparison.show`, then returns a versioned
`comparison-summary-bundle` document and canonical SHA-256. Unavailable or invalid
evidence produces no document/hash; foreign IDs remain `not_found`.

The allowlisted payload contains host scope, source report ID/hash, experiment and
plan references, policy revision, statistics, issue summaries, price projections
and artifact references. It contains no source artifact bytes, completion text,
prompt documents, credential data or provider configuration. It is classified
`private_evaluation_evidence`, not public/anonymized data: labels, counts and IDs
may still be sensitive. The consumer must protect any copy it chooses to store.

The operation returns JSON only: no filesystem path, publication, service restart,
configuration import or activation. `activationAuthorized=false` is descriptive;
consumers must never treat any bundle field as authority. Hashes prove content
identity, not authorship, permission or scientific validity. The pure packaging
helper assumes the scoped store read already occurred and is not a substitute for
that access check.

Full private-content export, executable candidate/configuration packages and
cross-report bundles are not implemented by this operation. Previously exported
copies cannot be recalled by later source redaction. PostgreSQL export/redaction
acceptance is prepared and compiled, not executed.

### `experiment.accounting` (read-only reconciliation)

An operator or evaluator supplies `experimentId`; a proposer cannot read this
operational accounting view. One read-only repeatable-read transaction loads
parent reservation totals and every scoped physical attempt. It returns reserved
totals, observed input/output tokens, unresolved attempts, reservation mismatches
and token overruns, with attempt references. No prompt or completion is loaded.

Unknown usage makes that observed total unknown, not a partial or zero total.
`conservativeExposure` retains the larger parent/attempt reservation and raises
token exposure for observed overruns. It is not a proved maximum provider bill.
The original parent and attempt totals remain separate for diagnosis; the reader
does not repair discrepancies or release capacity. Duplicate/invalid rows or
overflow fail closed; more than 100,000 attempts rejects the report rather than
reporting truncated totals.

`monetaryStatus=not_reconciled_currency_and_billing_unavailable`: current manifest
budgets/reservations do not carry a monetary currency, and analytical price
projections are not billing receipts. Do not release monetary reservations based
on this output. Money settlement needs a versioned currency/billing-evidence
contract; old manifests must not be silently interpreted as USD or rehashed.
No reconciliation here authorizes retry, completes a trial or resumes a worker.
Transactional acceptance remains pending; the operation has only offline/source
validation at this checkpoint.

### `experiment.budget.approve` / `experiment.budget.show`

These are persisted tool-library operations, not an enabled execution route or
a deployed CLI database connection. Approval requires host `Operate` authority;
read requires `ReadExperiment`. The request contains `contractVersion=1`,
`approvalId`, `experimentId`, `planSha256`, `currency` and `maximumCost`; every
field is required. No field grants authority or selects a scope.

The store resolves the scoped experiment under `FOR UPDATE`, the same first lock
used by claims and physical attempt admission. It validates the stored manifest,
exact plan hash and original ceiling, absence of cancellation, planned state,
zero attempts and zero reservations. It records an immutable operator declaration
with one approval per experiment. Currency is explicit uppercase three-letter
syntax, not a claim of provider billing support. Existing manifests are unchanged.

The same ID/content/author returns a historical receipt, even after a later claim;
this does not renew permission. A different declaration conflicts rather than
overwriting it. Uncertain commits are not retried by the store; reconcile by scoped
read or the exact original request. `show` returns only the stored declaration,
not a claim that execution is currently authorized or cost has been settled.

Migration **007 is prepared, not applied**. Cross-scope, immutable, idempotence and
claim-race PostgreSQL tests are compiled but unexecuted. No integration guarantee
is inferred from offline tests. Currency-aware admission and cancellation-based
revocation are implemented in source as described below; billing reconciliation remains pending.
These operations alone do not make the workbench ready for paid execution.

### Monetary binding at the physical-call boundary

`EvaluationEndpointSettings.ReservationCurrency` is required host configuration;
there is no default currency or implicit conversion. `AttemptLedger.BeginAsync`
loads the immutable monetary approval under its existing experiment/trial locks,
checks scope, experiment, plan hash, ceiling and reservation currency, then runs
the existing cancellation, lease, unresolved-attempt and budget checks. Only then
can it insert/commit the physical attempt. The HTTP boundary already waits for
that commit before sending. A missing or mismatched declaration admits no call.

Migration **008 is prepared, not applied**. New attempts record approval ID and
reservation currency with a scoped composite foreign key. Historical rows keep
NULL monetary provenance; no migration invents approval/currency. Old unapproved
plans cannot admit new calls. Final accounting for a previously submitted attempt
does not require a new approval, so an additional admission requirement does not
erase a late result. The approval is one prerequisite, not permission to ignore
current cancellation, lease, host authority or budget exhaustion.

Offline binding/host-configuration tests pass. Existing PostgreSQL fixtures now
create explicit operator approvals before claims. Missing/mismatched approval,
zero reservations on rejection and stored currency/provenance tests are prepared
and compiled but not run. Live billing, settlement and provider spend caps remain
unproven. Analytical cost estimates are still not billing receipts.

### Revoking further admission

For this version, revoke further use of a monetary approval through
`experiment.cancel`, not by changing or deleting the historical approval.
Host evaluator and operator roles have the narrow `CancelExperiment` operation;
the proposer does not. This does not grant the operator `Evaluate` or artifact
read authority. The explicit experiment is resolved in the host scope.

Cancellation locks the experiment row, shared with claim/attempt admission, then
sets `cancel_requested` and the applicable terminal state and appends an audit
record in the same transaction. Concurrent repeats observe cancellation and do
not append duplicate audit events. Earlier cancellations lacking an audit event
are not retroactively attributed to the new caller.

Successful cancellation blocks subsequent admissions. An already admitted or
submitted request may still finish; cancellation is not an atomic remote abort,
unsend, refund or proof of zero consumption. Late observations remain recordable,
and reservations are retained. There is no automatic reactivation/reset or retry.
A historical approval receipt after cancellation does not reopen the experiment.

Role guards are tested offline. Real scoped cancellation, concurrent repeats,
audit atomicity, rejection of further attempts and late accounting are represented
in PostgreSQL acceptance tests, compiled but not executed in this window.

### `attempt.charge.declare` (operator attestation; partial reconciliation)

This append-only operation records an operator's interpretation of scoped
`billing-evidence`, not an automatically verified provider invoice. It requires
`Operate` and an explicit versioned declaration: decision, experiment, trial,
physical attempt, original owner/generation, monetary approval, currency, charge
and evidence ID/hash. Model estimates and price projections are not accepted as
implicit declarations. Evidence content is not returned or logged.

The store locks experiment, trial and attempt in admission order. It checks the
actual attempt binding, evidence metadata/kind/availability and original monetary
provenance. An observed/indeterminate attempt is eligible; a still-started attempt
requires cancelled, indeterminate trial state with a newer fencing generation.
This accommodates crashed work without rewriting the raw attempt as a success.
Billing does not resolve execution, recover a model result or authorize retry.

The same decision/content/author returns a historical receipt. A conflicting
declaration for the same attempt is rejected; superseding a mistaken declaration
is not implemented yet. Redacted evidence prevents new declarations, but does not
erase the receipt of a prior decision. Metadata existence does not verify the
truth of the operator's interpretation.

Charges greater than the original reservation stop further admission by setting
experiment cancellation/indeterminate in the same transaction. Lower or zero
charges never refund reservations or prove that a call did not execute. Original
reservations/outcomes are untouched. The accounting reader now joins declarations
as described below; this remains **not a settled billing report**. Correction,
final settlement and authorized resumption remain unfinished. No operational
paid-execution readiness is claimed.

Migration 009 is prepared, not applied. Policy/authority tests run offline;
scoped evidence, concurrent idempotence, overrun stopping and raw-history tests
are prepared and compiled but PostgreSQL acceptance remains unexecuted.

### Accounting with billing declarations

`experiment.accounting` loads approval, attempts and billing declarations in the
same read-only repeatable-read transaction. It revalidates the attempt's original
owner/generation, approval/currency and declaration/evidence bindings. Artifact
content is never loaded. SQL withholds the declaration payload when its evidence
is unavailable/redacted; the response exposes `evidence_unavailable`, not the
hidden charge. A scoped declaration receipt can remain historically valid while
its evidence is unavailable to a new analytical report.

`billing.declaredTotal` is nullable and appears only when every physical attempt
has an available, same-currency declaration. `knownDeclaredSubtotal` is explicitly
partial otherwise. Missing declarations and unavailable evidence are separate
counts, not zero charges. An empty unapproved plan acquires neither a currency
nor a zero bill. Duplicate attempt/decision IDs and mixed/unknown currencies are
rejected; arithmetic overflow never returns a partial total.

`retainedExposureLowerBound` sums the larger reservation/declaration per attempt
and retains a larger unexplained parent reservation. The existing conservative
exposure view includes this known overrun. Withheld or unknown charges mean it is
only a lower bound, not a maximum provider bill. Redaction does not release stored
reservations or undo cancellation. No projection authorizes refund, retry or
execution success. Totals retain `operator_billing_attestation` provenance, not
provider verification. The original `monetaryStatus` remains unsettled; callers
must not mistake complete declarations for a completed experiment.

Projection/overflow/withholding tests run offline. Readback, overrun and evidence
redaction integration assertions are compiled but await PostgreSQL acceptance.

### CLI dispatcher and host binding

The real executable now delegates stdin/stdout handling to `CommandDispatcher`.
`offline` keeps its response contract and pure tools. Input is bounded by both
characters and UTF-8 bytes; cancellation and I/O errors produce sanitized JSON.
One request produces one JSON line, without echoing private input. Unknown argv
does not select a shell command, role, scope, credential or endpoint.

The dispatcher can receive `PersistedEvaluationTools` plus `EvaluationAccess`
from a trusted host composition. Its `persisted` mode invokes that existing tool
boundary without deriving authority from stdin. The standalone executable does
**not** configure this binding yet: `persisted` returns `host_binding_required`
with `persistenceStatus=not_attempted` before reading input or connecting. This
is not a production authentication mechanism or a complete persistent CLI.
Operational binding/provisioning and the database-backed pipeline remain pending.

Offline tests exercise real pure tools and persisted authorization guards. A real
process smoke ran `revision.hash` successfully and confirmed unbound persisted
mode rejection. No database, credential lookup, provider or runtime was started.

### Prepared end-to-end acceptance and offline selection

`CompletionTrialAcceptanceTests.Bound_cli_and_real_executor_complete_controlled_workbench_pipeline_once`
now composes the bound CLI dispatcher for revision import, plan, monetary approval,
case/input binding, assessment, comparison save/show, decision, accounting and
summary export. Execution uses the actual `CompletionTrialExecutor`, adapter and
durable `AttemptLedger`; only the one-request loopback provider response is fake.
The fixture checks committed reservation before receiving the controlled response,
one physical attempt, no repeat execution, result pinning and export without raw
output. This is plumbing acceptance, not model-quality evidence or settled billing.
The execution entrypoint is host-called, not a implemented `experiment.run` CLI
operation. That operational wiring remains pending.

This PostgreSQL test requires migrations through 009 and has **not been run**.
An older fixture was corrected to explicitly approve currency before its claim;
compilation alone had not established compatibility with the stricter admission.

The separate adapter smoke that opens an ephemeral loopback listener is now tagged
`Category=LocalSocket`. Previous counts included this no-model local fixture;
they did not invoke LM Studio or external APIs. To require no listener at all:

```powershell
dotnet test tests/Cortex.Evaluation.Tests -c NativeMemory --no-restore --filter 'Category!=Postgres&Category!=LocalSocket&FullyQualifiedName!~DatabaseLifecycleTests' --verbosity quiet
```

This selection passed 587 tests; the previous 588 included that one local-socket
fixture. No test was deleted. PostgreSQL, Docker and local-listener acceptance
remain explicit, separate execution choices.
