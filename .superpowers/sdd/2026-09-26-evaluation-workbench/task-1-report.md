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
