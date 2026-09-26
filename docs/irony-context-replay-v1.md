# Irony/context replay V1: experimental, not deployed

## Question and approved scope

Does later, actually observed conversation change the interpretation of the four
reported examples: Megabrain/Copythief, “você é woke”, “Moggei você”, and “Agora com
1000”? Understanding a message later does not itself authorize a late reply.

This is a development replay, not a new brain node, participant profile, memory
writer or response scheduler. Filomena remains offline. The separate memory-only
backfill is unaffected.

## Corpus

All four target messages were found in the retained, exact-account/group native
history through the read-only source adapter. The private pinned corpus contains
eight earlier and eight later retained human text messages per target. Earlier and
later are strictly separated by provider timestamp. Same-timestamp neighbors, if
present, are omitted rather than inventing order. Observed provider quotes remain
quotes; their content is not asserted as the current speaker's statement.

Corpus SHA256:
`63a0ddfa60434b37a7a0a9b6e44efc33021b55d67f58b22c332d18bae39f8476`.

The corpus is stored outside git in the restricted host operations directory,
with access limited to Helena and SYSTEM. Speaker/message references are opaque,
but message text is still private and can contain personal information. Do not
describe it as anonymized or publish the surrounding conversation in this report.

Known coverage limitations: deleted payloads, unsupported/non-text content,
account-sent messages excluded by the memory history source, and bounded context.
Missing Filomena replies may materially limit interpretation. The replay declares
these gaps to the analyzer; it does not reconstruct omitted messages.

## Experiment

Two independent reads per target, eight calls total:

1. Initial: earlier context and the target, with no later-message leakage.
2. Reassessment: identical earlier context and target plus actual later messages.

The experimental analyzer returns reading, an optional alternative, qualitative
confidence, visible evidence IDs, a short explanation, and a diagnostic assessment
of whether responding remains relevant. Confidence is not a calibrated percentage.
No conversational response, permanent personality inference or runtime execution
is produced. A response-opportunity label is not a participation decision.

The planned model is GPT-6 Luna medium using the existing `OpenAiFlowCompletion`
client. No local GPU inference is added while memory extraction is running. API
usage and latency are recorded; no unverified price is inferred.

Files:

- `tests/Filomena.Llm.Tests/IronyReplayExperimentTests.cs`: test-only input projection,
  schema, prompt and evidence validation.
- `tests/Filomena.Llm.Tests/IronyReplayExperimentLiveTests.cs`: explicit opt-in model
  experiment, private result files, no runtime/transport/Graphiti dependency.

Local test gate: five tests first failed against unimplemented helpers, then
passed. Full Llm/App test project: 612 passed, 44 skipped, zero failed. Skipped
tests include the replay's explicit external-model opt-in.

## Authorization and observed result (2026-09-26)

The first attempt was rejected before execution because transmission of private
historical excerpts to the external API was not explicitly approved. Helena then
explicitly approved the four excerpts, eight messages before/after, and eight
external GPT-6 Luna analyses. Only after that approval was the experiment run.

Eight sequential calls completed using `gpt-6-luna`, medium reasoning. The opt-in
test passed, including schema and visible-evidence-ID validation. Private inputs,
outputs, prompt/corpus hashes and usage are retained outside git in
`replay-1767b6e5931f4e96a0c1ac2d9457a1a7.jsonl` in the restricted replay directory.

| Case | Initial reading / confidence | Later-context reading / confidence |
| --- | --- | --- |
| Megabrain/Copythief | literal / medium | provocation / medium; literal alternative retained |
| You are woke | provocation / medium | provocation / high; irony alternative retained |
| Moggei | provocation / medium | provocation / medium; reference alternative retained |
| Now with 1000 | ambiguous / medium | provocation / medium; literal alternative retained |

Two primary readings changed; one unchanged reading gained qualitative confidence.
All four response-opportunity labels changed from `still_relevant` to `obsolete`.
These are observed model judgments, not validated participation decisions.

### Evidence review and limitations

The cited later messages were checked against the retained corpus. They support
a playful/challenge interpretation, but do not establish hidden speaker intent.
The Megabrain case later changes topic; those later statements are not independently
fact-checked and must not be treated as proof that the original request was false.
The 1000 case includes a provider quote connecting it to a previous digits-of-pi
response and subsequent challenge reactions. It also includes Helena's explicit
comment about adding sarcasm detection: this is natural contextual evidence, but
it contaminates any claim of independent sarcasm-discovery accuracy.

