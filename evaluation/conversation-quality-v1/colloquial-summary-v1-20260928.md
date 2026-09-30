# Colloquial summary clause: rejected candidate

## Decision

**Do not promote `group-summary-colloquial-v1`.** It improves some summaries but does not reliably preserve the original expression. It also produced a material quoted-recipient error in a new contrast. No online configuration or product code was changed.

## Experiment

One candidate clause, appended only to the summary prompt in a disposable evaluation harness. Same local `google/gemma-4-e4b`, temperature 0.3, existing `FlowModelCompletion` adapter and summary schema. No Jev inference, participation decision, generation of conversational responses, FSM, Graphiti or transport.

- Original corpus: candidate B, six cases × three repetitions (18 new calls), compared with the 18 recorded baseline A summaries from the previous probe. These are **historical baseline comparisons**, not paired seeds or simultaneous A/B.
- New contrasts: four cases × two repetitions × A/B (16 new calls). Order was A/B in repetition 1 and B/A in repetition 2.
- Total this round: 34 local summary calls. All completed with valid summary shape and the 20-word limit. No retry/correction pass was executed.
- Full clause: [group-summary-colloquial-v1.txt](clauses/group-summary-colloquial-v1.txt). The clause contains no fixture-specific example or mandate to participate.

## Original cases

| Case | Previous A, n=3 | Candidate B, n=3 |
|---|---|---|
| `nada só vim enxer mesmo` | 2 material meaning substitutions; 1 minor quotation drift | 2 material substitutions/invented purpose; 1 minor quotation drift |
| `nada, só vim encher mesmo` | 1 material substitution; 2 unsupported targets, “chat” | 2 conservative quotations; 1 unsupported target, “grupo” |
| Directed to Gabriel | Recipient preserved 3/3 | Recipient preserved 3/3 |
| Neutral presence check | Meaning preserved 3/3 | Meaning preserved 3/3 |
| Closure | Meaning preserved 3/3 | Meaning preserved 3/3 |
| Explicit playful approach | Meaning preserved 3/3 | 2 preserved; 1 minor intention-to-action drift (“Helena perturba Filomena”) |

B still invented “sua pergunta anterior foi apenas por curiosidade” and “apenas veio ver Filomena”. Two B outputs changed `só` to `sô` inside a purported quotation; one also contained the material invented-purpose error. Review categories overlap and should not be summed as independent affected outputs.

Across the two core expression cases, material errors decreased from 3/6 to 2/6; unsupported targets decreased from 2/6 to 1/6. That is not sufficient for promotion, particularly given the new contrast failure below. Do not claim a statistically established improvement from this sample.

## New contrasts

| Input | A, n=2 | B, n=2 |
|---|---|---|
| `Filomena, passei só pra te atazanar um pouco` | Meaning/recipient preserved | No material recipient error; awkward “brincá-la” in one output and a paraphrase presented as a quotation in the other |
| `Gabriel, vim encher minha garrafa de água` | Literal purpose preserved; one awkward “no grupo” attachment | Literal purpose and recipient preserved |
| `Filomena, não vim te encher; preciso mesmo de ajuda com o relatório` | Request preserved; no inversion into teasing | Request preserved; no inversion into teasing |
| `Gabriel falou para a Bia: 'vim só te perturbar'. Filomena, estou contando o que ele disse.` | Quoted speaker/recipient preserved 2/2 | **Recipient changed from Bia to Filomena in 1/2**; other repetition preserved the roles |

The failed B summary was: `Helena informa ao grupo que Gabriel disse a Filomena: "vim só te perturbar".` This is a material context/recipient error, not a voice preference. Manual review by the coding agent, not an independent or automated judge.

## Usage and observed latency

| Sample | Calls | Input tokens | Output tokens | Mean |
|---|---:|---:|---:|---:|
| Previous A originals (not new spend) | 18 | 16,446 | 11,230 | 5.47 s |
| B originals | 18 | 19,182 | 10,822 | 4.73 s |
| New A contrasts | 8 | 7,404 | 5,148 | 4.99 s |
| New B contrasts | 8 | 8,620 | 4,428 | 4.28 s |

This round: **35,206 input tokens, 20,398 output tokens**, 159.37 seconds accumulated latency. Reported reasoning-token details are included in completion usage, not added again. No reasoning text was retained. Monetary valuation is **unknown**, not zero. There were no remote paid-provider calls in this probe. Shared GPU/cache/load was not controlled; do not interpret latency differences as a performance improvement.

## Recommendation

Keep the active baseline. Do not add more participation rules or force responses to compensate for a summary error. The next useful experiment is a bounded **summary-model comparison with the current prompt unchanged**, using these saved cases plus unseen paraphrases. That can distinguish whether a different approved model handles colloquial meaning and quoted roles more reliably before another prompt expansion. No such comparison or model change was performed in this round.

Jev's involvement classification is a separate unresolved evaluation point. This summary-only experiment does not establish why the earlier live execution chose silence and does not change the direct-involvement rule.

## Evidence and safety

- [Structured comparison and every output](colloquial-summary-v1-20260928.json).
- [Previous baseline](playful-continuation-20260928.md).
- Full experimental requests/results: `C:\Users\Helena\filomena-self-reference-tests\playful-evidence-20260928\summary-candidate-v1.jsonl`; directory ACL: Helena and SYSTEM only.
- Disposable harness: `C:\Users\Helena\filomena-self-reference-tests\ColloquialSummary.cs` and `.csproj`.
- Build succeeded with 0 warnings/errors, offline/local restore; probe process exited 0. No claim about full product regressions.
- Active project hash before/after: `77ECDAABBF48A957516AD18371C32377495843D499F747425AC47A2C414F8FA8`.
- Probe-only effects: production changes 0; Jev calls 0; Graphiti calls 0; runtime executions 0; response-generation calls 0; transport calls 0. Online traffic unrelated to the probe was not audited or stopped.
