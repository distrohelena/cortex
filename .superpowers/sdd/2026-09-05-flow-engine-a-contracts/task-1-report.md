# Flow Engine A — Task 1 report

Date: 2026-09-05

## Scope

Implemented Task 1 only in the requested Flow contracts and tests:

- `src/Filomena.Core/Flows/FlowDocuments.cs`
- `src/Filomena.Core/Flows/FlowTypes.cs`
- `src/Filomena.Core/Flows/FlowDocumentReader.cs`
- `src/Filomena.Core/Flows/FlowIssues.cs`
- `tests/Filomena.Core.Tests/Flows/FlowDocumentReaderTests.cs`
- `tests/Filomena.Core.Tests/Flows/FlowTypeTests.cs`

This work did not modify production runtime, settings, identity, Gateway integrations, models,
sends, Graphiti, push state, or `DREAMS.md`. Existing dirty files were preserved.

## Implementation

The portable graph contract now provides:

- `GraphDocument` with `Id`, `FormatVersion`, `GraphRole`, optional `CharacterId`, nodes,
  separate `DataEdges` and `ControlEdges`, and optional `EntryNodeIds`.
- `FlowNode` with `Id`, `Kind`, `BlockVersion`, and an object `Config`.
- `PortRef`, `DataEdge` with `FieldPath`, and `ControlEdge`, with no edge kind discriminator.
- `CharacterDefinition` with stable ID, display name, personality, and graph ID.
- `ModelProfileDefinition` with profile ID and credential reference only; no secret value field.
- The approved `FlowEnvironment`, `JoinMode`, and `FlowEffect` enums.

`FlowDocumentReader.Read(string)` uses bounded UTF-8/STJ parsing, rejects duplicate properties
before document materialization, rejects unknown document/node/edge/port fields, validates the
portable structure, clones config `JsonElement` values for document ownership, and emits safe
JSON-Pointer-style issue paths without including prompt or secret-like values.

Named limits are centralized in `FlowLimits`: 1 MiB UTF-8 input, JSON depth 64, schema depth 16,
256 schema fields, 256 nodes, 1024 total edges, and 32 KiB per prompt.

`FlowType` uses immutable records for string, finite number, integer, boolean, string enum,
closed object fields, arrays, and nullable types. The central codec converts the supported subset
to closed JSON Schema and strictly parses supported schemas/portable type objects, rejecting
remote `$ref`, unknown keywords, duplicate schema properties, and unsupported constructs. The
compatibility relation is directional: integer-to-number and enum-subset-to-superset are allowed;
number-to-integer and optional-to-required are rejected; nested objects and arrays recurse.

## TDD evidence

Tests were written before the initial production implementation. The first focused run failed at
compile time because `Filomena.Core.Flows` did not yet exist. Implementation then proceeded in
small red/green corrections, including valid deep JSON construction, `GraphRole`, schema writer
nesting, malformed-vs-depth classification, and strict schema duplicate handling.

The tests cover malformed JSON, duplicate properties, unsupported versions, unknown fields,
unknown portable type kinds, valid separate graph edges, config ownership, valid deep nesting,
input UTF-8 size, prompt size, node/edge limits, nullable object fields, closed schemas, schema
keywords/remote references, schema depth/field limits, and directional compatibility.

## Commands and results

Initial focused red run:

```text
rtk dotnet test tests\Filomena.Core.Tests\Filomena.Core.Tests.csproj --no-restore --filter FullyQualifiedName~Filomena.Core.Tests.Flows
Build failed because Filomena.Core.Flows was missing.
```

Final focused Release run:

```text
rtk dotnet test tests\Filomena.Core.Tests\Filomena.Core.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~Filomena.Core.Tests.Flows
25 tests passed, 0 warnings.
```

Final full Core Release run:

```text
rtk dotnet test tests\Filomena.Core.Tests\Filomena.Core.Tests.csproj --no-restore -c Release
59 tests passed, 0 warnings.
```

## Concerns / boundaries

Node `Kind` is intentionally retained as a validated non-empty string rather than a hard-coded
Task 1 enum. NodeCatalog owns the later catalog of recognized kinds, block versions, and config
schemas, as directed; the reader still rejects unknown structural fields and all unsupported type
discriminators. Graph-reference existence and graph topology validation remain subsequent
validator responsibilities.

## Reviewer fix round 1

Base commit: `c5af9f75b37fd8a8070ce0e0ef53728a23401cfa`.

Addressed the three scoped reviewer findings:

1. Data-edge `fieldPath` is now required and missing values produce
   `edge.field_required` at `/dataEdges/{index}/fieldPath`; the edge is never silently
   omitted from an issue-free result.
2. Object compatibility now enforces source fields as a subset of target fields, checks
   every source field recursively against its target field, and requires every target
   required field to be present and required in the source. Target optional fields may be
   absent from the source.
3. Nullable enum JSON Schema emits `null` in the enum alongside string members, and the
   nullable-only parser accepts and normalizes that null member while non-nullable enums
   remain strings-only.

TDD regression run before production fixes:

```text
rtk dotnet test tests\Filomena.Core.Tests\Filomena.Core.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~Filomena.Core.Tests.Flows
26 passed, 5 failed: the three reviewer defects reproduced (with directional cases split into focused assertions).
```

Focused regression run after fixes:

```text
rtk dotnet test tests\Filomena.Core.Tests\Filomena.Core.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~Filomena.Core.Tests.Flows
31 tests passed, 0 warnings.
```

Full Core Release run after fixes:

```text
rtk dotnet test tests\Filomena.Core.Tests\Filomena.Core.Tests.csproj --no-restore -c Release
65 tests passed, 0 warnings.
```

`git diff --check` was run on the staged round-1 diff with no whitespace errors. No runtime,
settings, identity, Gateway, model, send, Graphiti, push, or `DREAMS.md` files were included.

## Reviewer fix round 2

Base commit: `66f1ba4bcef3c7d2c32ca8412361373cb9df0634`.

The remaining strict-schema defect was fixed: a nullable enum schema must include JSON `null`
in its `enum` values. `ParseNullableEnumArray` now emits the safe
`schema.nullable_enum_invalid` issue at `/enum` when `type` includes `null` but `enum` does not.

TDD evidence:

- The new negative test first failed in an isolated harness with `Assert.Throws` reporting that
  no exception was thrown.
- After the guard, the isolated Task 1-only harness passed all 32 Flow tests with 0 warnings.

Commands/results:

```text
dotnet test C:\Users\Helena\flow-task1-round2-test\FlowTask1Round2.csproj --no-restore -c Release --filter FullyQualifiedName~Filomena.Core.Tests.Flows
32 tests passed, 0 warnings.
```

The repository’s normal focused/full Core commands are currently blocked before test execution by
untracked pending Task 2 files (`FlowValidatorTests.cs` references missing `FlowValidator` and
`NodeCatalog`; `FlowBindingValidator.cs` has an incomplete tuple contract). Those files were not
edited, removed, or silently excluded from the repository. No full suite claim is made for this
round. Staged `git diff --check` is clean.
