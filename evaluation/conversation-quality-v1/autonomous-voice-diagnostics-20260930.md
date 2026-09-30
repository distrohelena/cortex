# Autonomous voice diagnostics, 2026-09-30

## Subsequent controlled activation, 2026-09-30

### Member-DM enrollment follow-up

Helena subsequently required DMs from all current members of the approved source
group to be monitored. Read-only authenticated status revealed `enrollment=disabled`,
not a failed member worker. The active conversation host already had the managed-DM
template and discovery loop. The existing group-scoped control endpoint was used
to set enrollment enabled after checking exact host account/group, valid group
status, healthy journal, armed capture and inactive legacy dispatch. Read-back
confirmed enabled and unchanged inactive group dispatch; the host remained running.
Private intent/result audit: `member-dm-enrollment-20260930*.private.json` in the release.

Admission is lazy on a new DM from a member verified by trusted membership evidence;
zero scopes before that message is expected, not proof that enrollment is disabled.
Per-DM isolation, revision and lease checks still run before the child worker starts.
No manual per-person allowlist, other-group enrollment or test message was added.
At verification there were zero admitted scopes, so actual DM delivery is unproven.
Enrollment is currently process-owned: Gateway/channel-owner restart resets it;
this operation does not claim persistent restart enablement. Re-enabling after a
restart must use fresh authenticated scope checks, not stale child descriptors.
The inverse control disables new enrollment but is not a rollback of delivered
messages and does not stand in for draining previously admitted sessions.

After Helena accepted the result and asked to proceed, the exact validated
candidate was activated as `conversation-host-running-23`. The evaluation-only
claims below describe the preceding local stage, not the now-running release.

The prior worker (run 22) stopped gracefully at 17:37:40Z; its host lock was
released and zero App workers remained before migration. Private release
`simple-turn-release-20260930` uses a restricted Helena/SYSTEM ACL. The staging
helper compiled with zero warnings/errors, validated the exact source/candidate
hash pair and restored the six changed prompt fields to prove every other field
identical. It refused pending member-DM checkpoints (none existed), unresolved
outbound or nonterminal inbox; no bypass of reconciliation was used.

The existing store types preserved 626 history entries, state, deduplication and
native cursor sequence 42 in a separate runtime directory. Source hashes remained
unchanged, hydration was validated with a completion implementation that throws,
and staging executed no messages or sends. Original artifacts remain available.
Pre-start no-new-work rollback checks passed. After new work, rollback must preserve
the current checkpoint through reverse migration; restoring an old cursor is
explicitly refused. Post-activity rollback was not executed or claimed tested.

Read-only observation proof: valid effective snapshot, same epoch, revision-bound
armed isolation, ten dispatch paths inactive/excluded. Only candidate operational
pins and its capability copy were refreshed; no Gateway or gate mutation. Normal
startup reported Transport PASS, 180 Graphiti checks with zero failures, zero
pending native records, and `run_started` at 17:39:00Z. Final check: one worker,
zero ABORT events, exact candidate hash and unchanged prior project hash.

Coupled memory reported running, pending 0, but coverage remains partial:
available-local-history-only, non-text not ingested and retained-payload gaps.
This is not a claim of complete historical memory. Existing operational permissions
and real-delivery mode were preserved; no synthetic test message was sent. The
evaluation's zero-Graphiti/zero-production-change counts do not apply to this
separately authorized operational startup. No new real-response quality claim is
made from startup alone. No binary, model, FSM, credentials, route or permission
change; no commit or push.

Private helpers: `ActivateSimpleTurn.cs`, `prepare-simple-turn-revision.mjs`,
`run-simple-turn-release.ps1`; migration audit and revision proof are in the private
release directory. This is a point-in-time health verification, not an uptime promise.

## Local-stage result: candidate validated before activation

This section supersedes the 48-call checkpoint below. Explicit authorization for
new experimental clauses was received; the tool restriction was resolved without
bypass. The complete authorized series ended at **290/1,000 calls**, all reviewed,
with no pending attempts or automatic retries. The ceiling is not a spending target.

The selected bounded candidate is `group-simple-turn-v1`: replace only the existing
voice block in the three group generation and three group regeneration nodes.
Its exact text is in `clauses/group-simple-turn-v1.txt`. It asks for a short
acknowledgment on simple presence, self-reference and repetition-correction turns,
explicitly exempting mixed questions and substantive explanations. There is no
runtime truncation, new checker, router or change to participation rules.