The initial woke explanation says there is no visible response. That is only a
property of this partial input: account-sent messages were excluded, so absence
of a visible reply is not evidence that Filomena did not respond. The experiment
must not turn that gap into a conversational fact.

One sample per phase, four correlated examples, no blind human labels, no
randomized repeated comparison. Changes may include sampling variance. Schema
success is not semantic correctness; this is a feasibility observation, not a
detector benchmark or a basis for permanent participant profiles.

### Usage and safety

- 8 calls; 13,539 input tokens and 4,160 output tokens (17,699 total).
- Sum of measured call latency: 49.325 s; mean 6.166 s; range 3.880–9.042 s.
- Monetary cost unverified; no price inferred from the model alias.
- This replay made zero transport calls, conversational runtime executions,
  Graphiti calls or memory writes. Separate memory-only recovery is not part of
  these counters and was not changed.
- No deployment or conversational restart was performed.

## Recommended next bounded slice

The result supports testing contextual reassessment while keeping interpretation
separate from permission to reply. Before live integration, use an offline
pending-message replay to verify that new context may revise a hypothesis without
automatically reopening an obsolete response opportunity. Preserve quotes and
speaker identity, label missing history explicitly, and test topic migration and
large intervening-message counts. Do not infer that this queue, its scheduling,
or a live analyzer has already been implemented.

## Offline pending-entry spike (2026-09-26)

Implemented **only in the test assembly** as `PendingIronyReplay` in
`IronyPendingReplayTests.cs`. This is a disposable sequential state-machine probe,
not a deployed queue, durable scheduler, concurrency implementation or brain node.
The question was whether interpretation can evolve without reviving an obsolete
reply opportunity. No production files were changed for this slice.

The probe reserves one analysis per context version, with at most one in flight
per entry. It binds completion to that exact reservation and run instance. Newer
context invalidates an older result. Invalid analysis remains failure, not a
literal reading or permission to reply. Repeated polling/duplicate context does
not schedule more work. A separate handled marker prevents duplicate response
opportunities for an already-handled original.

| Controlled input/event | Observed result |
| --- | --- |
| Ambiguous message, no new context | No reassessment scheduled |
| Clarification, high-confidence interpretation, still relevant | Diagnostic `needs_participation_review`, never send authorization |
| Better interpretation but opportunity obsolete | Interpretation updated; entry closed |
| Topic closed while analysis is in flight | Entry stays closed; late result cannot reopen it |
| 149 then 150 intervening synthetic messages | Experimental distance boundary closes at 150, including in-flight work |
| Context advances while an earlier job runs | Earlier result marked stale, not applied |
| Duplicate completion or completion from another run | Rejected |
| Wrong group/target or changed payload under same ID | Rejected |
| Invalid JSON/schema or fabricated evidence | Analysis failure; no retry on the same context |
| Repeated inconclusive analyses | Experimental budget closes the entry |

### Saved-output replay

An explicit read-only test reapplied the prior private eight-result artifact,
preserving group, target, quoted message, speaker and declared history gaps.
All four pairs closed as obsolete; two readings changed, matching the recorded
experiment. No new inference or history transmission was performed. This proves
state-machine application of those recorded outputs, **not** their semantic
correctness or the accuracy of topic-migration detection.

### Deliberate experimental limits

- Topic closure is supplied by the synthetic driver, not detected by a new model.
- The 150-message horizon and two-attempt budget are configurable probe settings,
  not approved production thresholds. No wall-clock expiration was introduced.
- Low/medium confidence or ambiguity queues an entry in this probe; these are
  qualitative labels, not calibrated probabilities or a final selection policy.
- `needs_participation_review` is only a diagnostic. No automatic late replies,
  generation, participation calls, memory profiles or writes are wired up.
- State is process-local. A new run rejects old jobs; durable recovery, queue-wide
  capacity, fairness, cancellation and real multi-threaded races remain untested.
- Explicit message edits are rejected here rather than silently treated as new
  evidence. Production edit semantics remain a separate integration concern.

### Verification

Initial 15 checks failed against unimplemented probe methods, then passed. An
additional identity-integrity test reproduced acceptance of a changed payload
under an existing ID; the local probe now rejects that case. Eighteen synthetic
checks plus the saved private replay passed. Full `Filomena.Llm.Tests` regression
with only the file-replay opt-in: **631 passed, 44 skipped, zero failed**. Build
includes compiler/analyzer enforcement (`TreatWarningsAsErrors`).

