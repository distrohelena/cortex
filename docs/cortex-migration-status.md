# Cortex migration status

Latest acceptance update: see **2026-10-01: local acceptance blockers closed** below.
Earlier failed runs are retained as historical evidence, not the current status.

## 2026-09-30: Core compatibility slice

Base checkpoint: `17ad29a`. This records the first implementation slice, not completion of the entire Cortex migration.

- Moved the shared Core project and its tests to `Cortex.Core` and `Cortex.Core.Tests`.
- Updated solution, application, adapter, evaluation, test and tool compilation references.
- Preserved `Filomena.Core.Identity.FilomenaIdentity` in `Cortex.Core/Compatibility/Filomena` as the approved compatibility exception. Identity algorithms and persisted values are unchanged.
- Added an assembly metadata test proving the identity type resides in Cortex.Core and Core references no application/adapters.
- Fixed 53 approved artifact SHA-256 values independently of runtime outputs. Tests do not regenerate their expected values.
- Filomena.App, personality, project JSON, prompts, models, migrations, transport and active installation were not changed operationally.

## Verification executed

Restore used only `C:\Users\Helena\.nuget\packages`, with `NuGetAudit=false`; no provider or database acceptance was run.

Baseline under `.tmp-cortex-baseline`: 57 identity/host-bound tests and 10 migration/storage tests passed.

Post-rename under `.tmp-cortex-verification/artifacts`:

| Check | Result |
|---|---|
| Core identity, host-bound brain/session, context migration, observed shadow/live and journal recovery, assembly metadata | 128 passed |
| DirectParticipationRuleTests, GraphitiMemoryTests, GraphitiParticipantTests | 90 passed |
| GraphitiCanaryTests | 1 skipped; live opt-in was not enabled |
| MigrationCatalogTests and StorageDefinitionTests | 10 passed |
| member-dm-enrollment, native-conversation-runner, conversation-memory Node tests | 43 passed |
| architecture/hash guards | 2 passed, 53 unchanged artifact hashes |
| Filomena.App build | passed, no warnings/errors |
| ImportLiveContext and ReconcileNotSent builds | passed, no warnings/errors; executables not run |
| git diff --check | passed |

The architecture test first failed specifically because Cortex.Core did not exist, then passed after the move. Existing characterization tests remained green. Review was a self-review, not an independent agent review.

## Limits and findings

The complete solution build is NOT green: existing OpenClaw tests reference absent `Filomena.App.Frontend` and `Filomena.App.Settings` types. Those source trees are absent from the base checkpoint too. This slice does not invent replacements or delete those tests. Application and other consumers compile.

An initial test run under `.tmp-cortex-renamed` failed because older direct-participation fixtures ascend five directories from the assembly. Using the fresh `.tmp-cortex-verification/artifacts` layout restored the expected depth without changing fixtures or brain behavior; all 59 affected cases then passed.

No deploy, restart, models, Graphiti queries/writes, message send or commit was performed. DREAMS was not touched.

## Remaining approved plan

Tasks 3–6 remain: extract Cortex.Inference, rename Memory/OpenClaw/Evaluation/CLI, retain SQL logical resource names, and complete final compatibility verification. Memory still has the explicit legacy Filomena identity policy; this rename does not provide multi-application ownership.

## 2026-10-01: remaining source migration implemented

The paragraph above is the historical checkpoint. Core, Inference, Memory,
OpenClaw, Evaluation and Evaluation.Cli now use Cortex names. Filomena.App,
Filomena.Llm and the aggregate Filomena.slnx remain. No operational deployment occurred.

### Narrow design adjustment

EvaluationProviderFactory used SystemOneParticipationCompletion, a dependency missed
by the plan. The schema split/merge mechanism moved to
`Cortex.Inference.SystemOne.TypedSummaryComposition`; Filomena's node routing,
validation of its node bindings, defaults and public wrapper remain in Filomena.Llm.
Both consumers reuse the same mechanism, with no prompt, model, fallback or wire change.

