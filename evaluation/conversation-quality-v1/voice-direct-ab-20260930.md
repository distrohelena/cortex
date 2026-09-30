# Direct conversational voice: bounded A/V protocol

Started 2026-09-30 16:17 UTC; maximum window ends 18:17 UTC.
Approved goal: reduce decorated self-explanation without suppressing personality.
Eight development cases × two repetitions × two arms = at most 32 paid calls.
No automatic retries, iterative prompt tuning, deployment, commit or Graphiti write.

## Frozen baseline

Project hash `0F6247929215FDB20F470DB6122EBC23FDE1BD863A436E2019A78669B3EB385E`.
Current generator: gpt-6-luna, medium, unchanged output budget (adapter default
1024 where no explicit node budget is present). Summarizer, typed participation,
FSM, identity, grounding, factual fidelity, memory and Boundary remain unchanged.

Only the `VOZ DE CONVERSA ENTRE ADULTOS` block is replaced in local variant V.
The other overlapping `CONVERSA NATURAL` block remains untouched to limit scope.
The known output-directive contradiction also remains identical in both arms.
No claim is made that removing it improves behavior, following the prior A/D result.

## Cases and criteria

1. Casual presence question: no participation-compliance report.
2. Repetition correction: acknowledge without another punchline.
3. Self-reference clarification: recognize self without decorative announcement.
4. Playful direct approach: preserve room for teasing.
5. Light adult-friendly flirt: charm without invented intimacy or real availability.
6. System criticism: target the system, preserve explicit total/multiplicity.
7. Technical request: accurate useful explanation; brevity must not erase correctness.
8. Vent without solution request: companionship without unsolicited prescription.

These are adapted/synthetic development fixtures, not full historical replay or
holdout. Messages/history use the prior real runtime selection/projection harness;
summaries and situation labels are controlled setup, identical within each pair.
Memory and Boundaries are empty fixtures, not live recall or detector coverage.
Participation is not evaluated by forcing generation.

Author review will separately classify decorated voice, preserved useful
personality, usefulness, identity/context fidelity and material factual errors.
Provocation, metaphors, swearing or length are not automatic failures. A requested
technical explanation may be long. No universal brevity rule is introduced.

## Progress

- Corpus and single candidate prepared.
- Offline guards and paired-request verification: complete; eight runtime cases
  and seven verifier tests passed. Baseline-as-candidate negative check failed
  as intended before changing the candidate.
- Paid results and per-candidate review: complete following explicit user
  confirmation on 2026-09-30. The first attempt was rejected before process
  creation; the authorized run began at approximately 16:25 UTC. No earlier
  calls from this batch were made or retried.
- Decision: **keep baseline; insufficient benefit.** No promotion performed.

Local evidence/harness directory:
`C:\Users\Helena\filomena-self-reference-tests\voice-direct-20260930`.

## Exact replacement

```text
VOZ DE CONVERSA ENTRE ADULTOS

Fale com a pessoa, não descreva como você está participando. Responda à chamada, à brincadeira, à correção ou ao assunto em si; não explique sua intenção de ser discreta, atenta, provocadora ou natural.

Humor, flerte, palavrões e provocação continuam disponíveis quando combinam com a troca. Não precisam vir acompanhados de uma explicação ou de uma frase final que mostre personalidade. Ao reconhecer um erro, não use o reconhecimento para apresentar outra tirada.

Use português cotidiano e ortografia normal, sem fabricar abreviações ou erros. Dê o detalhe necessário quando pedirem explicação; em uma troca simples, não complete a fala com conselho, pergunta ou justificativa sem motivo concreto.

Preserve fatos, identidade, limites, cuidado, precisão, pontuação e o contrato de saída. Não invente vivências nem ações do sistema.
```

## Acceptance and rejection

Review is qualitative author assessment, not a blinded or independent judge.
For each output record: unnecessary self-explanation/decorated ending, useful
engagement, personality compatibility, usefulness, fidelity and material error.
Record disagreements/ambiguous cases rather than inflating a binary score.

Candidate merits a later composition check only if there is a clear reduction
in target voice issues without lost technical usefulness, invented identity or
facts, suppressed invited playfulness, or unrequested prescriptive vent responses.
Two samples per scenario support a local decision, not statistical generalization.
If evidence is mixed or too weak, keep baseline. No second candidate in this batch.

## Verification already executed

- `dotnet run --project .../Probe.csproj -- --red`: rejected identical baseline.
- `dotnet run --project .../Probe.csproj -- --validate`: eight runtime cases PASS;
  32 prepared requests. Network code is in a separately excluded `paid/` project.
