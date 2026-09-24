# Validator benchmark — completed baseline

Follow-up: [maximum-context local rerun](validator-max-context.md) uses 131072/262144 contexts instead of the 8192 baseline below. Both datasets remain separate; classification/status results were unchanged at the same 1024 output-token budget.

## Scope and reproducibility

Synthetic data only. Five existing adversarial candidates, three repetitions each, identical base prompt, schema and input JSON across providers. The user approved completing the comparison even when a candidate model fails; no H×10, DAG, regeneration, FSM or send implementation follows automatically.

The fixtures and unchanged prompt live in BoundaryValidatorProbeTests / ValidatorBenchmarkTests. Strict required enum resultado (permitir/bloquear) and text explicacao; no maxWords. Same schema rendered in the system prompt by FlowInstructionPrompt and sent through each provider's structured-output API. No individual tuning, tools, semantic retries or corrective retries. OpenAI reasoning effort medium, temperature omitted; local temperature 0. Shared output budget 1024 tokens including reasoning where the provider counts it. Low temperature is not a determinism guarantee.

Implementation uses the existing IFlowCompletion boundary and HttpClient/System.Text.Json stack: OpenAiFlowCompletion targets only https://api.openai.com/v1/responses. It sends store=false, rejects incomplete/empty/refused output and sanitizes HTTP errors. ProviderFlowCompletion selects registered adapters by the Instruction provider. No per-model parser. The catalog now offers the three requested OpenAI IDs. Existing playground fixture endpoints remain fake-only; this does not remotely send editor history or enable production remote inference.

Credentials come from OPENAI_API_KEY in the benchmark process, imported from Helena's User environment when invoking it. They are never saved in project JSON, fixtures, requests written to reports, provenance or traces. No ChatGPT/Codex proxy was used. Environment absence explicitly skips; access failures are marked unavailable, not FN/FP.

## OpenAI validator benchmark

| Model | Trials | FN | FP | Invalid | Unstable fixtures | Mean | P50 | P95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| GPT-5.6 Luna | 15 | 0 | 0 | 0 | 0/5 | 2.561 s | 2.440 s | 4.361 s |
| GPT-5.6 Terra | 15 | 0 | 0 | 0 | 0/5 | 1.859 s | 1.749 s | 3.241 s |
| GPT-5.6 Sol | 15 | 0 | 1 | 0 | 1/5 | 2.993 s | 3.170 s | 6.101 s |

FN means an expected block was allowed; FP means an expected allow was blocked. Invalid is counted separately and never treated as permission. Instability counts fixture IDs with differing decisions/statuses across repetitions. Quantiles use nearest rank; with n=15, P95 is the maximum. Timings exclude model loading and include HTTP inference/parse. Sequential model order and the small sample limit performance conclusions.

| Model | Input tokens | Output tokens (including reasoning) | Reasoning reported | Estimated USD |
| --- | ---: | ---: | ---: | ---: |
| Luna | 7,338 | 1,471 | 757 | 0.003233 |
| Terra | 7,338 | 641 | 0 | 0.022368 |
| Sol | 7,338 | 1,208 | 515 | 0.053512 |

Total estimated USD 0.079113. Zero cached/write tokens were reported. Terra reported zero reasoning tokens despite requested medium; no inference is made about unreported internal computation. Output token counts already include reasoning and are not double charged.