For the saved replay, set `FILOMENA_IRONY_QUEUE_REPLAY_FILE` to the restricted
existing JSONL and run `dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj
-c NativeMemory --filter FullyQualifiedName~IronyPendingReplayTests`. It reads a
local file; do not enable the separate `FILOMENA_IRONY_REPLAY` model experiment.

This slice used zero model calls, zero Graphiti calls/writes and zero transport
calls. It did not start the conversational host or change the memory worker.

Recommendation: retain the two separate concepts (interpretation revision and
current response opportunity). A production integration should consume trusted
session/handled state and the existing participation boundary, rather than letting
the analyzer or an old queue entry dispatch a reply. That integration is not
implemented by this spike.

## Runtime participation review boundary (2026-09-26)

Implemented `SessionFlowRuntime.ReviewParticipationAsync`: a host-selected,
revision-bound projection of the existing compiled participation graph. This is
now production runtime code, but has no live caller or automatic scheduler.
The pending-entry controller remains a test-only replay probe.

The caller supplies an exact session, retained original provider message ID,
current project revision, allowlisted structured decision prompts and explicit
decision exits. The runtime uses the current state and current retained context,
including later messages, without admitting the original again. The original is
excluded from the context list to avoid duplication; its timestamp and observed
quote remain intact. Existing history size/age settings still apply.

The real typed runner executes only entry/history/accumulation, selected decision
prompts, branches/switches and pure property/count dependencies. Every control
path must terminate at a declared decision exit. Generation, memory nodes,
Boundary enforcement, transitions and outbound are outside the accepted cut.
Host metadata explicitly labels this as an old-message reassessment using later
context, not a new inbound. Frozen system prompts are unchanged.

Reviews share session scheduling and shutdown cancellation. A new admission,
state change or project replacement during evaluation invalidates the result.
A busy session, wrong binding/revision, missing retained inbound or unsafe graph
cut is rejected before inference. Failures do not become silence or permission to
respond. Completed results are diagnostic decisions, not reusable send authority.

| Controlled case | Result |
| --- | --- |
| Original plus later clarification | Current context evaluated; no history/readmission duplication |
| Direct involvement in Inactive, Attentive, Engaged | Existing direct route responds; presence prompt bypassed |
| Non-direct involvement | Existing presence graph can respond, wait or remain silent |
| Relevant and unhandled pending entry | Participation review runs; may still choose silence |
| Obsolete or already-handled entry | Replay controller does not invoke participation review |
| Context/project changes during inference | Result discarded as stale; no decision exposed |
| Malformed output/provider failure | Failed result, no response authorization |
| Runtime disposal during inference | Cancellation awaited; retained history preserved |

### Verification and remaining boundary

Initial 18 tests failed against an unimplemented entrypoint, then passed; expanded
coverage contains 23 passing tests using the real v3 graph/runtime and controlled
completion responses. Fresh per-project regressions in `NativeMemory`:

- Core: 621 passed, 10 skipped.
- OpenClaw adapter: 175 passed.
- Llm/App: 653 passed, 45 skipped.
- Total: 1,449 passed, 55 skipped, zero failures.

The solution does not define `NativeMemory`; tests were run per project instead.
These are local controlled tests, not evidence of live model quality. No model,
Graphiti or transport calls were made by this slice. No conversational host was
started, and the separate memory worker was not changed.

Still not implemented: a production pending-message controller, live selection
policy, automatic analysis scheduling, or authorization/generation of late
responses. In particular, `Respond` from review does not send anything. Those
must preserve relevance and handled-state checks at their eventual execution
boundary; this API alone does not make delayed replies safe or enable the bot.

## Host integration source audit (not implementation)

The existing operational path is `NativeConversationHostCommand` ->
`NativeLiveCommand` -> `ObservedLiveRun` -> `ObservedShadowRun` ->
`RuntimeHandoffCore` -> `SessionFlowRuntime`. Filomena is not present in the
available codebase-memory index; these findings were checked in source, not
inferred from graph absence.

Important distinction for the proposed pending controller:

- `ShadowInboundJournal.Completed` records execution success, not a sent reply.
  `RuntimeHandoffCore.DrainAsync` sets it from `SessionFlowOutcome.Success`,
  including successful silence and shadow execution.
