# Summary-model comparison: frozen prompt

## Decision

**Prefer Luna medium for the next isolated summary integration test. No online activation.** In this narrow sample, it preserved colloquial meaning more reliably than the current Gemma profile without adding prompt instructions. This is not a global model ranking or proof that participation is fixed.

## Method

- Original active `group-situation` prompt frozen. The rejected colloquial clause was not used.
- Existing adapters and summary JSON schema. No Jev calls, `Decidir Presença`, response generation, runtime execution, Graphiti or transport.
- Gemma: `google/gemma-4-e4b`, local LM Studio, temperature 0.3, output ceiling 8192.
- Luna: `gpt-6-luna`, medium, existing Responses adapter, `store=false`, output ceiling 4096. Model field in responses matched the requested profile.
- Six original cases × three runs plus four transfer cases × two runs: 26 Luna summaries compared with the 26 previously recorded **baseline Gemma** outputs, not the rejected prompt candidate.
- Two new cases × two repetitions × two models: eight fresh outputs, order-balanced between models.
- **34 new calls: 30 Luna and four Gemma.** Comparison contains 30 outputs per model. No automatic retries or format-correction pass.

Provider protocols, reasoning settings, tokenizers and output ceilings differ. This evaluates usable deployment profiles, not an isolated model-weights variable. Prior Gemma samples are historical, not simultaneous or seed-matched. The shared GPU was not reserved for benchmarking.

## Results

| Measure | Gemma baseline, n=30 | Luna medium, n=30 |
|---|---:|---:|
| Material meaning/attribution errors observed | 3 | 0 |
| Additional unsupported target in core expression cases | 2 | 0 |
| New quoted speaker/recipient inversion | 0 | 0 |
| Summary word-limit violations | 1 | 0 |
| Mean observed summary latency | 5.40 s | 2.09 s |

The three Gemma material errors are the previously reproduced replacements of `encher/enxer` with “ver”, “checar o ambiente” or “preencher o tempo”. The two unsupported targets attached “encher” to the chat. These counts are carried forward from the baseline review, not newly reproduced this round.

Luna preserved the expression across all six core outputs, including `enxer`; it did not invent a purpose or a chat target. Literal filling of a water bottle, a request for help, closure, and teasing directed to someone else remained distinct. Both runs of Gabriel speaking to Bia preserved those roles.

### Fresh cases and limitations

1. `Filomena, relaxa, tô só pegando no seu pé`: both models preserved the colloquial message in both runs.
2. `Bia disse ao Gabriel: 'não tô tirando sarro de você'. Filomena, a frase é da Bia, não minha.`: neither model inverted the negation or attributed the phrase to Helena. One Gemma output omitted the quoted content; the other had 25 words, exceeding the 20-word contract. One Luna output omitted Gabriel as the quoted addressee, while preserving Bia's authorship and the negation; the other retained all roles.

Luna is **not flawless**: one output normalized `enxer` to `encher` inside quotation marks, and another rendered `só encher mesmo` as a quotation despite the original including `vim`. These are minor quotation-fidelity defects, not material changes of meaning in these cases. The original approved prompt was not modified to hide them.

Review was performed by the coding agent, not an independent judge. This is a small development corpus. It does not establish a production error rate, resolve general sarcasm, or prove a better participation policy.

## Usage and cost status

| New inference only | Calls | Input tokens | Output tokens | Accumulated latency |
|---|---:|---:|---:|---:|
| Luna medium | 30 | 27,252 | 4,310 | 62.74 s |
| Local Gemma | 4 | 3,792 | 2,506 | 23.55 s |

The historical Gemma comparison totals 30 calls, 27,642 input tokens and 18,884 output tokens including the four new calls. Historical usage was not incurred again.

Fresh order-balanced cases alone averaged **1.96 s Luna** and **5.89 s Gemma**, four outputs each. These observed timings are not a controlled performance guarantee. Monetary cost remains **unknown** because no verified tariff was applied; remote Luna calls are paid, and local compute is not inherently free. Token counts are provider-reported and not directly equivalent across tokenizers.

## Next minimal step

Validate a **summary-only routing change in isolation**, keeping the current Jev typed fields, participation flow, generation model and approved prompt untouched. Test the combined `SystemOneParticipationCompletion` contract and failure behavior before any operational activation. This round did not implement that change or authorize a live rollout. Summary accuracy alone must not be presented as a fix for silence or overparticipation, since Jev computes typed fields independently.

## Evidence and verification

- [All 60 comparison outputs, usage and source hashes](summary-models-v1-20260928.json).
- [Original baseline](playful-continuation-20260928.md) and [rejected clause experiment](colloquial-summary-v1-20260928.md).
- Full requests/results: `C:\Users\Helena\filomena-self-reference-tests\playful-evidence-20260928\summary-models-v1.jsonl`, existing private directory restricted to Helena and SYSTEM.
- Disposable harness: `C:\Users\Helena\filomena-self-reference-tests\SummaryModels.cs` and `.csproj`.
- Local-only restore/build: 0 warnings/errors; execution exit 0; 34/34 HTTP 200 and parseable summary objects. One Gemma word-limit failure remains recorded, not converted into a pass.
- Active project hash checked unchanged: `77ECDAABBF48A957516AD18371C32377495843D499F747425AC47A2C414F8FA8`.
- Experiment effects: production changes 0, Jev 0, Graphiti 0, runtime executions 0, outbound 0. This does not claim zero unrelated activity from the online character.
