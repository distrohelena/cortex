# Irony/context replay V1: experimental, not deployed

## Current status (2026-09-26)

The implementation now supports initial interpretation before the normal flow,
bounded pending entries, later-context reassessment, the existing participation
decision, and a full response preview through the existing memory/generation/
enforcement pipeline. It is available through explicit host-owned configuration
on `--native-shadow`; it is not enabled on live runs or deployed to the group.

The local operational replay reads a literal host JSON document with the same
strict serializer settings as the command, then exercises the real host
configuration and runtime. This covers initial interpretation reaching generation
and later reassessment reaching WOULD_SEND, with controlled model/memory inputs.
The JSON-to-host and observation matrix passed 25 cases. No endpoint from the
fixture is contacted, and no credential is loaded.

Remaining evidence: real-model quality of an actual delayed response and
active deployment. Initial integrated generation and later-context interpretation
have now been exercised with synthetic inputs as recorded below. The eight
historical analyzer calls below only measured
interpretation, not the new integrated response behavior. Local passing tests
do not prove conversational quality or that Filomena is online. The following
sections are chronological; later sections supersede earlier implementation gaps.

### Synthetic real-model check

`ContextInterpretationSyntheticLiveTests` ran six sequential real GPT-6 Luna
medium calls through the production analyzer: three invented messages, each
first without and then with a synthetic clarification. No private history,
Graphiti, transport, or live conversational execution was involved. The local
runtime fixture supplies identity/context structure only; no real conversational
generation was requested. Outputs were inspected after the run, not selected or
retried until a preferred label appeared.

| Synthetic case | Initial | With later context | Response opportunity afterward |
| --- | --- | --- | --- |
| “Nossa, que maravilha”, then explicit irony about lost/recovered draft | ambiguous / low | irony / high | obsolete |
| Same sentence, then sincere happiness about recovered draft | ambiguous / low | literal / high | relevant |
| “Organiza meus próximos mil anos”, then joke/topic closure | irony / medium | irony / medium | obsolete |

All six responses passed the real schema/evidence validator. The two explicit
topic closures were recognized; increased confidence was not forced in the third
case. These easy, explicit clarifications do not establish subtle sarcasm accuracy,
participant-specific knowledge, or integrated response quality. No permanent
speaker profile or memory fact was created.

Usage: 5,739 input tokens, 1,670 output tokens, 7,409 total. Summed call latency
21.669 seconds, mean 3.612 seconds, range 2.571–4.347 seconds. Monetary cost was
not inferred from the model alias. The opt-in defaults to skipped and requires
`FILOMENA_IRONY_SYNTHETIC_LIVE=1` plus an explicit existing output directory in
`FILOMENA_IRONY_SYNTHETIC_OUTPUT`; it never reads the private replay artifact.

The observed run produced `synthetic-context-*.jsonl` under the isolated temporary
directory `filomena-synthetic-context-4d4a6b9c79b941588cc4c7615bab270c`. This check
does not activate the group, validate delayed live sending, or replace the pending
integrated-flow quality assessment.

### Integrated synthetic model run

The next replay uses `manual-group-live-project-v3-natural-v1.json`, not the older
group-shadow artifact. Only accumulator quiet time is shortened for the test;
the resulting artifact is hash-pinned. Its existing model assignments are kept:
Gemma locally for situation/presence, GPT-6 Luna for generation. This does not
claim coverage of the separate operational Jev host overlay.

Updating controlled replay to this artifact first exposed six fixture failures:
the fake generator returned plain text, whereas natural-v1 requires structured
`texto` and nullable `replyTo`. The fake now honors that schema; all 17 controlled
observation cases pass against natural-v1, including regeneration and final Block.
No project, production prompt or runtime behavior was changed for this correction.

`ContextInterpretationPipelineLiveTests` then completed three synthetic inputs
through real interpretation, participation, generation, runtime and SendGuard:

| Input summary | Interpretation | Actual pipeline outcome | Total latency |
| --- | --- | --- | --- |
| Direct explicit irony about an app deleting a draft, no tutorial wanted | irony / high | WOULD_SEND; playful criticism of app, no tutorial | 26.656 s |
| Draft recovered, topic explicitly closed | literal / high, obsolete opportunity | Silence; presence says calar | 13.633 s |
| Direct sincere happiness, asks if Filomena would celebrate | literal / high | WOULD_SEND; recognizes happiness | 13.543 s |

Candidates from this synthetic-only run:

> Claro, tutorial nenhum. Esse aplicativo está fazendo questão de transformar cada rascunho numa instalação artística efêmera.