- Runtime `speech-committed` records actual provider-accepted speech. It carries
  execution/batch identity. Join it to the same session's outcome provider IDs;
  do not match reply text, sender display names or message adjacency.
- Speech can have been committed even when execution ends in failure (for example,
  a stale transition). Therefore `Success == false` does not prove no reply.
- A batch receipt does not prove the answer semantically addressed every message
  in the batch. Conservatively suppressing another reply for those inputs avoids
  duplicate attempts; it must not be reported as comprehension coverage.
- `node-outbound/result=shadowed` and WOULD_SEND are not speech. They can be used
  as simulation evidence only, never as a claim that the group received a reply.
- Persisted context restores history and seen IDs but not the full outcome/trace
  relationship. A process-local pending controller must not recover old response
  opportunities from history alone after restart.

The available narrow integration point is after the existing normal batch drain,
under the run's serialization boundary. This does not grant send authority or
remove the need to invalidate results when context changes. Implementation of
that controller and operational activation remain separate from this audit.

## Pending controller and shadow-host wiring (2026-09-26)

The approved local integration is implemented as `PendingParticipationReviews`
and opt-in methods on `ObservedShadowRun`. This supersedes the earlier statement
that no production pending controller exists; it does **not** activate a listener,
live analyzer, automatic pending selection or late outbound.

Host sequence:

1. Configure participation observation once on a shadow run, supplying the pinned
   participation cut, an analysis function, message horizon, attempt budget and
   capacity. Enabled/live runs cannot configure this observer.
2. Admit inputs through the existing trusted handoff and ordinary runtime.
3. Explicitly track a retained original after an uncertain interpretation.
4. On the next normal batch drain, the host processes pending originals with new
   context, then invokes the existing participation review only for a confident,
   still-relevant interpretation. `ObservedShadowInteraction.ParticipationReviews`
   reports the result; it is never fed into generation or outbound.

The analysis function receives an atomic current snapshot, original retained
message and retained history. Evidence IDs must occur in that input. Unknown
opportunity values, provider failure and cancellation cannot authorize review.
Inputs include private message content: choosing an external analyzer would need
the appropriate explicit operational configuration; these tests use no network.

State is bounded and process-local. Closed entries remain tombstones within the
configured run capacity; repeated registration cannot reopen them. A fresh
controller restores no pending work. Horizon/budget/capacity are explicit host
settings, not new character rules. Tests use 150/2/10 where not testing limits;
these are not silently installed production values. No polling timer was added.

The controller distinguishes ordinary successful execution from confirmed
speech. A committed reply closes the corresponding original opportunity even
if another lifecycle step failed. An uncertain outbound attempt requiring
reconciliation blocks registration, rather than treating absent speech as proof
of non-delivery. These cases were exercised with a local fake transport only.

### Controlled end-to-end data

All cases use the existing host, canonical inbound handoff, session runtime and
v3 participation graph. Model outputs are controlled fixtures; this measures
wiring and invariants, not sarcasm accuracy. The observation-host fixture changes
only accumulator quiet time to keep local runs short, not prompts or routing.

| Later assessment/current participation | Host result | Extra inbound executions | Observation sends |
| --- | --- | --- | --- |
| Relevant, confident / respond | reviewed: Respond | 0 | 0 |
| Relevant, confident / wait | reviewed: Wait | 0 | 0 |
| Relevant, confident / silence | reviewed: Silence | 0 | 0 |
| Obsolete | closed without participation calls | 0 | 0 |
| Shutdown while analyzer runs | cancelled and drained | 0 | 0 |

Additional tests cover duplicate context/registration, capacity, message horizon,
attempt budget, closed topic, project/context races, malformed evidence, backend
failure, cancellation ignored by an analyzer, confirmed speech, and uncertain send.
Two gaps were reproduced and fixed: cancellation returning as an ordinary result,
and accepting uncertain-send work as an unanswered opportunity. The host also now
cancels/drains its observer before disposing run resources.

Fresh regression results: Core **625 passed / 10 skipped**, Llm/App **671 passed /
45 skipped**, OpenClaw adapter **175 passed**; **1,471 passed, 55 skipped, zero
failed**. Project builds used `NativeMemory`, with warnings-as-errors enabled.
Whitespace verification passed for changed C# files; the formatter reports its
existing workspace-loading warning. No active build was overwritten.