SystemOne HTTP fixtures are source-linked into both test assemblies. OpenAI adapter
tests now carry a synthetic request instead of referencing a live Boundary probe class.
The workbench's old choice-profile assertion expected a string instruction; its
pre-existing failure was reproduced with the previous DLL. Only that assertion
was updated to verify `instructions.choiceGuidance` and preservation of the pinned
`instructions.task`, matching the existing adapter contract. No provider behavior changed.

### Final verification

All eight production projects compile through App and Evaluation.Cli in the fresh
`.tmp-cortex-final/artifacts` root, with zero warnings/errors. Both external tools
also compile. Restore used only the existing local NuGet cache. CLI execution with
no arguments and with unbound `persisted` mode returns exit 2 and the expected
`usage_offline_json_stdin_required` / `host_binding_required` errors without starting a store.

| Verification | Result |
|---|---|
| Cortex.Inference.Tests, entire suite | 46 passed |
| Cortex.Evaluation.Tests, excluding Postgres/LocalSocket/DatabaseLifecycle | 633 passed |
| Filomena composition/direct participation, memory adapters and assembly boundaries | 119 passed |
| Core targeted compatibility, identity, host-bound, journals and session tests | 128 passed |
| NativeObservationReaderTests and OpenClawMcpSenderTests | 102 passed |
| Three operational Node test files | 43 passed |
| Architecture/path/hash tests | 3 passed; all 53 frozen file hashes preserved |
| Whole Core suite (broader diagnostic) | 713 passed, 19 failed, 10 live tests skipped |
| Complete solution build | blocked by 8 missing-type compile errors in two historical UI test files |

The 102 transport tests ran from their unchanged source files linked into a temporary
test project at `.tmp-cortex-1001/transport-compatibility`, because the normal test
project cannot compile the unrelated historical UI tests. No tests were removed
from the normal project. This is targeted evidence, not a green OpenClaw suite.

The 19 Core failures were reproduced on the pre-rename Filomena.Core test assembly:

- FlowBindingValidatorTests: RequiresPrivateSpaceOwnerCharacterToExist,
  RejectsIngressGraphsCarryingCharacterIds,
  ValidatesUnusedGrantCharacterSpaceAndMediatedProtectorReferences,
  RejectsDuplicateDestinationIdsWithinAnEnvironment,
  RequiresCharacterGraphReferenceToBeACharacterGraphOwnedByThatCharacter.
- FlowCompilerTests.MalformedDirectObjectsReturnSafeIssues: character, environment,
  profile, destination, source, fieldPath, field, node, controlEdge, conversation,
  space, dataEdge and graph (13 cases).
- FlowCompilerTests.OversizedImportIsRejectedBeforeAllocatingItsUtf8Payload.

The binding group has five failures; compiler group
has fourteen (thirteen malformed cases plus one allocation case), totaling nineteen.
These are genuine pre-existing validation failures, not waived acceptance criteria.
FrontendBuildTests and SettingsStoreTests still reference absent App.Frontend and
App.Settings types. No replacement UI or runtime validation changes were invented here.

### Review and compatibility

A source-body comparison against 17ad29a covered 210 original production C# files:
207 are identical after ignoring namespace/import renaming. The three reviewed
differences are the previously qualified Core reference in NativeMemoryOutbox,
the participation wrapper extraction and the matching Evaluation factory call.
All SQL bytes/checksums and legacy embedded resource names remain unchanged.
Identity remains `Filomena.Core.Identity.FilomenaIdentity` in Cortex.Core by design.
No assembly-qualified persisted type contract was found in the inspected sources.

This was self-review, not an independent review. The user-required separate approval
for independent review was respected. Memory ownership, PostgreSQL acceptance,
new products, performance work and the older flow/UI failures were not expanded
into this rename. Existing source and historical documents were preserved.

### Reproduction and status

