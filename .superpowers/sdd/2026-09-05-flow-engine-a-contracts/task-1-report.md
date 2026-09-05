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
