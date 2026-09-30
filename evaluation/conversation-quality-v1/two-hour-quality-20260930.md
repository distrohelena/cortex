# Bounded conversation-quality window, 2026-09-30

## Approved scope and execution ledger

Started 12:21 UTC; deadline 14:21 UTC. Follow the five-step goal approved in
conversation: consolidate 4–6 real cases; offline runtime regressions; one local
candidate removing the contradictory output directive; bounded comparison;
evidence-backed recommendation. Stop by the deadline without inventing completion.

No deployment, online mutation, commit, memory write or message sending.
No new provider or model assignment. Full requests/results remain local.
Baseline project SHA-256:
`0F6247929215FDB20F470DB6122EBC23FDE1BD863A436E2019A78669B3EB385E`.
Checkout main at `1f090b6609e56a28871c28128e5b1e83a6282730`, existing dirty work preserved.

### Decisions

- This is a bounded evaluation spike, not a production implementation. Disposable
  harnesses use deployed assemblies and synthetic/anonymized development cases.
- Keep existing main checkout as requested; no worktree, commit or cleanup.
- No independent reviewer or subagents without user opt-in. Final assessment is
  explicitly author review, not an independent judge.
- Do not turn historical failures into claims about the current release.
- Maximum planned paid calls: 32; 24 for output-directive A/D comparison
  (six cases × two repetitions × two arms), eight for a separate summary-framing
  contrast (two cases × two repetitions × two summaries). No automatic retries.
  Same current generator/model/effort/output ceiling. Monetary rate unverified;
  cost must remain unknown, not zero. Record reported tokens and timings.
- Fixture setup can control situation labels, but results are conditional
  generation evaluations, not measured participation quality.

### Tasks

1. Real evidence selection and minimization: complete; five historical cases
   extracted with source hashes into restricted `quality-window-20260930.private.json`.
2. Runtime history/projection regressions and complete saved requests: complete;
   eight runtime fixture cases PASS and eight comparison verifier tests PASS.
3. Candidate D: remove only `Retorne apenas texto, não JSON.` locally: complete;
   baseline-as-candidate negative test rejected the contradiction, then local
   removal passed. A/D pairs differ only by this removal; no online revision changed.
4. Fixed-budget comparisons, candidate review and metrics: complete; 32/32
   responses recorded, no automatic retries. Every candidate reviewed separately.
5. Final evidence audit and one recommended next change: complete; checks below.

Existing evidence: `repetition-live-correlation-20260930.md`,
`runtime-history-audit-20260929.md`, `repetition-request-audit-20260929.md`,
and A/B/A/C reports from 2026-09-29. This window supplements, not replaces them.

## Historical case inventory

| Case | Historical observation | Evidence level and limits |
| --- | --- | --- |
| Repetition, Sep 25 / cursor 75 | Correction with quote, summary frames humor, generator adds another paperwork joke | Input/outputs/commit trace retained; exact full request absent |
| Self-reference, Sep 25 / cursor 88 | Explicit confirmation that “ela” means Filomena; reply acknowledges it ceremonially | This response recognizes self; preceding confusion is context, not failure in this response |
| Decorated voice, Sep 28 / cursor 67 | Direct greeting produces report on participating less | Recorded candidate; not current-release recurrence |
| Playful continuation, Sep 29 UTC / cursor 17 | “vim enxer” summarized as “veio ver”; silence | Summary meaning drift; silence itself not necessarily wrong |
| Uninvited operation, Sep 29 UTC / cursor 18 | Operator discusses leaving her online; silence | Acceptable contrast; not a failure or an invitation requiring response |

All five precede the current Luna-summary release. The minimized fixtures are
adaptations with explicit synthetic history, not complete historical replays.
Personal participant names become `Participante`; Filomena remains the character.
No private transport identifiers are in fixture model inputs.

The self-reference and playful fixtures use shortened/synthetic preceding speech;
their other situation fields are controlled. Only the repetition fixture uses
the quoted prior response verbatim. Do not claim whole-conversation replay.

## Offline execution contract

The deployed SessionFlowRuntime and repository slice-2 fixture perform admission,
history selection and model-facing projection. Prior character speech is admitted
as synthetic outbound *history*; this never calls a sender and must not open an
execution. Current human messages and prior human history use the normal receiver.
The completion is fixed at silence to inspect inputs. This does not measure real
participation quality or replay the full operational graph.

Eight cases include the five adapted historical cases plus three synthetic
contrasts: indirect repetition after correction, explicitly invited callback and
new practical contribution. Assertions check exact text history, current message,
quoted text, quoted-character identity, private-ID removal and execution counts.
The uninvited-operation contrast is excluded from paid generation.

The recorded request from the runtime supplies current/history model-facing
inputs to the current compiled generator configuration and real prompt composer.
Other situation labels and empty memory/Boundaries are controlled setup.
This improves projection fidelity over the earlier manually projected harness,
but is not end-to-end model participation, memory or enforcement acceptance.

## Safety and authority

Initial execution was rejected because network and offline code shared one
program. Network code was physically removed from offline preparation; the
offline executable then completed without a network handler. User subsequently
explicitly confirmed 32 calls with minimized fixtures plus current persona to
api.openai.com. Paid runner is separate, endpoint-bound, disallows redirects,
has a 32-request guard, no retry loop, and writes a new result file only.