Build/test commands used `--artifacts-path .tmp-cortex-final/artifacts`; build restore
properties were `-p:RestoreSources=C:\Users\Helena\.nuget\packages -p:NuGetAudit=false`.
Tests of the final binaries used `--no-build --no-restore`. The workbench filter was
`Category!=Postgres&Category!=LocalSocket&FullyQualifiedName!~DatabaseLifecycleTests`.
Node checks: `node --test tests/architecture/cortex-foundation.test.mjs` and
`node --test tools/member-dm-enrollment.test.mjs tools/native-conversation-runner.test.mjs tools/conversation-memory.test.mjs`.

Source migration implemented; global acceptance remains pending the documented
pre-existing failures. No commit, push, database migration, service restart,
live model call, Graphiti operation or outbound message. DREAMS and the running
installation remain untouched. Do not mix old and new DLLs in a future deployment.

## 2026-10-01: local acceptance blockers closed

The subsequent authorization to continue covered the pre-existing local acceptance
failures. First, the unchanged compiler/binding suite reproduced 19 failures and
36 passes against the renamed assembly. The fixes are deliberately separate from
the mechanical namespace migration:

- `FlowCompiler.Compile` validates required object/collection elements before
  traversing or freezing them, returning `compiler.shape_invalid` with a structural
  path rather than throwing on malformed direct objects. `FlowBindingValidator`
  uses the same small shape validator for direct callers.
- `FlowCompiler.ReadArtifact` checks the character lower bound and strict UTF-8
  byte count before allocating the UTF-8 payload. Invalid encoding is still rejected.
- `FlowBindingValidator` now enforces character graph ownership/role, prohibits
  ingress character identity, resolves private space owners and unused grants,
  requires mediated grant protectors, and rejects duplicate destination IDs within
  one environment. Valid cross-environment rebinding remains accepted.

All 55 compiler/binding tests now pass, including the original 19 failures.

The two dashboard drafts were not implemented or counted as passing tests. The
2026-09-05 flow-engine specification explicitly supersedes that settings dashboard
and records that its components were never delivered. Both source files are preserved
under `tests/Archived/LocalDashboard/*.cs.txt`, with an explanation and source references.
This is explicit retirement of superseded test drafts, not proof of dashboard coverage.
No tests for the current transport implementation were excluded.

### Fresh verification

| Check | Result |
| --- | --- |
| Entire `Filomena.slnx` build | PASS, 0 warnings, 0 errors |
| Cortex.Core.Tests | 732 passed, 10 live tests skipped |
| Cortex.OpenClaw.Tests, normal project | 197 passed |
| Cortex.Inference.Tests | 46 passed |
| Filomena.Llm.Tests | 790 passed, 49 opt-in tests skipped |
| Cortex.Evaluation.Tests, offline filter | 633 passed |
| Architecture/path/hash checks | 3 passed, 53 frozen hashes unchanged |
| Whitespace check of the three changed validation files | PASS |
| `git diff --check` | PASS; Git reports existing LF/CRLF conversion notices |

The five .NET test projects total 2,398 passing tests in this run. This is not full
integration acceptance: the workbench filter remains
`Category!=Postgres&Category!=LocalSocket&FullyQualifiedName!~DatabaseLifecycleTests`.
Live model and Graphiti opt-ins were disabled in the child test processes. Local
HTTP tests used temporary loopback servers and synthetic dependencies, not the active
Gateway or Filomena instance. Production settings/environment were not modified.

Artifacts: `.tmp-cortex-final/artifacts`; logs:
`.tmp-cortex-1001/{core-fix-red,core-fix-full,solution-fixed,transport-fixed,evaluation-fixed,llm-fixed}.log`.
The source changes remain uncommitted. No deployment, live inference, memory access,
outbound, service restart or push was performed. PostgreSQL acceptance and any live
deployment remain separate work. This validation does not claim a completed platform
redesign or full multi-project memory isolation.
