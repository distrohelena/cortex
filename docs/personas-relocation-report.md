# Cortex.Personas relocation acceptance report

Date: 2026-10-03. Scope: local/offline relocation only. No implementation commit or push performed.

## Ownership and source provenance

The reusable persona slice now continues in Cortex; Filo owns character-specific material and future composition. Damma's document-analysis ownership and its existing files were not changed. Its original persona tree remains historical/inert pending cleanup by its owner.

Source: Damma commit `7d3053c477869649394ae41450347bc7b87a3bda`, read through `git show`/`git ls-tree`, not copied from mutable files. Destination: `src/Cortex.Personas` and `tests/Cortex.Personas.Tests`. Cortex started at `bf62b11` on the existing `feat/damma-person-extraction` branch; the branch and unrelated work were preserved.

All 16 imported files were compared against the committed originals. They are equivalent after only `Damma.Personas` → `Cortex.Personas`, the necessary test project-reference adjustment, and line-ending normalization. No source-body behavior changes were made.

| Original file, relative to `personas/` | Source Git blob |
| --- | --- |
| `src/Damma.Personas/BehaviorPattern.cs` | `debb5e1f88c1145fc0ebc5f1e4ba2c3fdb8e87a1` |
| `src/Damma.Personas/Damma.Personas.csproj` | `da31edf4c722a648e4c3d277a818c48a15408025` |
| `src/Damma.Personas/PersonaContextCompiler.cs` | `c26d4928091fe865ee53f2d415b52178f38e619b` |
| `src/Damma.Personas/PersonaEvidence.cs` | `2d7bc809a90034d05cb52cbee0db0a32805497c5` |
| `src/Damma.Personas/PersonaRevisionCodec.cs` | `0f31bf5aaeea03825c9788b4f9e9daeb7c00d71d` |
| `src/Damma.Personas/PersonaSituation.cs` | `dd3ec8912f6c091f18c044e47a1938d098b2c55f` |
| `src/Damma.Personas/PersonaSpec.cs` | `5fd399231303464f3c7c9ab9f429662e9d13307d` |
| `tests/Damma.Personas.Tests/Damma.Personas.Tests.csproj` | `8ef82dabed4fc398ed3c23005cc4bfa88e1c3a5f` |
| `tests/Damma.Personas.Tests/HostBoundPersonaProjectionTests.cs` | `b007ae215e4fecf5ad199a8f1342bc4fee423a5a` |
| `tests/Damma.Personas.Tests/OfflinePersonaSmokeTests.cs` | `b4af9e4607034016d2a6ea33c8e5163d34f49fa5` |
| `tests/Damma.Personas.Tests/PersonaContextCompilerTests.cs` | `4386a04afc9e5725e3d6c58e5efe2f2f66edce76` |
| `tests/Damma.Personas.Tests/PersonaFixtures.cs` | `2945dc7fdeb841aa800cd7e5e090f6ff75f8d334` |
| `tests/Damma.Personas.Tests/PersonaInferenceFixture.cs` | `55327e1e462d2614993b5b43cb33001ed5a93ad0` |
| `tests/Damma.Personas.Tests/PersonaIntegrityRegressionTests.cs` | `c0a8f17764496db08012f5d053100d63085e0203` |
| `tests/Damma.Personas.Tests/PersonaRevisionCodecTests.cs` | `03e8cc7e7c15fa501f19dad91eaf35cbfe157a8a` |
| `tests/Damma.Personas.Tests/PersonaRevisionIsolationTests.cs` | `48d5929e89e367d83cd59742f19d47c58ddb133e` |

The frozen synthetic `SmokeBundle()` artifact retained SHA-256 `a466eb8e6e95ed859ced9ddc7c841f898b89e856b292a9f6f024190219698481`. The expected value came from the original offline result, before the port; the new compatibility test does not compute its own expectation.

## Changes and preserved boundaries

