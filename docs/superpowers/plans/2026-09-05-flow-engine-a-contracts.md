# Flow Engine A — Contracts and Compiler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development task-by-task. Track steps with checkboxes. If using subagents, use gpt-5.6-luna with high reasoning explicitly; do not retry a known exhausted quota or silently switch models.

**Goal:** Build a provider-neutral, offline compiler that validates versioned multi-character flow definitions and produces immutable environment-bound releases without executing them.

**Architecture:** Add a focused Flows area to the existing Filomena.Core assembly. Separate document parsing, type/graph validation, binding authorization checks and immutable compilation output. The current ConversationTurnManager and all production wiring remain unchanged.

**Tech Stack:** Existing .NET 10 SDK, C#, System.Text.Json and existing xUnit project. No new dependency or service is needed for this milestone.

**Spec:** docs/superpowers/specs/2026-09-05-filomena-flow-engine-design.md, milestone A.

## Global Constraints

- Não existe distribuição global implícita, último remetente global ou polling por personagem.
- Uma aresta passa a saída selecionada, não o contexto inteiro da execução.
- Draft é editável e não afeta produção.
- Testnet/mainnet rodam simultaneamente.
- Não reimplementar Baileys, Telegram ou Discord.
- Não fazer merge legado automático por nome.
- Do not start/stop/relink Gateway, call Gemma, send messages or write Graphiti.
- Do not modify workspace/DREAMS.md, existing UUID functions or production settings.
- No push. Preserve the dirty main worktree and any preexisting incomplete test files.

## Scope limits and baseline

This delivery is a library plus tests/example documents. It is NOT the durable
executor, UI, deployed release repository, model client, memory implementation,
permission enforcement at runtime or production migration. Publish here means
compile/freeze an artifact; switching an active deployment belongs to later work.

Two incomplete tests from the canceled dashboard implementation currently reference
missing App types. Run the Core baseline independently; attempt the solution suite
and report the existing failures separately. Do not delete those tests to obtain green
results. Resolve their disposition explicitly in the delivery that owns that work.

Execution preparation:

- [ ] Read spec and `git status --short`; record current HEAD and existing changes.
- [ ] Run `dotnet test tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj -c Release --no-restore`.
- [ ] Attempt `dotnet test Filomena.slnx -c Release --no-restore` once; record unrelated failures with exact paths.
- [ ] Preserve the user's main preference and current functional dependencies; do not reset/create a clean branch losing dirty changes.

## File map

Create these files; do not reorganize unrelated source:

| Files under src/Filomena.Core/Flows/ | Responsibility |
| --- | --- |
| FlowDocuments.cs | Graph, node, data/control edges, characters and profiles |
| FlowTypes.cs | Versioned portable type algebra and schema conversion |
| FlowDocumentReader.cs | Strict bounded JSON parsing and document version rejection |
| NodeCatalog.cs | Known node versions, ports, required config and capability metadata |
| FlowIssues.cs | Stable error codes and safe node/port paths |
| FlowValidator.cs | Local structural/type/DAG validation |
| FlowBindings.cs | Environment, named space and permission/capability descriptors |
| FlowBindingValidator.cs | Cross-graph/character/environment binding validation |
| FlowCompiler.cs | Validate then freeze immutable compiled release |

Tests: create `tests/Filomena.Core.Tests/Flows/` with corresponding *Tests.cs and
FlowTestFixtures.cs. Create reviewable JSON examples under `docs/examples/flows/`.

## Boundary definitions

Introduce only structural IDs here; never derive persistent identity from names.
Use opaque IDs with ordinal comparison. Environment is `testnet` or `mainnet` in v0.
The document format is version 1; block definitions each have their own version.

```csharp
public enum FlowEnvironment { Testnet, Mainnet }
public enum GraphRole { Ingress, Character }
public enum JoinMode { All, FirstValid, Independent }
public enum FlowEffect { Pure, Model, ReadMemory, WriteMemory, WriteState, Send, Await }
public enum FlowIssueSeverity { Error, Warning }
public sealed record FlowIssue(string Code, string Path, string Message,
    FlowIssueSeverity Severity = FlowIssueSeverity.Error);
public sealed record PortRef(string NodeId, string Port);
public sealed record DataEdge(PortRef Source, string[] FieldPath, PortRef Target);
public sealed record ControlEdge(PortRef Source, PortRef Target);
```

DataEdge does NOT imply control execution and ControlEdge does NOT copy values.
No arbitrary expression language. FieldPath selects object fields only in A; arrays
can pass whole but are not implicitly iterated. Nullable/optional source fields cannot
silently satisfy a required nonnullable target.

### Task 1: Portable documents, types and strict parsing

**Files:** FlowDocuments.cs, FlowTypes.cs, FlowDocumentReader.cs, FlowIssues.cs;
tests/Filomena.Core.Tests/Flows/FlowDocumentReaderTests.cs and FlowTypeTests.cs.