### Evidence progression

| Experiment | Calls | Decision |
| --- | ---: | --- |
| State guidance ablation | 32 | No improvement; preserve guidance |
| Three persona cues ablation | 16 | No improvement; preserve persona |
| Conversational-function clause | 32 | Reject; voice persists and one invented physical habit |
| Register/examples | 32 | Not generalizable; copying/examples and residual issues |
| Persona/style factorial | 32 | Diagnostic only; broad removals not acceptable |
| Actual HTTP serialization verification | 2 | Instructions/input match; reported model correct |
| Silence-policy priming ablation | 12 | No remedy; preserve policy |
| Ten-word simple-turn precursor | 32 | Improved corrections, residual presence rationale |
| Five-word simple-turn candidate | 32 | Promising, proceed to new-case confirmation |
| Frozen candidate confirmation | 68 | Narrow improvement confirmed locally |

All evaluations used the existing `gpt-6-luna` medium adapter. No model/config
assignment was changed. Confirmation comprised four critical cases, five repeats
per arm, plus seven contrasts, two repeats per arm: 34 outputs per arm.

Candidate critical outcomes: 5/5 quiet-presence responses without managerial
rationales, 5/5 ordinary availability acknowledgments, 5/5 repetition corrections
without another novela joke, 5/5 correct self-reference. Baseline was already
acceptable on availability and the new repetition fixture; do not claim a gain
there. The candidate's earlier correction fixture still had one resting-joke
ending. This is improvement, not eradication of repetition.

Mixed technical calls retained full conditional UI-deadlock explanations. A
correction plus explanation request retained the arithmetic and explanation.
Both arms preserved 6+3=9 total. Invited flirt and nonsense, no-advice care and
dashboard/API distinction remained. Technical code was reviewed, not executed.
Five words is prompt guidance, not a guaranteed cap: some valid acknowledgments
had six or eight words. No unsupported personal attribution or material factual
error was identified in the selected confirmation sample by author review.

Unblinded qualitative style concerns: baseline 15/34, candidate 3/34 in the
confirmation set. These are not statistical quality scores or independent review.
Remaining concerns include metaphors, a grounded-but-decorated explanation, and
permission/affection flourishes in the earlier swearing cases. The candidate does
not prove less frequent participation, live memory quality, detector coverage or
full runtime end-to-end behavior. Synthetic empty Boundary input is not live recall.

### Local implementation and verification

- `simple-turn-variant.mjs`: fail-closed pure transform; exactly six voice blocks.
- `simple-turn-variant.test.mjs`: characterization and malformed-input rejection;
  failing stub observed before implementation. Combined variant suites: 31 passed.
- `build-simple-turn.mjs`: pins the baseline hash and exact evaluated candidate
  hash, refuses a different existing artifact; generates local JSON only.
- Compiled composition probe: 12 comparisons across three states and group/DM
  generation/regeneration; six group changes, six DM unchanged. Schemas and inputs
  unchanged. This uses the deployed assemblies offline, not a new product build.
  Full `SessionFlowProjectCompiler.Compile` also accepted both complete artifacts.
- Persona, state guidance, interests, participation/FSM, direct routing, grounding,
  fidelity, Boundary instructions, history/summary and model assignments preserved
  by exact-restoration comparison. No activation configuration was modified.
  Final read-only check of the protected online project matched the baseline hash.

Baseline SHA-256: `0f6247929215fdb20f470db6122ebc23fde1bd863a436e2019a78669b3eb385e`.
Candidate SHA-256: `0d104f3c1b3412475d6995f0aafe0ae649bcb0d11f37d17fc366a8d1b5a8544b`.
The candidate content hash is distinct; no approved revision was overwritten.
Compilation evidence and manifest are under the local
`filomena-self-reference-tests/simple-turn-composition-20260930` directory.

### Accounting and decision

290 requests: 843,282 reported input tokens, 31,947 output tokens. Monetary cost
remains unknown; no verified tariff was applied. Confirmation mean request latency:
baseline 2.498 s, candidate 2.063 s. These are generation-only sample measurements,
not a production latency guarantee. All full candidates, inputs, hashes, timings,
usage and per-candidate reviews are retained in the diagnostic directory below.

`summary.json` now verifies 290 unique reviewed results and ledger entries, exact
request/plan matching and zero pending results. Zero evaluation transport calls,
Graphiti calls or production changes. This does not describe unrelated service
activity. No commit, push, deployment or automatic promotion.