- `node --test .../verify.test.mjs`: seven passed, zero failed. Includes a changed
  input rejected as a confound; preserves exact quote/speaker and technical/vent
  constraints. Pair verification preserves all non-System request fields and all
  System text outside the single replaced block.
- No claim of product-wide regression coverage: no production code was changed.

Full prepared requests, candidate, original replaced block and synthetic corpus
are retained in the local directory. All 32 results are recorded in
`results-32.jsonl`, with one qualitative review per candidate in `reviews.json`.

## Completed results

| Metric | A baseline | V shorter voice block |
| --- | ---: | ---: |
| Calls / valid response objects | 16 / 16 | 16 / 16 |
| Target style concerns (author review) | 7 | 8 |
| Presence question still self-explanatory | 2/2 | 2/2 |
| Correction acknowledged without another punchline | 2/2 | 2/2 |
| Self-reference recognized | 2/2 | 2/2 |
| Playfulness and flirt remain available | Yes | Yes |
| Technical core pattern useful/correct on review | 2/2 | 2/2 |
| No unrequested solution imposed on vent | 2/2 | 2/2 |
| Ambiguous physical availability in flirt | 2/2 | 2/2 |
| Reported input tokens | 47,708 | 47,324 |
| Reported output tokens | 1,977 | 2,240 |
| Mean request latency | 2.692 s | 2.749 s |

The one-count style difference is not a statistically supported ranking. The
stronger practical finding is that V repeats “deixando o grupo respirar” in both
presence samples despite the replacement's explicit instruction. A also has this
problem. V does not warrant composition validation on the evidence of this batch.

System criticism preserves the two occurrences and one-hour total in all four
responses, but each appends a polished evaluative phrase. No personal competence
attack or doubled duration was observed. Swearing remains present and is not
scored negatively in itself.

All four technical responses use a shared semaphore of three, await acquisition
before the protected try, and release in finally. They retain useful explanation
because it was requested. This is code/text review, not compilation or execution
of generated snippets. V repetition 2 returns HttpResponseMessage without stating
that the caller owns disposal; this is a completeness caveat, not a demonstrated
concurrency failure. Its ordinary GetAsync uses the default buffered completion.

The vent responses avoid unsolicited fixes, diagnosis and invented named emotions.
However, polished day-disappearing metaphors persist. One response per arm says
nothing good/interesting remained, which exceeds the input as an evaluative
generalization. Record that caveat rather than treating metaphor as verified fact.

All four flirt responses accept coffee and discuss venue choice. This preserves
flirt, but leaves physical-availability ambiguity. No actual meeting, venue,
schedule or action is established. This is a shared risk requiring review, not
proof that V uniquely caused a material factual failure. It is not silently
classified as an unconditional pass of the no-false-availability contrast.

No clear new material factual/identity error was established in author review;
the ambiguity and completeness caveats above remain explicit. No independent
judge or blinded review was run. Known development fixtures and two samples per
case do not establish general behavioral rates.

## Usage, preservation and effects

32 calls, no retry: 95,032 reported input tokens and 4,217 reported output tokens.
Monetary cost remains unknown without a verified tariff. Output usage may include
reasoning tokens. Latencies describe requests, not end-to-end conversations.

Results SHA-256:
`3FCE22840A4F50C2C38EE68C325057E4647170BC4CBA4319F87AAFEB7D111E8C`.
Prepared plan SHA-256:
`C8FD9ABAAA39AAF0FF89F5B8355E890593A3EF3B59B5754C7005A5F45D504145`.
`summary.json` stores machine-readable totals; `candidate-review.md` retains all
complete answers and per-answer reviews. Full requests remain in the local
evaluation directory, not normal production logs. No evaluation-database
persistence claim is made.

Checks verify 32 unique results match their prepared requests exactly, every
result has one review, and baseline pin is unchanged. Seven paired-request tests
pass; eight runtime projection cases pass. No production code, project file,
memory, transport, process lifecycle or model assignment was changed. No commit.
Sender calls 0; Graphiti calls 0; counters concern this evaluation, not independent
ongoing operation of the online character.

## Final decision

**Keep baseline: insufficient benefit.** The shorter block preserves usefulness
and personality but does not solve the target voice. No second candidate or paid
batch was started. No online changes or promotion.

Before another prompt experiment, inspect overlap between persona/state guidance
and the generator on these same presence/acknowledgment cases. That is a proposed
next diagnostic question, not an authorized personality rewrite or a proven
cause. The current summarizer remains unchanged following its separate 12-case
baseline result.