> Comemoraria, sim. Tudo recuperado automaticamente? Isso merece uma pequena vitória. Fico feliz por você.

Review: no clear factual/personal-attribution error in these candidates; neither
mistook the explicit ironic complaint for praise. The first response's elaborate
metaphor and second's generic phrasing remain style observations, not proof of
ideal voice. Two candidates are a smoke sample, not an accuracy benchmark.

Nine calls: five Luna, four local Gemma. Luna usage: 7,596 input and 1,031 output
tokens (8,627 total); monetary cost unverified. Overall 53.832 s, mean 17.944 s.
The first local situation call took 21.552 s; later local situation calls took
6.668 and 6.231 s. No latency optimization was attempted.

All three ordinary executions completed once, history contains only the three
inbounds, and no spoken-state transition was committed. Two outbound traces were
`shadowed/would_send`, with no transport implementation supplied. No Graphiti was
called: memory/Boundaries were explicitly controlled empty. Consequently this run
does not exercise the detectors or live recall. Every initial reading was high
confidence, so it also does not establish real-model delayed-preview quality.

The existing LM Studio app and loopback server were started for the test after
the CLI's daemon wake attempt failed. No model was downloaded, assignment changed,
group connected or conversational worker started. The initial harness attempt
failed creating its local journal directory before inference; that setup issue
was fixed before the successful run. Artifacts are under temporary directory
`filomena-synthetic-pipeline-6215fee8e5344b4d8e13673cfb5421f6`.

The integrated test requires the separate explicit opt-in
`FILOMENA_IRONY_PIPELINE_LIVE=1`, an existing output directory, and providers already
available. It caps completion calls at 18. Ordinary regressions skip this test;
they cannot silently start inference or reconnect the group.

### Real reassessment and interpretation handoff fix

The same opt-in pipeline supports `FILOMENA_IRONY_PIPELINE_SCENARIO=late`:
an ambiguous “Sensacional esse plano”, clarification that it was irony about
deleting backups, and explicit topic closure. These are invented messages, not
retained group history. There was one initial run and one repeat after a concrete
integration fix; no prompt tuning or repeated sampling to obtain a preferred reply.

The initial run reached a real pending entry and high-confidence reassessment,
but exposed an integration defect: the interpretation was attached to response
preview, not to the preceding participation-only review. That review inaccurately
described the comment as praise despite the new explanation. It chose Respond,
then the full preview's participation pass chose Silence. No candidate or send
result was produced. This illustrates that a completed preview is not WOULD_SEND.

The pending controller now passes the validated interpretation and its exact
snapshot to an internal runtime review entrypoint. The runtime rejects a different
snapshot before inference and preserves its existing during/after-call freshness
checks. The unchanged public participation-only API still needs no interpretation;
this metadata grants no send authority. Twelve controlled cases reproduced missing
metadata before the fix; all 40 observation/review cases passed afterward.

In the repeat, the original was tracked as ambiguous/low, then reclassified as
irony/high using later evidence. The actual participation review received the
interpretation and described the irony correctly, but chose Silence. The original
entry closed; topic closure did not reopen it. All three ordinary executions
completed with three retained inbound messages, no outbound, no late generation
and no duplicate admission. This is positive evidence for reinterpretation and
non-intrusion, **not** proof of a late candidate's real-model quality.

Known model observations: the local situation model marked `necessita_cuidado`
for the initial context-free sentence in both runs. Before the fix it also
misrepresented the clarified irony in a review summary. These intermediate
quality issues are recorded, not repaired by changing the frozen prompts or FSM.

| Run | Luna calls | Local calls | Luna input/output tokens | Pipeline time |
| --- | --- | --- | --- | --- |
| Before handoff fix | 4 | 10 | 4,010 / 1,090 | 72.241 s |
| After handoff fix | 4 | 8 | 4,040 / 1,067 | 58.486 s |

Cost remains unpriced rather than inferred. Artifacts are retained in temporary
directories `filomena-synthetic-late-9c057a57e0c54c239f2e3981b942d964` and
`filomena-synthetic-late-fixed-4012fa17700742f9bde5df7c22576e7d`. Neither run used
Graphiti, private history, an outbound transport, or the active group's worker.

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

## Late response preview through the existing pipeline (2026-09-26)

The host's existing `ConfigureParticipationObservation` now accepts the explicit
opt-in `previewResponses: true` (default false). This option is available only on
the shadow composition. Initial uncertain selection, subsequent interpretation,
and a completed Respond participation review can now reach a response preview.
Wait, Silence, Obsolete, invalid/stale context and analysis failure do not start
generation. No active host configuration or group subscription was changed.

