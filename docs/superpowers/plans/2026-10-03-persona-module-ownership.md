# Optional Cortex.Personas relocation implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Execution is native, without subagents or an independent reviewer. Implementation starts only after Helena reviews this written plan.

**Goal:** Continue the accepted offline persona slice in Cortex without changing its behavior, modifying Damma, or attaching it to Filomena.

**Architecture:** Port the committed pure library into an optional `Cortex.Personas` assembly. Keep Core independent and application composition absent. Preserve the characterized evidence/revision/compiler contracts through the relocated tests and an explicit serialized-artifact baseline.

**Tech Stack:** .NET 10, System.Text.Json, xUnit v3, existing Cortex host-authority test fixtures, and Node's built-in architecture test runner.

**Spec:** [Approved ownership design](../specs/2026-10-03-persona-module-ownership-design.md).

## Global constraints

- Source of the port: Damma commit `7d3053c477869649394ae41450347bc7b87a3bda`, read-only, not its mutable working tree. Do not cherry-pick the entire commit.
- Work in the current Cortex checkout, preserving existing changes and newer inference work. No new worktree, Damma/Filo writes, push, or automatic implementation commit.
- Library: `net10.0`, nullable, warnings-as-errors; no package, database, Core, Filo, or Damma project dependency. Use the existing Cortex SDK pin.
- Tests retain Microsoft.NET.Test.Sdk `17.14.1`, xunit.v3 `3.0.1`, and xunit.runner.visualstudio `3.1.5`; use the local dependency cache only.
- Preserve JSON contract version 1, field/enum names, hashes, offset semantics, scope checks, snapshot immutability, contextual matching, and supersession/origin validation.
- No character activation, inference, database/service activation, Graphiti activity, sender, runtime wiring, background worker, migration, or personality tuning.
- Old Damma code stays inert; its owner coordinates eventual cleanup. The Cortex port becomes this agent's continuation, not a second independently evolving implementation.

## Review focus

1. Assembly/namespace rename changes serialized bytes: Task 1 pins the existing synthetic smoke artifact hash.
2. Relocation introduces an application dependency: Task 2 rejects dependencies from Core/Filo to Personas and from Personas to other projects/packages.
3. Mutable Damma work is accidentally imported: Task 1 inventories exact committed paths and checks normalized source equivalence.
4. Negative cases disappear while the happy path passes: Task 1 preserves every baseline test and Task 3 checks the full execution result, with no skips.
5. Solution registration silently enables runtime composition: Task 2 asserts optional dependency direction and the two-project offline solution; Task 3 reviews the bounded diff for operational wiring.

## Files and interfaces

Create `src/Cortex.Personas/Cortex.Personas.csproj` and these six source files: `PersonaEvidence.cs`, `BehaviorPattern.cs`, `PersonaSpec.cs`, `PersonaRevisionCodec.cs`, `PersonaSituation.cs`, `PersonaContextCompiler.cs`.

Create `tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj` and preserve these eight source files: `HostBoundPersonaProjectionTests.cs`, `OfflinePersonaSmokeTests.cs`, `PersonaContextCompilerTests.cs`, `PersonaFixtures.cs`, `PersonaInferenceFixture.cs`, `PersonaIntegrityRegressionTests.cs`, `PersonaRevisionCodecTests.cs`, `PersonaRevisionIsolationTests.cs`. Add `PersonaRelocationCompatibilityTests.cs` for the artifact baseline.

Additional files: create `Personas.slnx`, `tests/architecture/persona-module.test.mjs`, and `docs/personas-offline.md`; modify only the project entries in `Cortex.slnx`. Record relocation evidence in `docs/personas-relocation-report.md` after verification. Do not modify the historical repository split manifest or frozen compatibility artifacts.

Preserve these public signatures, changing only their namespace to `Cortex.Personas`:

```csharp
ValidatedPersonaRevision PersonaRevisionCodec.Encode(PersonaRevisionBundle bundle)
ValidatedPersonaRevision PersonaRevisionCodec.Decode(ReadOnlyMemory<byte> payload, string expectedSha256)
PersonaSelection PersonaContextCompiler.Select(ValidatedPersonaRevision revision, PersonaSituation situation, int maxPatterns)
PersonaProjection PersonaContextCompiler.Compile(ValidatedPersonaRevision revision, PersonaSituation situation, int maxPatterns)
```

## Task 1: Port the characterized library and its complete offline suite

**Consumes:** The six library source files, two project files, and eight test/fixture source files under `personas/` in the pinned Damma commit.

**Produces:** The same interfaces and test behavior under `Cortex.Personas` / `Cortex.Personas.Tests`; no new runtime API.

- [x] Inventory the committed paths using `git -C C:/dev/damma ls-tree -r --name-only 7d3053c -- personas/src personas/tests`. Read contents using `git show <commit>:<path>`. Exclude `.git`, SDK overrides, local caches, outputs, and unrelated documentary code.
- [x] Confirm the synthetic source-baseline smoke hash from the existing offline result, read-only: `a466eb8e6e95ed859ced9ddc7c841f898b89e856b292a9f6f024190219698481`. Its source is `PersonaFixtures.SmokeBundle()`, not a live persona. The historical TRX is evidence, not a copied project dependency.
- [x] Add the port with `apply_patch`. Limit source changes to namespace/usings and necessary project names/references. Keep the library dependency-free. The test references become `../../src/Cortex.Personas/Cortex.Personas.csproj` and `../../src/Cortex.Core/Cortex.Core.csproj`.
- [x] Add `FrozenSmokeArtifactHashIsPreserved` in `PersonaRelocationCompatibilityTests.cs` with the assertion below. Do not derive its expected value from the relocated codec. This is characterization, not a reason to manufacture a failing baseline or tune serialization.

```csharp
Assert.Equal(
    "a466eb8e6e95ed859ced9ddc7c841f898b89e856b292a9f6f024190219698481",
    new PersonaRevisionCodec().Encode(PersonaFixtures.SmokeBundle()).ArtifactSha256);
```

- [x] Inspect project build targets and fake adapters before running. Restore the test project from the local cache, then execute it:

```powershell
dotnet restore tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --source 'C:\Users\Helena\.nuget\packages' -p:NuGetAudit=false
dotnet test tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --no-restore
```

Expected: the 84 existing cases plus the artifact test pass, zero skipped; the 12 smoke cases remain synthetic contract checks. If a dependency is absent, report the missing package rather than enabling network restore silently.

- [x] Compare each ported source/test against its committed original after normalizing only `Damma.Personas` to `Cortex.Personas` and line endings. Source bodies must be equivalent; account separately for the renamed project references and new artifact test. Record the complete baseline test-file inventory.

## Task 2: Package the optional boundary and prove dependency direction

**Consumes:** Task 1's two projects. **Produces:** Explicit main-solution entries, a two-project offline entrypoint, and architecture guards. No runtime consumer.

- [x] Create `tests/architecture/persona-module.test.mjs`, using `node:test`, `node:assert/strict`, and read-only filesystem inspection, with these named assertions:
  - `persona projects are explicitly registered`: `Cortex.slnx` contains exactly one entry for each new project.
  - `offline solution contains only persona projects`: `Personas.slnx` contains exactly those two entries.
  - `persona library has no project or package dependencies`: its project XML has zero `ProjectReference` and zero `PackageReference` elements.
  - `core and Filo production projects do not depend on personas`: Core and the actual `C:/dev/filo/src` project files have no `ProjectReference` to the optional assembly. Missing required local files fail rather than silently skipping the check. Read Filo only; do not modify it.
  - `persona tests use only the approved local project references`: the test project's resolved project references are precisely the library and Cortex.Core, and neither points into Damma/Filo.
