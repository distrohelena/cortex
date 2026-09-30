# Task 1 report: revisions and executable manifests

Implemented immutable evaluation contracts, deterministic revision hashing, trusted profile capability validation, manifest scope/budget/stop-rule validation, and the minimal CLI project registration. No persistence, execution, comparison, network, model, or other downstream behavior was added.

## Interfaces

- `EvaluationScope`, `RevisionRef`, and `ScopedRevisionRef` carry workspace/project scope explicitly with each pinned revision.
- `RevisionDocument` snapshots its `JsonElement`; `RevisionHasher.Hash` canonicalizes object properties ordinally, preserves array order and number lexical form, rejects duplicate properties and unsupported versions, and returns lowercase SHA-256.
- `ConfigurationProfile` snapshots parameters, criteria, prompts, schema, and optional pinned summary profile. Typed-with-summary requires a summary profile. Choice questions preserve field, instructions, and keyed criteria. Provider capabilities are a frozen in-code descriptor for Luna, LM Studio, and Jev.
- `ManifestValidator.Validate` and `ProfileValidator.Validate` return `ValidationIssue(Code, Path)` diagnostics without echoing values.
- `ExperimentManifest` snapshots candidate references and includes baseline, target, dataset, evaluator, policy, repetition/order/seed, budgets, and stop rules.

## TDD and verification

- RED: initial focused test build failed on the missing Evaluation contracts.
- RED: JsonElement lifetime test failed with `ObjectDisposedException` after its source `JsonDocument` was disposed.
- RED: manifest candidate snapshot test failed after the caller cleared the original list.
- GREEN: `dotnet test tests/Filomena.Evaluation.Tests/Filomena.Evaluation.Tests.csproj --no-restore` — passed, 14 passed, 0 failed, 0 skipped. Live-named opt-in environment variables were cleared for the test process.
- `dotnet build src/Filomena.Evaluation.Cli/Filomena.Evaluation.Cli.csproj` — succeeded, 0 warnings, 0 errors.
- `dotnet sln Filomena.slnx list` — confirmed the library, CLI, and test projects are registered.

## Concerns

The repository has unrelated pre-existing dirty changes; they were left untouched. Existing unrelated suites were not run. No live calls were made. The codebase-memory index did not include Filomena, so no graph coverage is claimed.

## Interrupted-task continuation (2026-09-27)

- Completed the remaining capability correction in `ManifestValidator`: typed-decision capability is independent of the `completion` versus `typed-with-summary` recipe. Jev and Laya now accept typed-only completion with threshold and choice questions; only `typed-with-summary` requires a summary profile, whose provider must support generative completion.
- Provider IDs remain `openai`, `lmstudio`, `jev`, and `laya`; Laya model names are constrained to the values accepted by `SystemOneFlowCompletion`. No provider calls or host configuration reads were made.
- RED: `dotnet test tests/Filomena.Evaluation.Tests/Filomena.Evaluation.Tests.csproj --no-restore --filter FullyQualifiedName~System_one_completion_profiles_can_be_typed_only_without_summary` — failed as expected for both Jev and Laya with `profile.recipe_unsupported`, `profile.threshold_unsupported`, and `profile.choice_questions_unsupported`.
- GREEN: same focused command after the fix — passed, 2 passed, 0 failed, 0 skipped.
- Full focused suite: `dotnet test tests/Filomena.Evaluation.Tests/Filomena.Evaluation.Tests.csproj --no-restore` — 20 passed, 0 failed, 1 skipped (database lifecycle test), 21 total. `OPENAI_LIVE`, `LMSTUDIO_LIVE`, `JEV_LIVE`, and `LAYA_LIVE` were empty for the test process.
- Build: `dotnet build src/Filomena.Evaluation.Cli/Filomena.Evaluation.Cli.csproj --no-restore` — succeeded, 0 warnings, 0 errors.
- `git diff --check` on the five owned files — clean. The first non-escalated test attempt was blocked by access denial writing the test SDK coverage mapping file; the repo-scoped rerun succeeded.
- Remaining concern: codebase-memory’s Filomena project was not indexed, and its installed skill instructions were unavailable at the listed paths. Structural coverage was therefore not claimed.

## Shared-index commit verification

- Actual HEAD/fix SHA: `f6f79effc8412f6a3d0303eb2cf912155b6903ce` (`feat(evaluation): prepare isolated conversation quality variants`).
- The commit contains all five Task 1 files listed above and seven offline variant files concurrently staged by the parent. This was a shared-index race; no reset, revert, amend, or duplicate commit was performed.
- Verified `git diff --exit-code HEAD -- <five Task 1 files>` returned clean, and the commit name list contains each of the five Task 1 files.
- Fresh committed-HEAD suite rerun: `dotnet test tests/Filomena.Evaluation.Tests/Filomena.Evaluation.Tests.csproj --no-restore` — 20 passed, 0 failed, 1 skipped, 21 total. Live opt-in flags were empty.

## Tiny Task 1 fix2 (2026-09-27)

- Restored the advertised OpenAI reasoning effort contract in `ProfileValidator`: `none`, `low`, `medium`, `high`, `xhigh`, and `max` are accepted; `minimal` remains invalid. The active OpenAI adapter is unchanged.
- Added a regression through actual OpenAI profile validation for `gpt-6-luna`, covering `none`, `max`, and rejection of `minimal`.
- RED: focused regression failed on `none` with `profile.parameter_invalid` before the validator correction.
- GREEN: focused regression passed. Full Evaluation suite passed: 21 passed, 0 failed, 1 skipped (database lifecycle), 22 total. CLI build succeeded with 0 warnings and 0 errors.
- No live provider calls were made. Changes remain unstaged; no commit was created.
