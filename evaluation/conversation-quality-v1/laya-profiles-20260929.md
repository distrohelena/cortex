# Laya project-profile experiments, 2026-09-29

Status: completed offline spike. Jev remains unchanged in production. No activation, service restart, production edit, remote inference, Graphiti access, or outbound was performed.

## Scope and protocol

Follow-up to `laya-comparison-20260929.md`: can question language, wording, serialization, or option labels improve involvement classification on the same cached multilingual checkpoint?

Used the brainstorming spike workflow to bound this to disposable evaluation code rather than production integration. Six profiles and twelve new evaluation cases were fixed before inference. The fourteen previous cases are development data; they are not a new independent test set. Select the highest development accuracy, breaking ties by declared profile order. Freeze selection before running new cases. No post-result tuning.

Checkpoint `5e7b2b1b8ca2ecdd3f2322d94069c9b6ce7e844b/multilingual`, same local source as the preceding experiment, CPU with four Torch threads, offline loading. Default head budget 256 and context 1024. No fine-tuning, temperature changes, provider calls, or new downloads.

## Development results

| Profile | Correct / 14 | Mean inference ms |
| --- | ---: | ---: |
| Previous Portuguese question + JSON | 7 | 330 |
| Short Portuguese question + JSON | 5 | 298 |
| Short English question + JSON | 6 | 292 |
| Short Portuguese question + text | 3 | 294 |
| Short English question + text | 4 | 277 |
| Explicit semantic option labels + text | 6 | 311 |

No state truncation occurred in development. These are profile comparisons, not proof that English or JSON is universally superior. Several variants change interacting factors; do not attribute semantic-label results to labels alone relative to the original baseline. Timing is descriptive on the available CPU, not a controlled hardware benchmark.

## Frozen-profile evaluation on new cases

Selected the original Portuguese/JSON profile. New cases cover direct questions, care requests, replies to Filomena versus another person, social invitations, third-person mentions, migration, indirect continuity, mundane technical messages, and historical quoted calls.

- Normal criteria order: 5/12 correct, identical in two repetitions.
- Reversed criteria order: 7/12 correct, identical in two repetitions.
- Four of twelve decisions changed solely when option order changed.
- All four direct cases were classified indirect in normal order.
- Reversal corrected social invitation, third-person mention and migration, but broke one indirect-continuity case.

This is a small synthetic held-out set relative to profile selection, authored by the same evaluator and covering related phenomena. It is not an external or blinded benchmark. Counts of repeated calls are not additional independent cases. The reversed order was a predeclared sensitivity probe, not an approved replacement selected on the test data.

The instability is observable order sensitivity, not evidence that the model randomly changes between identical calls. Repeatability alone conceals it.

## Actual context-overflow probe

Unlike the prior experiment, two stress inputs exceed 1024 tokens: 1,675 and 1,944 tokens at full coverage. Current messages occur after repeated synthetic history.

| Case | 1024, truncated | 4096, complete | Expected |
| --- | --- | --- | --- |
| Direct request | indirect | none | direct |
| Third-party message | indirect | direct | none |

Both larger-window inputs fit completely, yet both classifications remain wrong. Increasing the window restores information, not correctness. This two-case stress probe does not establish performance on natural long conversations; repeated padding is deliberately synthetic. No context-management change was applied to production.

## Evidence, resources, and limits

136 local inference calls: 84 development, 48 new-case/reordering repetitions, 4 stress calls. Reported usage: 31,261 input tokens and 0 output tokens under Laya's typed-head usage contract. Local CPU/RAM were consumed; zero remote calls means no remote inference charges from this experiment, not zero resource cost. No new Jev measurements were taken; the prior 28/28 Jev result applies only to the earlier corpus.

Artifacts: `laya-profiles-20260929.json`, disposable runner `laya-profiles-20260929.py`, offline validator `verify-laya-profiles-20260929.py`. The JSON includes the frozen protocol hash, questions, criteria, development/new/stress cases, selected profile, all raw typed answers, probabilities, latency and truncation counts. Validator confirmed 136 unique records, unchanged protocol hash, probability ranges/sums, accuracy arithmetic, and repeat agreement within each option order.

The runner depends on the prior `laya-comparison-20260929.json` beside it and refuses to overwrite existing evidence. No production tests/build are claimed: no product code changed. Experiment process exited; no server was started. No commit or push.

## Decision

Keep Jev. None of these project-specific inference profiles justifies replacing it. The new evidence is especially unfavorable on direct addressing and option-order robustness.

A future bounded experiment can test decomposition into independently scored recipient/continuity questions or task-specific training with a separate corpus and untouched evaluation set. Neither is implemented or validated here. Do not turn the current test labels into production heuristics or treat a new confidence threshold as a correction of wrong classifications.
