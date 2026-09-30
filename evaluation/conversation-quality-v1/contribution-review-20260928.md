# Post-repair contribution and repetition review

## Follow-up: participation, real voice and memory, 2026-09-30

Read-only current evidence captured at approximately 19:27–19:34 UTC, after the
simple-turn voice release was activated. The historical review below is unchanged.
Private artifacts are under the restricted operations directory
`three-priorities-review-20260930`; no conversation payload is reproduced here.

### Participation and voice

Run 22 contains 22 actual execution outcomes, all successful, all selecting silence
with no confirmed send. Empty polling events were excluded. Twenty-one outcomes
have a same-epoch/same-cursor-sequence observed input; one poll cursor advanced over
another event and cannot be used as an exact batch-input key. The initial audit
also selected a historical runtime directory incorrectly; `audit-v2.private.json`
resolves the journal from each host configuration and supersedes that correlation.
These are audit-helper corrections, not production-runtime fixes.

The directly correlated inputs are discussion among members, links, reactions and
fragments, with no explicit call to Filomena identified in those inputs. No excess
participation is reproduced. This does not prove perfect participation or infer
that 100% silence is generally desirable. In particular, the sample does not test
direct calls, playful continuation addressed to her, or ambiguous invitations.
One sarcastic criticism is summarized literally; the underlying primary message
remains available, and no reply or concrete downstream factual failure occurred.
Preserve it as a contextual-interpretation review item, not a new participation rule.

Run 23 is running, but at the captured point has **zero actual execution outcomes
and zero generated answers**. Sequence 43 was a non-text ignored input, not a
missed textual invitation. Therefore real-group voice acceptance and repeated-joke
evaluation remain **AWAITING_REAL_RESPONSE_SAMPLE**. The local A/B remains valid
as local evidence, not proof of a live response. No synthetic message was sent to
manufacture an online sample, and no new inference calls were made for this review.

Decision: do not reduce participation further from this evidence. Keep the active
voice candidate and preserve future observed input/decision/output comparisons.
No claim of background monitoring beyond the existing runtime logs is made.

### Memory: source, receipt and backend verified separately

The actual deployed history reader yields 522 eligible human text messages, all
with persisted receipts in the exact host-selected group. There are zero eligible
retained messages without a receipt, zero unpersisted eligible messages and zero
pending maintenance items at capture time. Thirteen media captions contribute text;
media content itself is not inferred from captions.

A read-only Graphiti episode query returned 577 episodes in the expected namespace.
All 522 receipt episode IDs exist exactly once. Twenty-nine bodies match receipts
directly. The other 493 match the exact authorized before/after name-repair plan,
whose SHA-256 was checked; no arbitrary mismatch was ignored. Final verification:
522 verified, zero missing, zero unexplained mismatches. The extra 55 episodes were
not classified as duplicates or belonging to these receipts without further proof.
Episode persistence is not a guarantee that every extracted social fact is correct.

The source scan contains 820 payload-bearing observation rows: 157 self records,
522 eligible human texts and 141 unsupported/non-text human records. The inventory
includes reactions, stickers, media without text and encrypted/protocol content.
Do not turn those events into invented personal facts or claim media was understood.
The two retained source tables also contain 994 completed ingress rows without any
payload or metadata, dated September 23–30. These are **not** 994 proven lost unique
messages: some may be represented by observation records or already ingested work,
and the missing envelope prevents safe identity/content reconstruction here.

What remains is historical source coverage, not a demonstrated stuck text backlog.
Closing it requires an available, trusted historical source with exact group/message
identity, or an explicitly designed non-text interpretation path. Do not clear the
partial-coverage flag, relink the account, broaden scope or resubmit known episodes
to make counters look complete. Existing recovery already rescans late arrivals and
deduplicates them; preserve that behavior.

### Verification and effects

Existing source/recovery regression suites were rerun: **14 passed, 1 opt-in live
audit skipped**. They cover late arrivals, restart, no resubmission, wrong namespace,
conflicting identities, unavailable source, and pending-work fairness. The shell
lacked pnpm; invoking the installed Vitest entry point succeeded after sandbox
permission for its local cache. No package installation was needed.

No product code, prompt, FSM, online configuration, memory record or receipt was
changed. No WhatsApp sends, inference calls or service restart were initiated by
this audit; ordinary ongoing service activity is separate. Codebase-memory was
available but its project list did not include Filomena, so targeted source reading
was used; no indexed-graph completeness claim is made.

Status: participation audit complete for this sample; voice live acceptance awaits
actual responses; retained-text ingestion verified end-to-end through episode
storage, with historical/non-text coverage explicitly incomplete.

Read-only review, 2026-09-28 (America/Sao_Paulo). No prompt changes, model calls,
tests, restart, transport action or memory mutation. This is a new operational
sample, not a rerun of the older “Discreta, não desaparecida” voice case.

## Scope and evidence

Current epoch `9f961b94-2d83-446f-9db1-89169e477757`, sequences 1–19,
19:02:36–21:46:35 local time. Fifteen observed text inputs and fifteen completed
inbound executions in supervisors 16 and 19; polls with no executions excluded.
Project revision: `77ECDAABBF48A957516AD18371C32377495843D499F747425AC47A2C414F8FA8`.
This selection spans the memory-model rollout; it does not imply every input was
ingested using Luna. Source-buffer hashes and selected private input/output
evidence are retained under operations `contribution-review-20260928`.

One SENT, fourteen successful no-send outcomes, zero failed executions in this
sample. SENT and speech commit are runtime evidence, not separate recipient
confirmation. No repeated empty poll is counted as another silence or delivery.

## Findings

| Case | Evidence | Assessment |
| --- | --- | --- |
| CR-01: uninvited discussion | Sequences 1–13: eleven executions concerning a game, links, reactions and fragments; all chose silence. | Excessive participation not reproduced in this sample. Do not add another silence restriction based on old examples. |
| CR-02: direct invitation | Sequence 15, “@Filomena tá ai”: direct classification, response “Tô aqui, Helena. O que foi?”, SENT. | Appropriate participation; the old compliance-report phrasing did not recur here. |
| CR-03: playful continuation | Sequence 17, “nada só vim enxer mesmo”: summary says she “apenas veio ver”; indirect involvement, weak social opportunity, silence for no contribution. | Observable loss of the teasing meaning in the summary. Silence itself is not a demonstrated violation: a brief closing exchange may reasonably end. Hypothesis: interpretation loses the social value before the participation decision. Full exact per-step request was not captured here; no causal claim about model internals. |
| CR-04: conversation about operation | Sequences 18–19: leaving her online while Astra works, then mentioning future external tools. Both silent; Engaged→Attentive→Inactive. | Reasonable withdrawal from an operator update rather than inserting herself into it. Third-person wording in an internal summary alone does not demonstrate self-identity confusion. |
| CR-05: repeated jokes | Only one generated/delivered response in scope. | Insufficient sample to evaluate semantic joke repetition. Do not call the issue fixed or reproduced. |

## Recommendation

Do not reduce participation further from this evidence. Preserve CR-03 as a
specific contrast between non-contributing chatter and an invitation to playful
continuity. A later bounded evaluation should distinguish harmless silence from
systematically flattening jokes into neutral summaries. It must not mandate a
reply to every joke, fabricate intimate intent or change the direct-route rule.

Repetition remains an open evaluation item requiring multiple actual responses
within a coherent topic. No new checker, rule or prompt is proposed as necessary
from this sample alone. Findings are descriptive, not an A/B causal comparison or
a claim of universal behavioral correctness.
