# Context and supervisor repair: local status

## Scope and authority

Operator requested fixing the issues documented in `live-context-review-20260928.md`.
Changes remain local. No live restart, inbound replay, WhatsApp send, Graphiti
query/write or voice/personalization change. Existing dirty worktree preserved.

## Supervisor defect reproduced and corrected

`tools/native-conversation-runner.mjs`: a failed worker could emit ABORT while the
parent host remained waiting on stdin. `runChild` waited for process exit; `run`
mapped every JSON event except transport-wait to `running`. Consequently the
supervisor falsely advertised health and never completed the failure handling.

Regression uses a real synthetic child that emits ABORT and waits for `/quit`.
It failed before the fix. After the fix, failure requests the existing graceful
quit exactly once, retains terminal evidence, completes as blocked without restart,
and removes the runner lock after normal cleanup. A following run_closed event
cannot restore `running`. Malformed output and explicit failed execution also drain.
Transient reader-unavailable policy and fail-closed reconciliation are unchanged.

This fixes the stuck host and false status, not the external model-service outage.
It intentionally does not turn failed inference or uncertain outbound into an
automatic retry/recovery authorization. Prior sends do not become replay targets.
Integrated memory uses its existing ordered drain contract.

## Situation candidate

`group-situation-grounded-v1` changes only the three group-situation prompts.
It explicitly distinguishes:
- a direct vocative/comment from a question-only definition of direct involvement;
- current batch vs previous history in the summary;
- third-person referent vs addressee, including explicit self-reference correction;
- specific need for care vs the general obligation to behave responsibly.

No downstream deterministic participation rule, FSM, schema, generation prompt,
model, DM, memory or safety configuration changed. The eight-case test builds the
candidate in memory and compiles it through the existing pinned-project validator.
No approved project revision was overwritten.

## Real-model development evaluation

Private artifact `native-memory/context-candidate-449e2f5c721f44469fd9323a06cb7dd6.jsonl`
contains revision, actual test requests, outputs and per-service diagnostics.
Jev classifies fields; the configured local Gemma generates summaries. This is a
controlled reconstruction, not a replay of the complete original live context.

Eight cases passed the participation/care rubric:
- direct wordplay in Inactive, Attentive and Engaged: direct/respond; care=false;
- new hypothetical question following old prison discussion: responds to new topic;
- third-person conversation to another participant: silent, contrast preserved;
- banal exchange to another participant: silent, care=false;
- explicit bereavement and request for conversation: responds, care=true;
- explicit correction that "ela" is Filomena: direct/respond, care=false.

All observed service calls returned HTTP 200 (18 HTTP calls: 10 typed + 8 summary).
One local-summary sample still interpreted wordplay as a compliment. Another
summarized a scheduling confirmation as postponement. These are semantic concerns,
not failures captured by the boolean/action rubric. Do not describe eight rubric
passes as complete summary/identity acceptance or claim the live behavior fixed.
No controlled baseline replay was run; no claim of statistically proven superiority.
There was no generation or outbound in the harness. Cost is not fully captured;
Jev usage/raw decision evidence remains private, local cost is not assumed zero.

## Verification

## Second development evaluation (not promotion)

Artifact: `native-memory/context-candidate-dc0384a778594eb089d1f9fd5f5f018e.jsonl`.
The refined clause was evaluated with the same eight synthetic cases. The opt-in
test passed in 41 seconds; all eight participation/care checks passed again.
Summary review does **not** pass: the Attentive wordplay summary replaced "pra
frente" with "progressista", and the lunch summary asserted postponement. The
lunch input itself ("ficou para amanhã então") permits that reading; it is an
ambiguous fixture, not conclusive evidence of a factual error. A contrasting
unambiguous confirmation fixture is required before using it as a regression gate.
The self-reference summary identifies Filomena but retains an ambiguous possessive.
The candidate remains experimental and must not be described as a complete repair.

Source inspection confirms `SystemOneParticipationCompletion.CompleteAsync` passes
the original request to the summary completion, changing only ResponseSchema.
The new instruction is therefore not deliberately stripped by that adapter.
This finding does not establish that the small local model reliably follows it.

Delivery-audit correction: the outbox uses `Ref(binding.OpaqueRef + "\n" + executionId)`,
NOT `Ref(executionId)` and NOT the inbox's `Binding` (that is `AuthorityRef`). Earlier
ad-hoc comparisons did not establish absence. The final offline reconciliation
helper derives the actual trusted binding, rejects any matching execution **or
input** in the outbox, and validates the original checkpoint using the real store.
Its successful guarded staging is the authoritative no-send proof for this failure.
No absence query by itself authorizes replay or checkpoint repair.

## Previously completed verification

- Supervisor: 20 tests passed, including failing-before regression.
- Conversation corpus/variant suite: 17 tests passed.
- Focused .NET SystemOne/composition suite: 72 passed, 3 opt-in skipped.
- Full offline Llm suite: 809 passed, 49 opt-in skipped.
- Live opt-in participation test: 1 passed, eight cases, no generation/memory/transport.
- Scoped diff whitespace check and runner syntax check passed.

## Remaining operational boundary

No production artifact was changed. The existing run has a terminal failed inbound
at sequence 99 and requires explicit, evidence-backed reconciliation before restart.
The exact original provider failure remains unattributed; the later successful
calls only prove current test availability. Do not bypass reconciliation, mark a
failed message delivered, or silently resend it. Summary interpretation still needs
a targeted acceptance check before promoting this candidate as a complete fix.

## Subsequent evidence and controlled activation

