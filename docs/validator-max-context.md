# Validator local rerun — maximum context

On user request, repeated all four local candidates with the maximum context reported by the installed LM Studio model inventory. The previous baseline explicitly forced 8192; it did not use the maximum saved/available context. This rerun corrects that test configuration without overwriting the baseline.

Five identical synthetic fixtures × three repetitions = 60 calls. Same prompt, inputs, strict schema, temperature 0 and 1024 output tokens (including reasoning). Only context length changed. No OpenAI calls, prompt tuning, retries or wall-clock timeout. Each load used parallel=1; identifier, contextLength and sole-loaded-model status were checked before the benchmark. The harness additionally checked exclusivity before each inference. All models were unloaded at completion.

| Model | Verified context | FN | FP | Invalid /15 | Unstable fixtures /5 | Mean | P50 | P95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Gemma 4 E4B | 131072 | 3 | 2 | 0 | 1 | 3.825 s | 3.655 s | 5.119 s |
| Qwen3 4B 2507 | 262144 | 3 | 0 | 0 | 0 | 4.286 s | 4.495 s | 5.454 s |
| Qwen3.5 9B | 262144 | 0* | 0* | 12 | 0 | 8.774 s | 9.400 s | 9.988 s |
| Bonsai 27B | 262144 | 0* | 0* | 15 | 0 | 45.809 s | 38.543 s | 86.138 s |

*FN/FP count valid classifications only. An invalid response never counts as approval or a successful validator. Nearest-rank P95 is the maximum for 15 samples.

## Findings

Classification/status results are unchanged from the 8192-context run. Gemma and Qwen3 4B still allow unnecessary exposure of the boundary in all three meta fixtures. Gemma still incorrectly blocks the self-directed personality candidate twice.

Qwen3.5 still exhausts the 1024 output-token allowance in 12 attempts, and Bonsai in all 15. Their maximum context does not remove the separately configured completion/reasoning cap. These are budget-limited observations, not proof of semantic failure under an adequate generation budget. A higher-output-budget run would be a separate experiment, not a silent correction to this context-only comparison.

Mean timing versus baseline: Gemma 3.905→3.825 s; Qwen3 4B 0.476→4.286 s; Qwen3.5 9B 9.206→8.774 s; Bonsai 11.221→45.809 s. These are observed wall-clock differences, not isolated GPU benchmarks. During Bonsai, one diagnostic sample showed 23160/24564 MiB GPU memory and 98% GPU utilization; its logs continued to show completed predictions. It was not aborted for slowness.

| Model | Input tokens | Output tokens | Reasoning tokens reported |
| --- | ---: | ---: | ---: |
| Gemma | 7209 | 7624 | 6503 |
| Qwen3 4B | 7728 | 1309 | 0 |
| Qwen3.5 9B | 7317 | 14244 | 13716 |
| Bonsai | 7317 | 15360 | 15360 |

No additional API cost. Local compute/energy cost not estimated. Full synthetic results: validator-max-context-results.json. Baseline retained in validator-benchmark-results.json.

No runtime/source changes in this rerun. No Gateway operation, personal configuration edit, DREAMS edit, push or commit. Filomena.App remains stopped from the previously authorized isolation; LM Studio remains running with no loaded models.