**Produces:** GraphDocument with Id, FormatVersion, Role, optional CharacterId,
Nodes, DataEdges, ControlEdges; CharacterDefinition with stable Id/display name/
personality/graph ID; ModelProfileDefinition with profile ID (credential reference,
never secret value); FlowReadResult(Document?, Issues); FlowDocumentReader.Read(string).

Type algebra: string, finite number, integer, boolean, enum of strings, object,
array; required/optional object fields and nullable types. Objects have closed fields.
Limits: 1 MiB input UTF-8, 64 JSON parse depth, 16 schema depth, 256 fields per schema,
256 nodes, 1024 total edges, 32 KiB per prompt. Keep limits named and unit-tested.

- [ ] Write tests for malformed JSON, duplicate JSON properties, unknown format version,
  unknown fields/kinds, depth/size overflow, nullable object fields and UTF-8 byte limits.

```csharp
var result = FlowDocumentReader.Read("{\"formatVersion\":99}");
Assert.Null(result.Document);
Assert.Contains(result.Issues, x => x.Code == "document.version_unsupported");
```

- [ ] Run focused tests to observe failure. Implement reader using bounded STJ parsing,
  reject duplicate properties before deserialization, return safe issue paths without
  echoing prompts/secret-like content. Clone JsonElements or own their lifetime.
- [ ] Implement type-to-JSON-schema conversion for this supported subset. Reject
  remote `$ref`, unknown schema keywords and unsupported constructs, rather than
  accepting unvalidated schemas. No general schema package/parser in A.
- [ ] Test directional type compatibility: integer -> number allowed; number -> integer
  rejected; enum subset -> superset allowed; optional -> required rejected; nested
  fields and arrays checked recursively. Run tests and review scoped diff.

### Task 2: Node catalog and typed graph validation

**Files:** NodeCatalog.cs, FlowValidator.cs; tests/Filomena.Core.Tests/Flows/FlowValidatorTests.cs
and FlowTestFixtures.cs.

**Consumes:** Task 1 documents/type algebra. **Produces:**
`FlowValidator.Validate(GraphDocument, NodeCatalog) -> IReadOnlyList<FlowIssue>`.
Catalog lookup is by kind + blockVersion, not a client-supplied set of capabilities.

Catalog v1 covers dm-input, group-input, manual-input, character-dispatch, context,
burst, ai, condition, parallel, join, await, history-record/history-read,
memory-learn/memory-read, state-read/state-update, protector and send.

- [ ] Create a literal minimal fixture (manual-input -> ai -> send), with data edges
  text -> prompt input and AI text -> send body, plus explicit control success edges.
  The fixture is test-only and builds declared document records, not compiler output.
- [ ] Write tests that mutate that fixture for duplicate node IDs, missing node/port,
  unknown block version, missing required data input, wrong field type, duplicate
  producer, missing decision exit, unsupported freeform code node and control cycles.

```csharp
var graph = FlowTestFixtures.WithUnknownTargetPort();
Assert.Contains(FlowValidator.Validate(graph, NodeCatalog.V1),
    issue => issue.Code == "edge.target_port_missing");
```

- [ ] Implement catalog definitions using immutable records. AI defaults to text output;
  structured output requires a supported schema. Condition compares a selected typed
  field against a literal; never interprets raw model text as code.
- [ ] Validate data/control edges separately, required inputs, type assignability and
  data availability along every reachable control path. A branch-local value cannot
  feed an unrelated branch. An explicit join exposes named result ports.
- [ ] Reject unreachable nodes as errors for A, cyclic control/data dependencies and
  multiple implicit starts. Allow a declared input-set entry for DM/group alternatives,
  with validated event union; do not require copying the character graph per event kind.
- [ ] Await has explicit resume predicate/deadline/timeout exit. Validate its bounded
  continuation contract; do not implement timers. Join requires named branches and a
  mode. First-valid before irreversible effects produces a warning, not an exactly-once
  claim. Issues distinguish warnings/errors, and warnings don't silently disappear.
- [ ] Run focused tests; deterministic issue ordering by path/code enables reliable UI.

### Task 3: Character, environment and storage-binding checks

**Files:** FlowBindings.cs, FlowBindingValidator.cs;
tests/Filomena.Core.Tests/Flows/FlowBindingValidatorTests.cs.

**Produces:** FlowWorkspaceDefinition (characters, graphs, model-profile references),
EnvironmentBindings (environment, route owners, space bindings, grants, capabilities),
and `FlowBindingValidator.Validate(workspace, bindings) -> IReadOnlyList<FlowIssue>`.
SpaceBinding includes logicalSpaceId, environment, scope, ownerCharacterId when private,
explicit conversation IDs when shared, and opaque physicalStoreKey. Permission records
separate read/write and direct/mediated; destinations have structural identifiers.

- [ ] Write literal two-character/one-account fixtures. Test same names/different IDs,
  missing character/model ref, recursive character calls, direct private access without
  grant, mediated access without protector, unknown destination and environment mismatch.

