# Typed-flow slice 3 — explicit read-only memory

> **For agentic workers:** Use TDD and verification-before-completion. Use subagents only after user approval. No worker commits or overlapping edits; integrator owns final selective commit and main integration.

**Goal:** A wired, executed memory node provides attributed, bounded persistent facts to typed Instructions, never implicit recall.

**Architecture:** Extend the existing Core type algebra and session runner. `IMemoryReader` is a Core boundary; a new `Filomena.Memory` assembly adapts the existing Graphiti MCP HTTP tools using installed official ModelContextProtocol 2.2.0. Runtime validates identity and memory results independently of the adapter. Graphiti remains unchanged.

**Stack:** .NET 10 / xUnit / current JS editor / existing graph schema 3 and project schema 1.

**Spec:** User's approved 29-section slice-3 request in this conversation; this file records its concrete implementation contracts.

**Execution record:** Implemented inline without additional agents (approval was not received). See `docs/typed-flow-slice3.md` for the final contract and `docs/typed-flow-slice3-verification.md` for executed tests and the external Graphiti connection error. Offline Core 198, backend 43, JS 72 passed. Live canary remains unproven; no service was changed to force it through.

## Global constraints

- No writes to Graphiti, legacy migration, transport/Gateway operations, sending or personal configuration/DREAMS edits.
- Namespace comes from session channel/native identity, never node configuration or names. Reuse `FilomenaIdentity` constants/functions; WhatsApp namespace and UUID goldens stay unchanged.
- `Pessoa` retains the existing `nome`/`stableId` domain. Canonical UUID is derived deterministically from validated stable actor identity plus session namespace, not a display name. If optional runtime identity metadata is supplied, reject inconsistencies. LID is never guessed into PN.
- `Memória` visual fields: `conteudo: text`, `categoria: enum<limite,apelido,relacao,fato,nao_classificada>`. Internal subjects/provenance stay outside property picker and are emitted only through a safe explicit memory serializer.
- `activeOnly=true` in this V1; inactive/historical facts never enter the prompt. No psychological or preference/event classification.
- Errors fail the execution; successful zero results return `[]`. No fallback to history/text or global query.

## Inspected backend evidence

`vendor/graphiti/mcp_server/src/graphiti_mcp_server.py` exposes:

```text
get_entity_neighborhood(group_id, entity_uuid, counterpart_uuid?, active_only=true, limit=10)
search_memory_facts(query, group_ids?, max_facts=10, center_node_uuid?, active_only=true)
```

Exact limit is 1–50 and order is recency then UUID, not boundary priority. Request the maximum bounded candidate set (50) before local category priority/budget; do not claim access to every historical boundary beyond that API limit. Both results contain `facts`, endpoint UUID/labels/stable_id/canonical, group_id and temporal fields. MCP errors may be an `error` result even without protocol `isError`. Exact nonexistent entity currently returns an error, not an empty neighborhood; preserve that distinction.

`utils/social_retrieval.py` and `plugins/openclaw-memory-graphiti/social-recall.ts` define active filtering and canonical endpoints. Established address terms require non-generic term plus two distinct evidence run IDs (not chunks); reuse the existing structural policy, not LLM classification. Generic terms remain observations, not nicknames.

## Shared implementation contracts

Namespace `Filomena.Core.Memory`:

```csharp
public enum MemoryMode { Exact, Semantic, Hybrid }
public enum MemoryScope { Person, Conversation }
public enum MemoryCategory { Boundary, Nickname, Relationship, Fact, Unclassified }
public sealed record MemoryPerson(Guid ParticipantUuid, string ActorStableId, string? DisplayName);
public sealed record MemoryQuery(string ConversationNamespace, Guid AgentUuid,
    MemoryMode Mode, MemoryScope Scope, IReadOnlyList<MemoryPerson> Persons,
    string? Text, IReadOnlyList<MemoryCategory> Categories, int MaxItems, int MaxCharacters,
    bool ActiveOnly = true);
public sealed record MemoryItem(string Id, string ConversationNamespace, string Content,
    MemoryCategory Category, IReadOnlyList<MemoryPerson> Subjects,
    string SourceKind, DateTimeOffset? ValidAt = null, DateTimeOffset? InvalidAt = null,
    DateTimeOffset? ExpiredAt = null);
public interface IMemoryReader {
    Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken);
}
```

Add provenance fields only as required by concrete backend mapping, never prompt-visible by default. A shared Core result policy verifies same namespace and canonical attribution, filters inactive facts, dedupes structural IDs, sorts priorities then stable ID, applies whole-item character/count budgets. An oversized higher-priority item is not displaced by lower-priority semantic facts. No textual dedupe.

Node kind `memory`; stable inputs `persons`, `query`; output `memories`; execution `exec` → `then`. Settings:

```json
{"mode":"hybrid","scope":"person","categories":["limite","apelido","relacao","fato","nao_classificada"],"maxItems":8,"maxCharacters":1800,"activeOnly":true}
```