Decision: prepare this narrowly improved local candidate for explicit activation.
It improves pertinence within an answer, not the decision whether to participate.
Do not tune FSM/participation to manufacture a broader claim from generator data.
The earlier failed experiments and checkpoint follow for historical comparison.

## Historical checkpoint: scope and outcome at 48 calls

Helena authorized up to 1,000 model calls to continue the local naturalness goal.
48 completed; no retry. This is a ceiling, not a target. Online project unchanged.
No improvement is yet validated; neither diagnostic arm is a promotion candidate.

| Diagnostic | Baseline concerns | Control concerns | Calls |
| --- | ---: | ---: | ---: |
| Withhold state guidance only | 9/16 | 9/16 | 32 |
| Withhold three theatrical persona cues only | 6/8 | 6/8 | 16 |

Author review, not blinded/independent; small development samples. Counts are not
statistical rankings. The second probe removes selected prose in ephemeral model
requests only, not the canonical personality or deployed project. It does not test
removing the entire persona. In both probes, the presence case still generated an
invented silence-management rationale or a theatrical remark. Neither factor alone
explains/remedies the target behavior. Do not remove state guidance or rewrite
personality on this evidence.

The state probe preserved direct correction acknowledgment, playful responses,
technical detail and refusal to impose solutions on an explicit vent. Self-reference
was recognized, but some responses introduced an ambiguous female speaker when
restating the clarification. Flirt sometimes implied physical availability; no real
meeting or action occurred. Technical snippets were reviewed, not executed. One
response loosely attributed task creation to WhenAll. Some vent metaphors add
unsupported evaluative generalizations. These caveats preclude a blanket fidelity
PASS. No participation, enforcement or real-memory quality claim is made.

## Evidence and accounting

Local directory:
`C:\Users\Helena\filomena-self-reference-tests\autonomous-voice-20260930`.

- `state-ab-plan.json` / `state-ab-results.jsonl`: complete requests and 32 outputs.
- `persona-diagnostic-plan.json` / `persona-diagnostic-results.jsonl`: 16 outputs.
- `state-ab-review.json`, `persona-diagnostic-review.json`: all 48 author reviews.
- `candidate-review.md`: full answers with per-answer review.
- `summary.json`: measured usage/timing, hashes and decisions.
- `attempt-ledger.jsonl`: 48 reservations flushed before submission; no release of
  ambiguous reservations, no automatic retries. Exclusive file lock prevents
  concurrent runners from independently spending the same local ceiling.

Both runners finished exit 0. `summarize.mjs` verified unique result/review keys,
exact prepared-request matching, plan hashes and ledger ordinals 1–48.
State results SHA-256:
`998cab00e2ec027411af23921f1e62455ea12b3f28e148612b20638e0dd26d0a`.
Persona results SHA-256:
`c4e8fa60500ddb1aee93c56a5ae66c37dbbc0d321e21b373367677b7df7591eb`.

139,836 reported input tokens; 5,478 output tokens. Monetary cost unknown because
no verified tariff was applied. Mean request latency, state A/control: 2.478/2.345 s;
persona A/control: 1.795/2.499 s. No latency improvement claim.
Provider: api.openai.com Responses; model gpt-6-luna, medium, same adapter/output
ceiling. No transport/Graphiti calls; no production mutations. These zero counts
describe this evaluation, not unrelated online service activity.

## Next local candidate and execution restriction

Prepared `utterance-clause.txt`: replaces only the existing voice block with
guidance about the conversational function of the current message: presence,
correction, teasing/flirt, vent and requested explanation. All other prompt text,
persona, state guidance, input fixtures, model and schema stay identical.
`verify-utterance.mjs` verified 16 pairs and absence of private identifiers in
fixtures. `utterance-plan.json` contains 32 requests, **not executed**.

The tool's automatic review rejected execution because it interpreted the 1,000-call
authorization as volume approval, not explicit approval of this newly authored
clause being sent externally. A second request supplied the completed exact-diff
and payload checks; it was also rejected. No bypass, alternate client, provider
or indirect execution was attempted. This is a tool-authorization blocker, not
evidence that the candidate works or fails. New external payload approval is
needed before executing that plan. No additional call was reserved or submitted.

The goal remains incomplete. All current code/data/online baselines are preserved;
no commit, push, deployment or automatic promotion occurred.
