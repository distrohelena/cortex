# Laya versus Jev: compact involvement decisions

Date: 2026-09-29. Status: completed isolated pilot; no production activation.

## Question and scope

Can project-specific question/context settings make the cached multilingual Laya checkpoint a credible alternative to Jev for Filomena's involvement classification?

This is a typed-decision test, not the complete Ler Situação/Decidir Presença flow. No generator, Graphiti, conversational runtime, WhatsApp transport, memory writes, or production configuration was invoked or changed. No fine-tuning or probability calibration was performed. Brainstorming constrained the experiment to this single classification task. Existing historical results were not mixed into the scores.

## Frozen protocol

Fourteen synthetic cases, two repetitions each: direct call and paraphrase; reply to Filomena and paraphrase; reply to a third party; mention about Filomena; indirect continuity and paraphrase; explicit topic migration; banal technical vocabulary; quoted historical call; direct social invitation; two longer histories with a current direct or third-party message.

Expected labels, the Portuguese question, and criteria were authored before inference and retained in the JSON. Both models received the same state and question. No adjustments were made after inspecting outcomes. This is a development pilot, not an independent holdout or a ranking of all model capabilities. Two repeated calls establish only observed repeatability, not general stability.

Laya source: `C:\dev\ref\laya`, revision `d120d4ba220711b93c171973118753460310e16b`. Cached multilingual checkpoint: `5e7b2b1b8ca2ecdd3f2322d94069c9b6ce7e844b`. CPU, four Torch threads, offline model loading. Default `max_len=1024`, `head_max_len=256`; second profile changes only `max_len=4096`. Jev requested and returned `jev-1.13.0` through the existing provider endpoint. No package/model update was attempted.

The disposable Python runner directly exercises model/provider contracts, not the C# production adapter. An offline validator checks record counts, unique case/repeat keys, choices, probability keys/ranges/sums, argmax consistency, and accuracy calculations. This does not establish production adapter integration acceptance.

## Results

| Profile | Correct | Repeat disagreements | Mean wall time/call | Reported input/output tokens |
| --- | ---: | ---: | ---: | ---: |
| Laya multilingual, 1024 | 14/28 | 0/14 | 320 ms | 6,928 / 0 |
| Laya multilingual, 4096 | 14/28 | 0/14 | 317 ms | 6,928 / 0 |
| Jev, same compact question | 28/28 | 0/14 | 299 ms | 16,120 / 1,292 |

Laya classified every direct fixture as indirect, including explicit name calls and replies to Filomena. It also classified a third-person mention as indirect. Both profiles made identical choices. Jev matched every expected label in this sample. Both models matched labels across the three paired paraphrase groups, but Laya was consistently wrong on the direct and direct-reply pairs.

No head, option, or state truncation occurred. The instruction head used 42 tokens; options, including markers, used 17/21/19. Long cases used 741 and 849 sequence tokens, below 1024. Consequently the 4096 arm received identical sequences: this is a negative control, **not evidence that a larger context window is useless when truncation actually occurs**. The original short-message failures cannot be attributed to truncation in this pilot.

Timing includes model call plus local evidence preparation for Laya, and network/response parsing for Jev. It excludes Laya initialization (recorded separately in JSON). Sequential arms, different hardware/provider tokenizers, and no dedicated performance isolation preclude a general speed/cost claim.

## Resources and limitations

Completed scored calls: 56 local Laya, 28 remote Jev. Before the scored run, one local attempt failed saving its result due to sandbox filesystem permissions; it is excluded and its inference/usage is unverified. No remote retry occurred. Jev usage is provider-reported, not an inferred tariff: monetary cost remains **unknown**, not zero. Laya incurred local CPU/RAM consumption; its reported zero output tokens is the typed head's usage contract, not zero computation.

The checkpoint is pinned and older than current upstream documentation; no claim about every Laya release or a task-fine-tuned checkpoint follows. No independent holdout, confidence calibration, training, full participation decision, full runtime test, or online deployment was performed. The 4096 arm did not exercise overflow. Full production prompts were not compared here.

## Recommendation

Keep Jev for current typed involvement decisions. A compact project profile plus context expansion alone did not make this Laya checkpoint competitive. Do not tune confidence temperature to disguise wrong argmax labels. If pursuing Laya further, use a separately approved, bounded task-specific training/calibration experiment with a genuine held-out corpus, or evaluate a new pinned checkpoint. Do not activate based on repeatability alone.

## Artifacts and verification

- `laya-comparison-20260929.json`: exact synthetic states, labels, question, all 84 responses, latency, usage, checkpoint configuration and initialization time.
- `laya-comparison-20260929.py`: experimental runner; Jev credential supplied only via process environment.
- `verify-laya-comparison-20260929.py`: offline evidence validator. Executed successfully: all 84 records validated; no head/option/state truncation.

Experiment processes exited. No model-serving process was started. Production was not restarted or edited. Existing working-tree changes were preserved. No commit or push.