Optional `queryText` scalar configured locally OR a `query` wire, never both. Person input accepts actor / actor? / actor[] / actor?[] by inferring the connected source type and validating the domain, not by globally weakening optional/list compatibility. Remove nulls, dedupe canonical UUIDs in stable order. Exact/person requires at least one resolved person; semantic/conversation requires nonempty query. Hybrid/person requires person and optionally query. Hybrid/conversation requires query, with optional people for exact supplementation. Unsupported combinations are explicit executable errors while drafts remain saveable.

## Task 1 — Core type/runtime/identity

Files: `Core/Memory/*.cs`, `FlowDomain.cs`, `TypedFlowCompiler.cs`, `TypedFlowRunner.cs`, `Flows/Sessions/*.cs`; tests `Core.Tests/Flows/Memory*.cs` and fixture.

- [ ] Write red tests for memory projection, optional-person list, invalid identity, no implicit memory and session-isolated fake reader calls.
- [ ] Add Memory alias to existing type algebra and safe serialization: category/content plus `sobre` and ephemeral subject refs where needed; exclude all internal metadata. Shared references disambiguate identical names.
- [ ] Compile node contracts and modes; unknown/invalid ports remain errors. Await reader only at the node's execution pin; data wires do not execute it.
- [ ] Resolve `ConversationId`/namespace from session, canonical person UUID via existing schemes, validate read results. Inject optional reader into SessionFlowRuntime; absent reader fails only a reached memory node.
- [ ] Add MemoryQueryStarted/Completed trace metadata without query/content. Preserve state/batch/history semantics.
- [ ] Fake acceptance fixture combines batch, before-batch history, projected senders, hybrid memory and typed fake Instruction. Test attribution, priorities, UUID exclusion, errors/empty, isolation, and three separate JSON inputs.

## Task 2 — Graphiti read-only adapter

Files: new `src/Filomena.Memory/` project; tests in `Filomena.Llm.Tests/GraphitiMemory*.cs`. Adapter project references Core + ModelContextProtocol 2.2.0, no OpenClaw project. Main owns solution/App/test project registration.

- [ ] Red tests drive a fake tool boundary with actual backend-shaped JSON. Assert exactly one namespace in every call, no mutation tools.
- [ ] `IGraphitiReadClient` exposes neighborhood and semantic facts; MCP implementation is lazy/long-lived HTTP with official SDK, bounded timeout/cancellation, safe errors and structured/text-content JSON response support.
- [ ] `GraphitiMemoryReader : IMemoryReader` classifies only structural facts: boundary names, evidence-established nickname, canonical Agent↔Person relation, direct canonical fact; otherwise unclassified. Reject legacy/ambiguous endpoints. Validate canonical endpoint UUID from stable_id and namespace, not just the backend boolean flag.
- [ ] Person semantic facts must involve supplied canonical endpoints; conversation semantic results never become guessed person memory. Dedup/final budget via shared Core policy.
- [ ] Test invalid/expired, malformed backend/error, missing endpoint, duplicate IDs, same-name persons, boundary budget, nickname evidence and active_only arguments.
- [ ] Opt-in real read-only test for DRIFT42/PLEX73/PRIVATE91. No service startup, imports, seeding or graph mutation. Unavailable environment is reported, never a fake green live result.

## Task 3 — Editor/persistence

Files: existing `flow-contract.mjs`, `flow-model.mjs`, `flow.js`, `flow.html`, `flow-typed-panel.mjs`, `PlaygroundProjectStore.cs`; `frontend/flow-memory.test.mjs`.

- [ ] Red JS tests: Memory/Memory[] typing, property picker, enum values from memory.category, persons nullable list wiring, settings round-trip and invalid wire preservation.
- [ ] Compact node/panel, no UUID/namespace settings. Defaults 8/1800/all categories/active only. Named Instruction inputs accept memory and lists. No new generic library-specific Filomena logic.
- [ ] Save/load/import/export preserves graph3/project1 contracts; memory settings only, no runtime data. Instruction safe preview exposes connected inputs as separate properties.

## Task 4 — Integration / main

- [x] Integrate slice2 fast-forward into main; stash only overlapping user UI files, restore both sides and leave user changes unstaged. Preserve stash `8102d9ac415c2fe0bf1bad6078ae408035a8ff21` as recovery.
- [x] Main after integration: focused Core 54/54, backend 6/6 with separate artifacts path because user's App is running, JS 14/14. No Gateway/App stop.
- [ ] Register Memory assembly/dependencies and fake/real reader composition in playground (no personal config edits). Fake responses remain explicit; real Graphiti endpoint is opt-in, lazy, read-only.
- [ ] HTTP fixture tests verify safe memory payload, independent batch/history/memory inputs, failed vs empty and clean project roundtrip.
- [ ] Run complete focused scope, build, changed-file format, JS and opt-in integration; review all isolation and failure boundaries.
- [ ] Stage only slice3. Commit locally and fast-forward main with selective restoration of preexisting changes; no push. Record tests, actual canary status and deferred slice4 work.