The preview uses the retained original inbound and the current retained history,
not a fabricated transport event or a second admission. The interpretation is
passed as diagnostic context in `reavaliacao.interpretacao_contextual`; it is not
a permanent participant fact, memory write, or sending authority. The existing
full pinned flow is rerun and remains free to decline participation. The existing
memory reader, compiler, generation/regeneration and multi-detector enforcement
are reused without changing their prompts or semantics.

The result is returned in `ParticipationReviews[].Preview`, with its own steps,
outputs and traces. It does not append a normal execution outcome, complete the
inbox a second time, add speech to history, or commit a state transition. A
Completed preview means that the flow completed, not that it reached WOULD_SEND:
inspect its outbound trace for `shadowed`, a block, or no send. A previous shadow
receipt still blocks a repeated input through the existing durable replay index.

Safety is checked again around model/memory awaits, at each node and before
publishing the result. Changed context/revision, cancellation and shutdown discard
the preview. Each original gets at most one preview attempt in that bounded run.
An immutable shadow-only restriction travels with the outbound execution and is
checked at the SendGuard sink. Even rearming the same host to Enabled cannot turn
the preview into a transport call. Failed memory recall is failure, not `[]`.

Local coverage now includes automatic initial selection through WOULD_SEND;
retained original identity; ordinary history/outcomes unchanged; empty Boundary;
real compiler/enforcement with a controlled MemoryItem Boundary; regeneration;
final Block; Wait/Silence/Obsolete; memory outage; shutdown during generation;
wrong session; changed revision/context; duplicate attempts; and Enabled rearm.
The group fixture uses the existing group operational artifact with send wiring,
not the older DM-only manual-shadow artifact. Test model outputs are controlled;
these tests do not establish live inference quality.

Fresh regressions: **1,510 passed, 55 skipped, zero failed** (Core 634; Llm/App 701;
OpenClaw 175). External opt-in environment variables were removed from the test
processes. The current slice made zero real model, Graphiti or transport calls;
it did not restart the conversation host or modify the separate memory worker.

Remaining operational distinction: this is still a post-batch shadow observation
mode, not an active-group deployment or a live delayed sender. It does not alter
an already-running initial response. The public participation-only method remains
participation-only; generation requires the separate host preview opt-in above.

## Initial interpretation and operational shadow configuration (2026-09-26)

This section supersedes the post-batch-only limitation above. When the shadow
host explicitly supplies `analyzeInitial`, it now runs at the beginning of the
normal batch, before Ler Situação, Decidir Presença or generation. Each target sees
only its own prefix of the pinned context. Later messages in the same batch, and
messages arriving during the callback, cannot leak into its initial reading.

The resulting provisional hypotheses are included in the existing completion
inputs as `interpretacoes_contextuais`, indexed by position in the current batch.
There are no raw transport IDs in that added field. Prompts, schemas, personality,
model routing and FSM rules were not changed. The generation path can now see the
interpretation on its first response, rather than only after it has responded.

The normal outcome retains a cloned assessment. After draining the batch, the
pending controller consumes that assessment without a second inference call.
Low-confidence targets remain eligible for bounded later reassessment. Initial
backend failure, invalid evidence, invalid opportunity or revision mismatch fails
the batch before conversational model calls; failure is not converted to a
literal/empty reading. The observer still cannot override participation or bypass
Boundary/SendGuard checks.

### Continue the validated participation decision

The preview no longer asks the participation prompts a second time after a
completed Respond review. The runtime retains their raw structured responses in
an internal, process-local field only after the safe participation cut succeeds.
The preview copies those responses while checking the same session/context,
project revision and state. The ordinary graph runner validates their schemas
again and executes its branches; it does not force a Respond output. Generation,
regeneration, memory and enforcement are still executed normally, never cached.

Reports, project JSON and model output cannot supply this internal field.
`participation-decision-reused` trace entries identify each reused prompt. A new
inbound, state/project change, cancellation, previously answered target or mode
change still invalidates the preview. The immutable shadow-only outbound ceiling
remains in place even if the host is rearmed concurrently.

Before the change, six controlled cases exposed repeated participation calls.
The focused regression set now passes 40 cases, including regeneration, final
Block, outages and stale-context checks. Full local regressions after the trace
assertion: Core 640 passed/10 skipped; Llm/App 710 passed/47 skipped; OpenClaw
175 passed. Total 1,525 passed, 57 skipped, zero failed. External opt-ins and
credentials were removed from the regression test environment.