- [x] Run `node --test tests/architecture/persona-module.test.mjs`. Before solution registration, expect the registration/offline-solution cases to fail for the missing entries/file. Other assertions are characterization and may already pass.
- [x] Add the two entries to `Cortex.slnx`. Create `Personas.slnx` containing only `src/Cortex.Personas/Cortex.Personas.csproj` and `tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj`. Do not add any production reference or modify build scripts.
- [x] Run the new guards alongside the existing checks:

```powershell
node --test tests/architecture/persona-module.test.mjs tests/architecture/cortex-foundation.test.mjs tests/architecture/repository-split.test.mjs
```

Expected: all cases pass, including frozen compatibility hashes. The guard documents this slice's optional boundary; a future approved consumer must deliberately update it rather than bypass it.

## Task 3: Document ownership and accept the bounded relocation

**Consumes:** The verified port and dependency guards. **Produces:** Usage/provenance documentation and an evidence-based acceptance report, not live personality coverage.

- [x] Write `docs/personas-offline.md`: module purpose, preserved public interfaces, synthetic example, offline commands, scope-versus-authority distinction, observed behavior versus interpretation, and explicit absent capabilities (extraction, persistence, publication, automatic evolution, feedback UI, runtime attachment). State Cortex/Filo ownership and Damma's separate documentary role.
- [x] Run bounded acceptance from Cortex, after local-cache restore:

```powershell
dotnet restore Personas.slnx --source 'C:\Users\Helena\.nuget\packages' -p:NuGetAudit=false
dotnet build Personas.slnx --no-restore
dotnet test tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --no-restore --logger 'trx;LogFileName=persona-relocation.trx' --results-directory .tmp/persona-relocation
dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~HostInferenceAuthorityTests|FullyQualifiedName~InstructionContextTests'
node --test tests/architecture/persona-module.test.mjs tests/architecture/cortex-foundation.test.mjs tests/architecture/repository-split.test.mjs
$personaFiles = @(rg --files src/Cortex.Personas tests/Cortex.Personas.Tests -g '*.cs' | Where-Object { $_ -notmatch '[\\/](bin|obj)[\\/]' })
dotnet format Personas.slnx --verify-no-changes --no-restore --include $personaFiles
git diff --check
```

Expected: build zero warnings/errors; all relocated tests pass without skips; the 21 characterized Cortex authority/context cases pass; Node guards and scoped formatting pass. Counts are expectations from the existing baseline, not claims until executed. Do not run a broad Filo build or unrelated integration suites.

- [x] Self-review source and project diffs: preserve strict admission, hashes/Unicode offsets, immutable snapshots, scope matching, unknown conditions/exceptions, origin deduplication and supersession checks; preserve grant revocation, cancellation/failure and invalid-output tests against the actual Cortex host with a fake adapter. Confirm no new source, provider, system instruction, credential or effect authority.
- [x] Write `docs/personas-relocation-report.md` with pinned source commit/path provenance, normalized comparison result, exact commands/counts, artifact hash, validation limits, and deferred Damma cleanup. Keep test output in the local ignored results directory; do not commit TRX/build outputs.
- [x] Inspect Git status in Cortex/Filo/Damma read-only and compare with the starting status. Only the listed Cortex files may belong to this slice. Leave concurrent/unrelated changes untouched; do not infer ownership merely from a dirty worktree.
- [x] Hand off the completed local slice with its evidence and limitations. No automatic commit, live inference, database deployment, corpus processing, or Filomena activation. A later source-to-candidate evidence experiment requires its own bounded approval and must not silently publish a personality.

## Written-plan review gate

Execution record: approved by Helena and completed offline on 2026-10-03. See [relocation acceptance](../../personas-relocation-report.md). Test results were kept in this plan's ignored `.superpowers` workspace instead of the preexisting `.tmp` directory; no behavioral change or production activation was performed.

This plan implements the approved relocation design only. After Helena confirms this plan, execute natively with the existing no-subagent/no-independent-review constraints. No remaining architectural choice is required for this slice; storage and automatic evolution remain separate future work.
