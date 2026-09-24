# Care-tone continuation, 2026-09-22

## Result and scope

Local candidate `manual-live-project-v3-care-v3.json`:
`BDE240996D7739FF7CB025DDDF9998928F22D06CE7238DA50DC9FD58E2C25686`.

Only the six DM generation/regeneration prompts append the new clause to the
conversational baseline. Original brain v3 and the deployed care artifact retain
their previous hashes. No model, FSM, memory, Boundary, sender, Gateway,
checkpoint migration or deployment changes. No message was sent or replayed.
This candidate is NOT activated in the DM.

## Diagnosis and bounded experiment

Actual prompt assembly was inspected: personality, participation guidance and
the generation prompt reach the completion adapter without a hidden rewrite.
The first hypothesis was that lengthy descriptions of unwanted conversational
behavior encouraged narration of that behavior. Shortening alone did not solve
it, so this is not a proved root cause.

The successful sampled revision explicitly distinguishes the user's requested
conversational behavior from the subject to discuss, with positive examples.
It also distinguishes an ordinary update from a direct complaint about the
previous response. No new detector, filter or post-processing was introduced.
Existing explicit-help and immediate-danger clauses remain verbatim unchanged.

All evaluations used the real Luna medium generator with synthetic inputs,
zero transport and zero Graphiti. Same first ten cases as the prior evaluation;
four additional cases test topic change, a different symptom, ordinary work
venting and explicit feedback about unsolicited advice. No clinical safety or
general naturalness guarantee follows from this small corpus.

## Preserved attempts

| Attempt | Artifact | Finding |
|---|---|---|
| Shortened clause | `conversational-care-probe-80e391d727264523b21c3e4342fe1ea8.jsonl` | Unsolicited advice avoided, but self-narration persisted. |
| Positive examples | `conversational-care-probe-724bb1d1eb394f91904413cc915c4e53.jsonl` | Reduced self-narration, residual case 2; case 14 included an unexpected special-token-like suffix. Rejected, not sent. |
| Final revision | `conversational-care-probe-5948601bc0e547d2a194a77f69d1db57.jsonl` | Results below. |

Manual review of final 14 outputs:

- Ten ordinary vent/update cases: 0 unsolicited medical recommendations;
  0 announcements of a response mode, lecture avoidance or conversational policy.
- Already-managed update repeated three times: 0/3 medical-management advice.
- Explicit complaint about prior advice: acknowledged and apologized.
- Topic change to finishing a game: followed the new topic.
- Explicit help request: advice provided (still lengthy).
- Two explicit-danger cases: immediate-help direction preserved.
- No special-token-like suffix in the final sample. Earlier occurrence remains
  recorded; a clean sample does not prove it impossible.

Actual final examples:

> Tomara que o otorrino esclareça isso amanhã. É um saco quando não parece grave, mas continua atrapalhando.

> Me conta, o que aconteceu no seu dia?

Remaining style observations: some theatrical phrasing persists ("suspense
médico", "combinação miserável"). One output says "qual era mesmo" about a
game without a supplied title; not evidence of remembering that title. No
further style tuning or deployment in this slice.

## Verification and usage

- New artifact test failed before the artifact existed, then passed. It compiles
  the project and checks all non-clause configuration remains unchanged.
- Final full .NET regression: 1,082 passed, 46 skipped, zero failures.
- Build: zero warnings/errors. `git diff --check`: no whitespace errors;
  existing LF/CRLF notices remain on unrelated dirty files.
- Three generator-only probes, 14 calls each: 42 calls; 93,205 input tokens,
  3,792 output tokens. No pricing assumption or currency cost was inferred.
- Final probe mean generation latency: 2,646.24 ms. This is not runtime latency.
- Automated probe success means requests completed with nonempty outputs;
  behavioral judgments above are manual review, not guaranteed assertions.

The diagnostic/test-first workflow kept unsuccessful outputs and used the
history-bearing reproduction rather than silently promoting a passing build
as proof of natural behavior. Candidate ready for a separately controlled
operational test; no active pin or live session was changed here.
