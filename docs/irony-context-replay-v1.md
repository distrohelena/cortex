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