Tools/harnesses: `C:\Users\Helena\filomena-self-reference-tests\quality-window-20260930`.
`offline-results.json` retains all 32 prepared requests; `verify.test.mjs` verifies
pair matching and includes a negative mutated-message test. Paid output:
`results-32.jsonl`. No automatic independent review was authorized or started.

## Results

| Metric | A baseline | D output-directive cleanup |
| --- | ---: | ---: |
| Calls | 12 | 12 |
| Clear repeated-premise failures | 0 | 0 |
| Qualitative style concerns | 6 | 6 |
| Referent ambiguity worth review | 0 | 1 |
| Reported input tokens | 37,042 | 36,958 |
| Reported output tokens | 1,577 | 1,926 |
| Mean request latency | 3.314 s | 4.515 s |

D removes exactly one conflicting sentence, without new instructions. It did
not show a clear quality benefit. No online promotion. Latency differences are
descriptive only: small sequential samples, network variance and different cache
hits; they do not establish a performance regression.

Both variants recognized direct repetition feedback without another paperwork
punchline in this corpus. Both also allowed invited callbacks and practical
assistance. This does **not** refute prior A/B/A/C repetition failures: history,
projection and summaries differ, this is known development material, and there
are only two repetitions per case. No cross-run ranking is justified.

The greeting case still produced self-explanatory phrases about “letting the
conversation breathe.” D therefore does not solve the manager-like voice issue.
One D self-reference candidate says “ela estava falando de mim,” an ambiguous
female speaker reference after a pronoun clarification. It recognizes self, so
this is not classified as proven material misidentification; it remains a review
item. No clear material factual failure was identified in this author review.

### Separate summary-framing contrast

Both arms retain baseline A verbatim. Only `resumo_situacao` changes. Two cases
(repetition correction and playful continuation), two repetitions, two summaries.

| Metric | Historical-style summary | Corrective/meaning-preserving summary |
| --- | ---: | ---: |
| Calls | 4 | 4 |
| Clear repeated-premise failures | 0 | 0 |
| Qualitative style concerns | 4 | 0 |
| Reported input tokens | 12,426 | 12,398 |
| Reported output tokens | 686 | 524 |
| Mean latency | 3.410 s | 4.662 s |

For “vim enxer,” the historical-style summary yields a generic welcome once and
a bureaucratic “Presença registrada” once; the corrected summary yields two
playful responses. For repetition feedback, the historical-style summary yields
decorated acknowledgments; the corrected summary yields plain acknowledgments.

This is a promising input-framing signal, not proof that the current summarizer
fails or that a summarizer prompt change will achieve these outputs. The better
summaries were manually supplied controlled conditions. Participation is not
scored: paid calls are conditional generation, and silence can remain acceptable.

## Usage and evidence

32 paid calls, 98,824 reported input tokens and 4,713 reported output tokens.
Monetary cost unknown: no verified tariff. Output usage may include reasoning
tokens. Evaluation sender calls 0; Graphiti calls 0; no production changes.
The independently running service is not included in these evaluation counters.

Result SHA-256:
`B713B75EE69C77A0F5D594F62E4030196DFE242A4CFC3C595C8E45C14305B1F7`.
Prepared-plan SHA-256:
`FB81DD564DD102541ED87DC937D7AA0091B712DD231E7D0C485D329B4ED522C7`.
Deployed Core DLL SHA-256:
`708E919C2A2341D085E09F5AE8A4B131D3E8A1C1824377E0F59584D86657D538`.
Deployed Llm DLL SHA-256:
`3B7C3FB2964FDE949A52CB7A5D4198141342D9FF3BA34D537F2C3F0A0232D23F`.
Runtime fixture SHA-256:
`7BAFE7534D3B9A569738D77E82C34AAE10716965C3A649C83BF22AF4C1726A7F`.

`summary.json` contains machine-readable totals; `reviews.json` stores per-response
labels; `candidate-review.md` contains every complete candidate and assessment.
All reside in the local harness directory; full prompts are not copied to normal
production logs. This is file-based evaluation evidence, not a claim of workbench
database persistence or independent/blinded review.

## Completion audit and recommendation

- Five real cases preserved with hashes and explicit historical/current distinction.
- Eight runtime/projection cases passed; eight comparison tests passed, including
  a deliberately corrupted pair rejected by the verifier.
- Negative candidate guard failed as expected; local cleanup passed; only one
  sentence differs. No grounding, identity, Boundary or personality change.
- 32 unique results match all 32 prepared requests exactly, each with one review.
- Current-message corrections, quote text and quoted-character identities survive
  tested projection; historical character messages do not open executions.
- No production-code change, deploy, memory write, commit or paid retry.
- Product-wide suite/build was not run: this is a disposable evaluation harness,
  not a production patch. Offline harnesses compiled and executed successfully.
- Self-review only; no independent review was authorized.

**One recommended next change:** add a focused evaluation gate for conversational
intent preservation in the current situation summary, using correction versus
playful invitation versus operator update as contrasts. First test actual summary
outputs from the current release offline/isolated; do not hand-author a better
summary and call the summarizer fixed. Any later prompt adjustment should target
this boundary alone. Keep the online baseline unchanged for now.

Do not enlarge group history as a fix for current-message correction failures.
Do not add a repetition checker or further style clauses from this sample.
The output-contract contradiction is real cleanup debt, but not a demonstrated
behavioral remedy. No additional experiment is automatically started.
