# Filomena Flow contracts (milestone A)

This document describes the static graph and portable-release contract used by
the milestone-A compiler. It is a file-format and validation contract, not an
executor specification. The delivery does not run graphs, call a model
provider, publish to a transport, persist memory, or activate a release.

## Portable artifact

`FlowCompiler.Export` writes one JSON object with exactly these top-level
fields:

```json
{
  "artifactFormatVersion": 1,
  "catalogVersion": 1,
  "releaseId": "release-id",
  "environment": "testnet",
  "workspace": {
    "characters": [],
    "graphs": [],
    "modelProfiles": [],
    "modelProfileReferences": {}
  }
}
```

`workspace.graphs[]` uses the graph-document format: `id`,
`formatVersion`, `role` (`ingress` or `character`), optional `characterId`,
`entryNodeIds`, `nodes`, `dataEdges`, and `controlEdges`. Every node has
`id`, `kind`, `blockVersion`, and object `config`. Data and control edges are
separate arrays. A data edge has `source`, `fieldPath`, and `target`; a control
edge has `source` and `target`; each port reference has `nodeId` and `port`.

The examples in [docs/examples/flows](examples/flows) are literal,
hand-authored portable artifacts. They use only fake destinations and do not
contain an example-only binding envelope. `FlowCompiler.Import` is called with
the artifact string, a complete local environment registry, and an explicit
target environment. Environment permissions, route owners, destination
bindings, physical stores, and grants are deliberately outside the artifact.
Credential references are opaque handles, not copied credentials.

## Schema and data rules

The supported type subset is `string`, `number`, `integer`, `boolean`, `enum`,
`object`, `array`, and `nullable`. Objects are closed; portable object fields
use `{ "type": <type>, "required": <boolean> }`. Structured AI output uses
this portable type form in `config.outputSchema` and exposes one `output` data
port. Text AI output exposes `text`. A data edge selects only its declared
`fieldPath`; the compiler does not inject the rest of an object or an implicit
execution context into a target node.

Required inputs and control exits must be explicitly connected. The structured
example demonstrates an object containing `texto`, `humor`, and `reacao`, but
only `output.texto` is explicitly connected to `send.body`. `humor` and
`reacao` have no automatic effects, reactions, writes, or transport behavior.

## Node catalog

Catalog version 1 currently accepts the following block kinds, all at
`blockVersion: 1`:

| Kind | Static effect |
| --- | --- |
| `dm-input`, `group-input`, `manual-input` | Pure input |
| `character-dispatch` | Pure character boundary |
| `context`, `burst`, `condition`, `parallel`, `join` | Pure data/control |
| `ai` | Model request description |
| `await` | Await description |
| `history-record`, `memory-learn`, `state-update` | Write description |
| `history-read`, `memory-read`, `state-read` | Read description |
| `protector` | Pure protection boundary description |
| `send` | Send description |

The catalog is trusted compiler metadata. A draft cannot change a node's
effect or grant itself a capability. Provider-specific execution code is not
part of this catalog.

## Issue codes

Validation returns machine-readable `FlowIssue` values with `code`, JSON
Pointer-like `path`, `message`, and `severity`. The principal code families
are:

- Document parsing: `document.json_malformed`,
  `document.property_duplicate`, `document.field_required`,
  `document.field_unknown`, `document.version_unsupported`,
  `document.role_unsupported`, `document.nodes_limit`,
  `document.edges_limit`, `document.input_too_large`,
  `document.json_depth_exceeded`, and `node.prompt_too_large`.
- Graph/catalog checks: `node.kind_unsupported`,
  `node.block_version_unsupported`, `node.input_missing`,
  `node.control_exit_missing`, `node.control_input_missing`,
  `edge.source_node_missing`, `edge.target_node_missing`,
  `edge.source_port_missing`, `edge.target_port_missing`,
  `edge.target_producer_duplicate`, `edge.type_incompatible`,
  `data.value_unavailable`, `graph.entry_missing`,
  `graph.entry_ambiguous`, `graph.entry_node_missing`,
  `graph.control_cycle`, `graph.data_cycle`, and
  `graph.dependency_cycle`.
- Schema checks: `schema.unsupported`, `schema.depth_exceeded`,
  `schema.fields_limit`, `schema.property_duplicate`, and
  `schema.nullable_enum_invalid`.
- Artifact checks: `artifact.root_invalid`, `artifact.version_unsupported`,
  `artifact.catalog_unsupported`, `artifact.environment_invalid`,
  `artifact.field_required`, `artifact.field_invalid`,
  `artifact.field_unknown`, and `artifact.property_duplicate`.
- Binding checks: `binding.environment_missing`,
  `binding.environment_duplicate`, `binding.character_missing`,
  `binding.graph_missing`, `binding.model_profile_missing`,
  `binding.destination_required`, `binding.destination_unknown`,
  `binding.capability_missing`, `binding.space_required`,
  `binding.space_unknown`, `binding.direct_grant_required`,
  `binding.mediated_grant_required`, `binding.route_ambiguous`,
  `binding.cross_environment_alias`, `binding.private_physical_alias`, and
  `binding.private_conversation_alias`.

Warnings are retained in the result; any error prevents a compiled release.
Import parses and revalidates the workspace instead of treating portable JSON
as trusted executable instructions.

## Limits and execution boundary

The static reader/compiler enforces these current limits: 1 MiB UTF-8 input,
JSON depth 64, schema depth 16, 256 schema fields, 256 nodes, 1024 total
edges, and 32 KiB per prompt. It also checks closed schemas, type
compatibility, required ports, acyclic dependencies, graph/character/profile
references, destination IDs, environment shape, capabilities, route
ownership, grants, and physical-store alias rules.

Runtime step budgets, fan-out budgets, model timeouts/retries, transport
idempotency, persistence, authentication, and per-operation authorization are
not implemented here. A passing compile is not a production authorization
decision: a future runtime must recheck permissions and capabilities at every
sensitive boundary before executing any effect.

## Draft, release, and environment

- **Draft:** a `FlowWorkspaceDefinition` is the authoring package of
  characters, graphs, model profiles, and structural profile references. It is
  not executable and may be edited before compilation.
- **Release:** `FlowCompiler.Compile` validates the draft, assigns a caller
  supplied `releaseId`, freezes graph/config/profile data, and produces a
  deterministic compiled representation. `Export` serializes that portable
  representation. There is no activation or deployment operation in this
  milestone.
- **Environment:** compilation and import require the complete registry for
  both `testnet` and `mainnet`, then select one explicit target environment.
  Import may rebind the same artifact to a different target registry; the
  artifact's `environment` records its source artifact environment and does
  not supply target permissions or bindings.

The test-local helper in `FlowExamplesTests` builds fake bindings separately:
fake route ownership, `model`/`send` capabilities, fake destinations, and (for
the two-character example) one private space per character with one explicit
conversation and a distinct physical-store key. Those bindings are test setup,
not portable artifact content.

## Examples

- [minimal-text.json](examples/flows/minimal-text.json) is an input → text AI
  → fake send pipeline.
- [structured-response.json](examples/flows/structured-response.json) is an
  input → structured AI → `texto` field → fake send pipeline; `humor` and
  `reacao` remain unconnected data.
- [two-characters.json](examples/flows/two-characters.json) uses an ingress
  `parallel` fan-out to two independent `character-dispatch` boundaries and
  includes separate character graphs. The local test bindings keep each
  character's private character+conversation space distinct.