One synthetic real-model run after this change used the same three late-context
inputs, without sampling until a desired answer appeared. Interpretation changed
from ambiguous/low to irony/high; participation chose Wait, so generation and
preview were not entered. This is not evidence of a real-model delayed candidate
or of response-cache use: the controlled Respond cases establish that wiring.
The local situation model also marked care as needed without strong support;
that quality observation remains open and no brain prompt was tuned.

That run made four Luna and eight local Gemma calls, with 4,014 Luna input tokens
and 1,010 output tokens, 68.732 seconds across the three inputs. Monetary cost is
unverified. Artifacts are in temporary directory
`filomena-synthetic-continuation-b43ae6ec55cc45daaf86ed61267126e7`.
No candidate, transport send, Graphiti call or memory write occurred in this run.
The group remains disconnected from this feature; live delayed sending is not
implemented by this Shadow-only surface.

### Wait is bounded, not terminal

A completed participation review that chooses Wait now keeps its pending target
eligible for later context. It does not generate or send, and cannot re-run at the
same admission watermark. The existing attempt budget and message-distance
horizon still apply. Respond and Silence remain terminal decisions; an obsolete
opportunity, expired distance or exhausted budget closes the entry permanently
for this run. Restart does not reconstruct this pending queue.

Four regression cases reproduced the previous premature closure before the fix:
Wait followed by Respond, topic closure, attempt exhaustion and distance expiry.
The operational JSON/observer integration also exercises an initially ambiguous
message, a first clarification choosing Wait, and further context choosing
Respond and reaching WOULD_SEND. Three native inputs produce three ordinary
executions, not a second admission of the original; only one preview is generated,
and duplicate polling cannot generate another. Memory/model results in these
tests are explicitly controlled fixtures, not additional live model evidence.

Verification after this correction: 1,530 passed (Core 640, Llm/App 715,
OpenClaw 175), 57 skipped, zero test failures. An initial parallel Llm build
terminated with CS2012, a shared App assembly file lock reported against Defender;
the sequential retry after the other builds completed passed. No antivirus
settings or exclusions were changed. No external model or Graphiti calls were
made for this correction.

### Host configuration and invocation

The existing `--native-shadow <host.json>` command now accepts an optional
`contextInterpretation` section in its **host-owned** document. It is not accepted
from the project, model output or memory. No active document was edited. Example
section for the existing reviewed group node IDs (replace the project revision
with the exact pinned artifact revision, not the Gateway config revision):

```json
{
  "contextInterpretation": {
    "version": "contextual-interpretation:v1",
    "participation": {
      "projectRevision": "<exact pinned project revision>",
      "promptNodeIds": ["group-situation", "group-presence"],
      "boundaries": [
        { "nodeId": "group-direct-involvement", "exit": "direct_involvement_rule", "decision": 0 },
        { "nodeId": "group-action", "exit": "case-responder", "decision": 0 },
        { "nodeId": "group-action", "exit": "case-esperar", "decision": 1 },
        { "nodeId": "group-action", "exit": "case-calar", "decision": 2 }
      ]
    },
    "model": {
      "model": "gpt-6-luna", "provider": "openai", "reasoningEffort": "medium",
      "maxOutputTokens": 2048, "beforeMessages": 8, "afterMessages": 8, "maxTextCharacters": 2000
    },
    "messageHorizon": 150, "attemptBudget": 2, "capacity": 256,
    "previewResponses": true
  }
}
```

The decision enum is `0=Respond`, `1=Wait`, `2=Silence`. Model and observation
budgets are explicit operator settings, not defaults added to the brain. The
existing command still requires all of its normal transport/session/memory
preflights and operator-stepped `/poll`; this option does not open a listener.
Validation checks every state’s participation cut against the pinned graph before
external setup. Unsupported version, wrong revision, missing/unsafe cut, invalid
limits, DM scope or an Enabled/live invocation are rejected. A missing section
leaves the existing host unchanged.

Interaction reports now expose initial interpretations and pending reviews,
including preview outputs/traces. Target/evidence IDs are hashed in those report
fields. Text, hypotheses and candidates are still private experimental data, not
anonymized or suitable for general production logs.

Local verification: **1,525 passed, 55 skipped, zero failed** (Core 640; Llm/App
710; OpenClaw 175). Coverage includes a first response whose generator receives
the initial hypothesis, automatic pending/reassessment/preview through the host
configuration, no duplicate initial inference, same/queued-batch temporal
isolation, failure before generation, cloned evidence and host scope rejection.
Tests use controlled inference, memory and transport. They do not substitute for
a live conversational quality assessment. No group was reconnected, no active
config changed, and no real model/Graphiti/transport calls were made by this work.