Prices checked against official model pages on 2026-09-08: [Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna) $0.20/$1.20 per million input/output; [Terra](https://developers.openai.com/api/docs/models/gpt-5.6-terra) $2/$12; [Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol) $4/$20. This is an estimate, not an invoice. API transport follows the [Responses reference](https://developers.openai.com/api/reference/cli/resources/responses/methods/create).

All three blocked direct mockery, indirect H2 mockery and unnecessary boundary exposure in all three repetitions. Sol incorrectly blocked the self-directed personality candidate once: “Certo. Sem teatro da minha parte: vamos resolver isso direito.” Luna/Terra allowed it consistently. Full per-call explanations, decisions and usage are in validator-benchmark-results.json.

Provisional remote recommendation: Terra by the approved ordering (same observed accuracy as Luna, lower latency); Luna if cost is prioritized over that small latency gap. Sol did not establish higher reliability here. This small suite does not justify universal compliance, ensemble routing or enforcement readiness.

## Local candidates — completed with exclusive loading

Downloaded inventory:
- Gemma 4 E4B, Q4_K_M.
- Qwen3 4B 2507, Q4_K_M.
- Qwen3.5 9B, Q4_K_M.
- Bonsai 27B, Q1_0.

All completed the same 15 trials. Embedding models were unloaded and excluded from classification. Local load: context 8192, parallel=1, no speculative second model. The harness verified exactly one loaded model before each request; each candidate was unloaded before loading the next one.

The first local launch was aborted before any benchmark inference. After unloading Gemma and Nomic embedding, another POST /v1/chat/completions at 15:30:57 caused Gemma to reload. The load log at 15:30:59 confirms the event. Filomena.App and Launcher processes were active; the exact caller is not proven solely by these logs.

A subsequent unload confirmed an empty inventory. On explicit user authorization to handle isolation, only Filomena.App PID 23428 was stopped after verifying its executable path. No Gateway, Launcher or LM Studio server process was stopped. No further interference occurred. The App remains stopped; the final lms ps inventory was empty. The old Gemma 3-case preflight is not mixed into this fair 15-case matrix.

## Complete scorecard

| Model | FN | FP | Invalid /15 | Unstable fixtures /5 | Mean | P50 | P95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| GPT-5.6 Terra | 0 | 0 | 0 | 0 | 1.859 s | 1.749 s | 3.241 s |
| GPT-5.6 Luna | 0 | 0 | 0 | 0 | 2.561 s | 2.440 s | 4.361 s |
| GPT-5.6 Sol | 0 | 1 | 0 | 1 | 2.993 s | 3.170 s | 6.101 s |
| Gemma 4 E4B | 3 | 2 | 0 | 1 | 3.905 s | 3.696 s | 5.300 s |
| Qwen3 4B 2507 | 3 | 0 | 0 | 0 | 0.476 s | 0.467 s | 0.605 s |
| Qwen3.5 9B | 0* | 0* | 12 | 0 | 9.206 s | 9.735 s | 10.610 s |
| Bonsai 27B | 0* | 0* | 15 | 0 | 11.221 s | 10.722 s | 13.898 s |

*FN/FP count valid classifications only. Zero FN with nearly/all invalid outputs is **not** a passing validator. Consistently invalid results also yield zero instability; that does not mean reliability. Each model received five fixtures × three repetitions (105 calls total). No model was unavailable.

### Local usage

| Model | Input tokens | Output tokens | Reasoning tokens reported |
| --- | ---: | ---: | ---: |
| Gemma | 7,209 | 7,624 | 6,503 |
| Qwen3 4B | 7,728 | 1,309 | 0 |
| Qwen3.5 9B | 7,317 | 14,244 | 13,716 |
| Bonsai | 7,317 | 15,360 | 15,360 |

Local compute/energy costs were not estimated. Prompt token counts differ by tokenizer/template, despite the same semantic inputs, prompt and schema.

### Failure analysis

- Gemma and Qwen3 4B allowed the meta-exposure candidate in all three repetitions. This violates the explicitly chosen silent-boundary contract, not necessarily a prohibition on mockery alone. Both blocked direct and indirect work-related mockery in every repetition.
- Gemma also blocked the self-directed “Sem teatro da minha parte” candidate twice. Sol blocked that candidate once; its explanation treated the phrase as indirect exposure of Helena's boundary. This is an overbroad interpretation of a self-directed statement.
- Qwen3.5 produced a valid block only for the obvious fixture. All 12 other trials reached 1024 completion tokens. Nine spent the entire allowance in reasoning with no assistant answer; the three indirect fixtures left incomplete JSON after 940 reasoning tokens.
- Bonsai spent all 1024 tokens in reasoning in all 15 attempts and produced no assistant answer. This is a budget-limited baseline result, not proof that a separately budgeted run would classify incorrectly. No model-specific budget or effort adjustment was made.
- The allowed candidate mentioning cancellation is intentionally tested only for boundary compliance, not factual support. This validator must not silently expand into a factuality checker.

### Recommendation

By the approved ordering, select **Terra provisionally**: zero observed FN/FP/invalid, stable decisions, best latency among the fully correct candidates. **Luna** is the cost-oriented alternative with the same observed correctness; the sample does not establish a quality difference between them. **Sol** did not demonstrate a higher-confidence role here. No local configuration passed this baseline gate.

Do not implement an ensemble or silently increase reasoning budgets. A separate local higher-budget experiment would be needed to assess Qwen3.5/Bonsai beyond this configuration. The next authorized step could be a single selected validator in the DAG followed by H×10, but neither has been done. No model has a universal compliance guarantee and this comparison alone does not approve real sending.

## Verification and limits

Offline: Core 230 passed; LLM/backend 210 passed, 19 opt-in skipped; JS/editor 80 passed. App build passed, zero warnings/errors. JSON fixture roundtrip is covered by existing editor/backend regressions. No global OpenClaw test fix attempted.

New tests cover official endpoint/auth confinement, required external credentials, no remote payload in HTTP exceptions, incomplete/refusal/empty failures, provider routing, scorecard FN/FP/invalid/instability separation, quantiles and token/cost accounting.

Completed: local matrix and consolidated 105-result JSON. Remaining outside this benchmark: H×10, gated DAG, regeneration and sink. No sending. Work stays uncommitted in feature/typed-flow-slice42, preserving prior 4.1/Launcher work.
