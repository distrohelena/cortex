# Playful continuation: current-stage baseline

## Scope and provenance

Approved bounded local evaluation: six controlled cases, three repetitions each, using the active `group-situation` prompt and model bindings. No candidate prompt, deployment, restart, memory access, response generation, participation decision, or message sending.

Project SHA-256: `77ECDAABBF48A957516AD18371C32377495843D499F747425AC47A2C414F8FA8` (verified before and after). Typed fields: `jev-1.13.0`. Summary: local `google/gemma-4-e4b`, temperature 0.3. The actual pinned project's compiled schema and existing SystemOne/LM Studio adapters were used. Context was reconstructed as Helena calling Filomena, followed by Filomena saying “Tô aqui, Helena. O que foi?”. This is **not** a native event replay.

The probe executes only `Ler Situação`. It validates field structure/types but does not execute the full runner's correction retries, `Decidir Presença`, FSM, or generator. Therefore, classifications below are not observed send/silence outcomes. Jev computes typed fields independently of the Gemma summary; a bad summary does not establish why the earlier live execution chose silence.

## Results

| Current message | Summary, 3 repetitions | Jev involvement | Social opportunity |
|---|---|---|---|
| `nada só vim enxer mesmo` | 2 material meaning losses; 1 short quotation with minor `só` → `sô` drift | direct 3/3 | weak 3/3 |
| `nada, só vim encher mesmo` | 1 material meaning loss; 2 added an unsupported target, “encher o chat” | indirect 3/3 | weak 3/3 |
| `Gabriel, só vim te encher mesmo` | Recipient and teasing preserved 3/3 | none 3/3 | none 3/3 |
| `nada, só vim ver se você estava aqui` | Neutral meaning preserved 3/3 | direct 1/3; indirect 2/3 | weak 3/3 |
| `era só isso, boa noite` | Closure preserved 3/3 | indirect 3/3 | none 3/3 |
| `Filomena, vim só te perturbar um pouco` | Playful approach preserved 3/3 | direct 3/3 | strong 3/3 |

Material distortions were “estava passando para ver”, “checar o ambiente”, and “preencher o tempo”. These replace the observed teasing expression with a different activity. The other two corrected-spelling outputs retained “encher” but attached it to the chat without evidence. They are tracked separately, not inflated into five material errors.

All 18 returned `necessita_cuidado=false` and `ambiente=casual`. Casual versus playful environment alone is not considered an error. No summary exceeded 20 whitespace-delimited words. All 36 HTTP requests returned 200; no adapter/schema failures occurred.

The six core-case outputs therefore contain **3 material semantic substitutions, 2 unsupported-target additions, and 1 minor quotation drift**. The 12 contrast outputs preserved their relevant meaning/recipient. This is a small development sample, not a statistical guarantee or holdout comparison.

## Interpretation and smallest next experiment

The meaning loss is reproducible and not confined to misspelling. Two separate boundaries need attention:

1. Gemma's summary paraphrases a colloquial expression into an unsupported purpose. Test a narrow summary instruction to retain the observable expression or conservatively paraphrase it, without inventing a target or purpose. Include new examples, not just “encher”.
2. Jev's direct/indirect classification changes with wording despite the same prior exchange. Compare the existing typed-choice criteria against the approved situation definition, which already includes a clear answer to Filomena's speech. Do not turn every DM, recent message, or playful comment into direct involvement.

Do not force “strong social opportunity” or “respond” to make the original case pass. Understanding a joke and choosing to participate are separate questions. No candidate was promoted or implemented in this baseline probe.

## Usage and validation

| Provider | Calls | Reported input tokens | Reported output tokens | Accumulated HTTP time |
|---|---:|---:|---:|---:|
| Jev | 18 | 82,485 | 3,832 | 5.46 s |
| Local Gemma | 18 | 16,446 | 11,230 | 98.45 s |

Gemma reported 10,502 reasoning tokens, included in completion-token details, not additional billable tokens. No reasoning text was requested or retained. Monetary cost is **unknown**, not zero; no verified tariff was applied. Local resource usage is not cost-free.

Mean stage latency: **5.77 s**, range **4.63–8.09 s**; 103.95 s summed stage duration. The online host was not stopped, so these are observed latencies, not an isolated performance benchmark.

Probe build: .NET 10, existing deployed assemblies, local-only restore; **0 warnings, 0 errors**. Execution: **18/18 completed**, process exit 0. No product-code build/regression claim is made.

## Artifacts

- Comparative structured evidence: [playful-continuation-20260928.json](playful-continuation-20260928.json), including all candidates, manual review categories, usage and hashes.
- Full request/result artifact: `C:\Users\Helena\filomena-self-reference-tests\playful-evidence-20260928\results.jsonl`, ACL restricted to the user and SYSTEM.
- Reproduction harness: `C:\Users\Helena\filomena-self-reference-tests\PlayfulContinuation.cs` and `.csproj`.
- Prior live analysis: [contribution-review-20260928.md](contribution-review-20260928.md).

Evaluation effects: transport 0, Graphiti 0, response generation 0, runtime executions 0, production configuration changes 0. These counts describe this probe, not unrelated concurrent online activity.