```csharp
var (workspace, bindings) = FlowTestFixtures.CrossEnvironmentPhysicalAlias();
Assert.Contains(FlowBindingValidator.Validate(workspace, bindings),
    issue => issue.Code == "binding.cross_environment_alias");
```

- [ ] Validate runtime capability requirements from the trusted catalog against supplied
  environment capabilities. A send node without an explicit destination binding cannot
  publish. Invocation cannot grant the caller another character's authority.
- [ ] Enforce a unique route owner per account/conversation in the supplied complete
  environment registry; reject ambiguous overlap. Explicit duplication requires separate
  marked routes and grants, not a wildcard accidental overlap.
- [ ] Reject two environments resolving to the same physical store key. For private
  spaces reject cross-character physical aliases; intentional shared spaces resolve via
  the same declared logical shared space, not independent private aliases.
- [ ] Treat legacy bindings as opaque explicitly assigned references. Do not add new
  environment prefixes to existing FilomenaIdentity functions. New identity/namespace
  codecs are NOT selected here: no Graphiti node is created in A.
- [ ] Tests prove renaming a character does not change its ID, profile model names do
  not act as identity, and destination checks do not consult display names. Runtime
  revocation/label propagation is delivery D/B; static validation is not that guarantee.

### Task 4: Immutable releases and compilation

**Files:** FlowCompiler.cs; tests/Filomena.Core.Tests/Flows/FlowCompilerTests.cs.

**Consumes:** Reader, catalog and validators. **Produces:**

```csharp
public sealed record FlowCompilationResult(
    CompiledFlowRelease? Release, IReadOnlyList<FlowIssue> Issues);
// Compile(workspace, completeEnvironmentRegistry, targetEnvironment, releaseId)
// -> FlowCompilationResult. No file I/O, deployment side effects or active pointer.
```

CompiledFlowRelease is privately constructed; exposes immutable environment,
release ID, document/block versions, frozen graphs/config/profile references and
ordered node plans. Never retain caller-owned mutable dictionaries/arrays.

- [ ] Test invalid workspace returns no release, draft mutations cannot change a release,
  same draft publishes separately to testnet/mainnet, and later compilation doesn't
  modify earlier release output. No API here has side effects or makes provider calls.

```csharp
var draft = FlowTestFixtures.ValidWorkspace();
var compiled = FlowCompiler.Compile(draft, registry, FlowEnvironment.Testnet, "release-1");
Assert.NotNull(compiled.Release);
Assert.Equal(FlowEnvironment.Testnet, compiled.Release.Environment);
```

- [ ] Validate all referenced graphs/profile IDs as a closed package before freezing;
  detect character-call recursion across graphs, not only cycles inside one graph.
  Produce deterministic topological ordering with ordinal node-ID tie breaks.
- [ ] Provide portable release export/import with format/catalog compatibility checks;
  imported artifacts are revalidated, never treated as trusted executable commands.
  References to credentials remain handles. A does not copy data/ACL/credentials when
  compiling for another environment; target bindings must be supplied explicitly.
- [ ] Run roundtrip tests and mutation tests for nested schemas, node configs and
  character definitions. If release digests are added, use a defined canonical encoding
  and SHA-256; do not hash arbitrary dictionary serialization order.

### Task 5: Examples, regression and delivery evidence

**Files:** docs/examples/flows/{minimal-text,structured-response,two-characters}.json;
docs/filomena-flow-contracts.md; tests/Filomena.Core.Tests/Flows/FlowExamplesTests.cs.

- [ ] Create literal examples in the supported format: minimal text pipeline; response
  object with texto/humor/reacao, only texto explicitly wired to send; ingress fan-out
  to two character graphs and distinct private spaces. All use fake destinations.
- [ ] Parse/compile each example in tests. Change an input-only fixture field and prove
  it is not added to another node's bound inputs without an explicit data edge.
- [ ] Document schema subset, node kinds/versions, issue codes, static-vs-runtime limits,
  draft/release/env differences and the fact that this delivery does not execute graphs.
- [ ] Run Core tests, existing identity golden tests, full solution attempt, Core Release
  build and `git diff --check`. Report known dashboard test compile blockers honestly;
  do not claim full solution green if incomplete unrelated tests still prevent it.
- [ ] Review compiler boundaries for provider-specific code, silent coercions, mutable
  release state, wildcard permission grants and unused implicit context propagation.
- [ ] Commit only new Flows files/tests/examples/docs after scoped verification. No
  modifications to runtime activation or production config; no push.

## Handoff and coverage

Tasks 1–2 provide graph/schema contracts, 3 environment/character isolation, 4 immutable
releases, 5 executable fixtures and legacy compatibility evidence. These cover milestone
A only. Durable SQLite/runtime, model execution, memory ACL enforcement, editor, actual
transport and Filomena equivalence stay in B–F. A passing compiler is not a production
authorization system until the runtime rechecks permissions at every sensitive boundary.

Suggested execution: in-session after plan handoff while delegated quota is unavailable.
If subagents are available again and used, preserve Luna/high and disjoint ownership;
no independent review session without asking Helena.