Remaining operational work: bind the real interpretation adapter and initial
uncertainty selection, gather model/replay quality data, then explicitly activate
the reviewed group configuration. Automatic late generation/send is still absent.
No new calls to real models, Graphiti or WhatsApp occurred in this slice, and no
conversational service or memory worker was restarted or modified.

## Interpretation adapter connected locally (2026-09-26)

`Filomena.Llm.Context.ContextInterpretationAnalyzer` now implements the analysis
function used above. Its `ContextInterpretationContract` preserves the exact
experimental prompt/schema and validation rules. It uses the existing
`IFlowCompletion` provider interface; model/provider/effort and context limits
are explicit host settings. No new endpoint, secret, inference runtime, memory
writer or participant-profile store was added.

The adapter checks session/account/group and original identity before inference.
It selects bounded earlier/later retained context by admission order, preserves
observed quotes, declares missing/truncated context, and projects transport and
speaker IDs to session-scoped opaque references. Text remains private, not
anonymized. Output evidence is validated against only the projected messages and
mapped back to trusted internal IDs. A quoted ID alone is not evidence that its
original message was present. Outage remains failure, not an empty/literal result.

Pending results retain the reading, alternative, qualitative confidence,
explanation, opportunity and evidence for **private experimental reports**.
They are not permanent claims about a participant. Stale results expose no fresh
decision/assessment. Interpretation still does not authorize a late response.

The observed-host integration tests now use this real adapter and validator,
with only completion responses controlled locally. Four non-shutdown replay
cases each make one interpretation call; relevant cases additionally execute
the existing participation cut. Obsolete context never reaches that cut. This
is end-to-end local wiring coverage, not new live model accuracy evidence.

Twelve adapter cases cover the qualitative mapping, malformed/foreign/quote-only
evidence, backend failure, wrong account, already-answered target, outbound target
and explicit truncation. Final fresh regressions: **1,483 passed, 55 skipped,
zero failed** (Core 625; Llm/App 683; OpenClaw 175). Compilation and targeted
whitespace checks passed, with the previously noted formatter workspace warning.

Still not activated: automatic initial uncertainty selection, live group
subscription, and delayed generation/send. The opt-in shadow host can now accept
an explicitly selected pending message and execute the full reassessment chain.
No real model requests, Graphiti calls/writes or WhatsApp sends were made by this
work. Do not conflate local passing tests with Filomena being back online.

## Automatic initial selection in the observation host (2026-09-26)

The host can now opt into `analyzeInitial` alongside reassessment. After the normal
batch drains, it selects only the provider IDs of the newly completed batch, not
old history or recovered work. The initial callback receives no messages later
than its target, including when several messages arrived in one batch. The real
adapter independently projects `phase=initial` with an empty `after` array.

Uncertain readings create pending entries automatically; clear or obsolete
readings close their entries. Later context can then invoke the existing
reassessment/participation path. Initial failure, cancellation, changed project,
explicit closure or fabricated future evidence cannot create response work.
Duplicate inputs do not repeat the initial analysis. Capacity is explicit and
bounded for this experimental run; exhaustion is reported, not silently treated
as a clear interpretation. No production capacity/budget default was installed.

`ObservedShadowInteraction.InitialInterpretations` exposes the initial decision
separately from `ParticipationReviews`. One automatic end-to-end test performs:

1. Original initial read: ambiguous/low -> tracked.
2. Clarification initial read: literal/high -> clear.
3. Original reassessment: provocation/high/still relevant -> current participation
   says Respond, but no generation/send is invoked.

The test uses the actual adapter, controller, handoff and runtime with controlled
completion responses. There are three interpretation calls, two ordinary inbound
executions, one diagnostic participation review, no second inbound admission and
no outbound. Six additional controller checks and one adapter check cover initial
temporal isolation, duplicate selection, future evidence, backend failure, revision
change, closure and capacity. Final regressions: **1,491 passed, 55 skipped, zero
failed** (Core 625; Llm/App 691; OpenClaw 175).

Important remaining distinction: this is a post-batch observational mode. It does
not yet feed the initial hypothesis into the normal response generator, suppress
an already-running normal reply, or generate a late response. Those behaviors
must not be claimed from these tests. No live group was subscribed/restarted,
and all inference for this slice was controlled locally.