- Added the optional library, its complete test corpus and one artifact-compatibility test. The library has no package/project dependency; tests reference only Personas and local Cortex.Core.
- Added two explicit entries to `Cortex.slnx` and the delimited `Personas.slnx`; neither Core nor Filo production projects references Personas.
- Added five bounded architecture guards and usage/provenance documentation. The historical split manifest and 53 frozen compatibility artifacts remain unchanged.
- Preserved strict JSON admission, version 1, hashes/Unicode offsets, independent snapshots, scope integrity, conditional matching/uncertainty, counter-evidence, origin deduplication and local supersession validation.
- Preserved the real Cortex authority/fake-adapter checks: evidence cannot alter routing/System/profile, foreign scope/revoked grant is denied, failure/cancellation does not become confirmed success, and invalid output is rejected.

## Verification actually executed

Commands ran from `C:/dev/cortex`. Restore used only `C:/Users/Helena/.nuget/packages`, with NuGet audit disabled; no remote feed or real provider was used.

| Command/check | Result |
| --- | --- |
| `dotnet restore tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --source 'C:\Users\Helena\.nuget\packages' -p:NuGetAudit=false` | Successful local-cache restore |
| `dotnet test tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --no-restore` | 85 passed, 0 failed, 0 skipped |
| New Node architecture guards before solution registration | 2 expected failures: missing entries and missing offline solution; 3 existing-boundary checks passed |
| `dotnet restore Personas.slnx --source 'C:\Users\Helena\.nuget\packages' -p:NuGetAudit=false` | Successful; all projects up to date |
| `dotnet build Personas.slnx --no-restore` | 0 warnings, 0 errors |
| Full persona suite with `--logger 'trx;LogFileName=persona-relocation.trx'` | 85 passed, 0 failed, 0 skipped |
| `dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~HostInferenceAuthorityTests\|FullyQualifiedName~InstructionContextTests'` | 21 passed, 0 failed, 0 skipped |
| `node --test tests/architecture/persona-module.test.mjs tests/architecture/cortex-foundation.test.mjs tests/architecture/repository-split.test.mjs` | 15 passed, 0 failed, 0 skipped |
| `dotnet format Personas.slnx --verify-no-changes --no-restore --include <all 15 persona source/test .cs paths>` | Exit 0; no formatting change required |
| `git diff --check` and explicit whitespace/link checks for new files | Clean |

Relocated cases: codec 20, compiler 16, revision isolation 11, host-bound projection 11, integrity 14, smoke 12, artifact compatibility 1. Total 85; the original 84 remain represented, including negative cases. The 12 smoke cases are included in 85, not an additional live benchmark.

TRX files are retained locally in `.superpowers/sdd/2026-10-03-persona-module-ownership/results/`, an existing ignored workspace. No build output or TRX is part of the proposed source change. This replaces the plan's `.tmp` results location to avoid touching preexisting scratch data.

## Self-review and limits

Author self-review, not an independent review: checked the normalized committed-source comparison, new test assertions, dependency direction, complete baseline case inventory, bounded diff, and lack of operational wiring. No blocking relocation defect was found. No subagent or separate reviewer session was launched.

The architecture guard requires sibling Filo only for its read-only consumer-boundary assertion. Library compilation and persona tests do not depend on Filo/Damma. It checks the concrete project files in this slice; it is not a general MSBuild security auditor of arbitrary imported targets.

This acceptance does **not** prove naturalness/personality fidelity, corpus extraction, source truth, production authorization, live memory, durable persona storage, autonomous evolution, feedback UI or application integration. Supersession remains local to a bundle, and caller-owned inputs must not be concurrently mutated during admission.

No Filomena activation, database/service activation, real model inference, Graphiti read/write, sender call, outbound or Damma/Filo file change was performed. Only local Cortex build/test artifacts were produced.

## Next separately scoped work

Use a small authorized/fictional corpus to produce candidate behavioral evidence with traceable passages, attribution, interpretation alternatives and explicit approval boundaries. A database is not a prerequisite for that experiment. Do not connect or publish a persona automatically; model use and any production attachment require their own authorization.

See [offline usage](personas-offline.md), [approved design](superpowers/specs/2026-10-03-persona-module-ownership-design.md), and [implementation plan](superpowers/plans/2026-10-03-persona-module-ownership.md).