The sections above are chronological development checkpoints, not current deployment status.
The consolidated `group-situation-grounded-v2` variant replaced only the three group situation
prompts. The same ten-case development corpus produced 3/10 participation/care rubric passes
for the current control and 10/10 in each of two consolidated runs. These are not holdout
results or universal semantic guarantees. Some summary wording remains interpretive.

Use `context-repair-comparison-20260928.v2.json` for aggregate measurements. The original
unversioned comparison artifact is superseded: its aggregator incorrectly read lowercase
runtime/latency properties from PascalCase fields. No new model calls were made to correct
the aggregation. All three runs had zero runtime failures; monetary cost remains unknown.

The project artifact `77ECDAABBF48A957516AD18371C32377495843D499F747425AC47A2C414F8FA8`
was staged with byte-identical published .NET binaries and the corrected supervisor.
The guarded checkpoint migration retained the old terminal failed inbound without retry;
run 13 subsequently recognized that input as duplicate/no execution. Transport and Graphiti
preflights passed before activation. A compiled-model smoke had six cases, four WOULD_SEND
and two silences, without real transport or Graphiti access.

Run 13 then stopped on one indeterminate outbound attempt. The installed Gateway logged
`bound_quote_unavailable`; inspection of that exact installed sender confirms the error is
thrown before `socket.sendMessage`. Helena explicitly confirmed that the message did not
arrive. The quote target was native sequence 100, timestamped approximately 67 minutes before
the attempt; the existing metadata cache has a ten-minute lifetime. Age is consistent with
expiry; the regression separately reproduces missing-cache failure without assuming cache
contents were observable after the fact.

The supervisor drained and reported blocked rather than falsely running. A separate private
reconciliation copy records `not_sent` through the real outbound-host API, retains all eleven
new inbound history entries and their deduplication identities, preserves the observed
non-spoken transition to Attentive, and keeps original evidence unchanged. No attempt was
resent. The exact transition timestamp was not recorded and is explicitly unknown rather
than fabricated. This copy has not by itself restarted the character.

The local transport repair recovers exact host-owned retained quote payloads from the existing
journal, without a new store, extended retention, arbitrary quote bodies in the public request,
or unquoted fallback. Regression evidence includes missing cache, restart, a new capture epoch,
wrong account/group/native ID, invalid sender metadata, and public-payload injection rejection.
The first missing-cache test failed before the fix. Validation/deployment remains in progress;
do not interpret this entry as proof of live reply recovery or current online operation.

Validation caveat: the broad Filomena.Core run had 712 passes, ten skips and nineteen failures
in existing FlowCompiler/FlowBindingValidator platform tests; see
`test-results/context-repair-core.trx`. The focused migration suite passed 24 tests and the
Llm suite passed 809 with 49 skips. No newly compiled Core binary was deployed.

### Quote repair deployed and operation resumed

The retained-quote repair passed 107 focused OpenClaw tests, core/extensions typecheck,
scoped lint, builds and a compiled-region fake-socket smoke. Three existing installed
artifacts and one internal helper were deployed with hash-verified backups; launcher,
host configuration and brain/model assignments were unchanged. Gateway readiness passed,
capture started OFF, and authenticated status returned in approximately 44 ms. This is
not proof of a new live reply delivery.

After Helena explicitly authorized processing the pending events and restarting normal
operation, native events 112–125 were recovered through the real runtime. All fourteen
finished, with zero sends. The original failed attempt remains `pending → indeterminate
→ not_sent`; it was not retried. Recovery reports contain 52 model-usage entries and
107,260 ms of summed interaction pipeline duration, excluding repeated startup/preflight
time. Cost remains unknown rather than zero. Full outputs, usage and audit hashes are
preserved privately in `quote-repair-20260928/recovery-comparison.private.json`.

The closed epoch was fully drained; an intermediate empty epoch was explicitly verified.
The new epoch starts at cursor zero, without skipping fresh events. The restored checkpoint
contains 586 history entries. `conversation-host-running-14` reached `run_started` and
normal polling with transport/Graphiti preflights green and zero reported host failures.
The coupled memory worker is running, but thirteen ingestion items were still awaiting
confirmation at the verification point. Its coverage remains partial (available local
history only, unsupported non-text and missing retained payload); do not describe memory
backfill as complete. Analyses/evidence remain available for cross-comparison.

### Quote repair deployed and operation resumed

The retained-quote repair passed 107 focused OpenClaw tests, core/extensions typecheck,
scoped lint, builds and a compiled-region fake-socket smoke. Three existing installed
artifacts and one internal helper were deployed with hash-verified backups; launcher,
host configuration and brain/model assignments were unchanged. Gateway readiness passed,
capture started OFF, and authenticated status returned in approximately 44 ms. This is
not proof of a new live reply delivery.

After Helena explicitly authorized processing the pending events and restarting normal
operation, native events 112–125 were recovered through the real runtime. All fourteen
finished, with zero sends. The original failed attempt remains `pending → indeterminate
→ not_sent`; it was not retried. Recovery reports contain 52 model-usage entries and
107,260 ms of summed interaction pipeline duration, excluding repeated startup/preflight
time. Cost remains unknown rather than zero. Full outputs, usage and audit hashes are
preserved privately in `quote-repair-20260928/recovery-comparison.private.json`.

The closed epoch was fully drained; an intermediate empty epoch was explicitly verified.
The new epoch starts at cursor zero, without skipping fresh events. The restored checkpoint
contains 586 history entries. `conversation-host-running-14` reached `run_started` and
normal polling with transport/Graphiti preflights green and zero reported host failures.
The coupled memory worker is running, but thirteen ingestion items were still awaiting
confirmation at the verification point. Its coverage remains partial (available local
history only, unsupported non-text and missing retained payload); do not describe memory
backfill as complete. Analyses/evidence remain available for cross-comparison.
