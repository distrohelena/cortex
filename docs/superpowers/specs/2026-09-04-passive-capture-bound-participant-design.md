# Passive Capture and Bound Participant Design

## Scope

Replace the Graphiti plugin's `agent_end` batch capture with one inbound
`message_received` event per episode and add deterministic participant identity
binding. This phase does not migrate or merge legacy Person nodes.

## Inbound episode identity

The OpenClaw plugin resolves the canonical conversation namespace from the
trusted channel and conversation JID. It accepts only inbound events with a
provider message ID, conversation JID, and sender identity.

Episode UUIDs use UUIDv5 with a fixed, versioned namespace and the unambiguous
key:

```text
channel + "\0" + canonicalConversationJid + "\0" + providerMessageId
```

Each episode stores one original provider message as structured JSON. It
preserves the original text, timestamp, conversation JID, normalized sender
identity, observed display name, provider message ID, and available reply
metadata. The original timestamp becomes Graphiti `reference_time`.

The MCP queue checks the deterministic episode UUID inside the conversation's
serialized queue. Existing UUIDs are acknowledged without reprocessing. This
makes redelivery and concurrent retries idempotent without a sliding window.

## Sender normalization

The plugin prefers provider-supplied canonical sender fields. It strips only
documented device qualification from WhatsApp JIDs while preserving the JID
domain. It does not infer identity from display name or merge PN and LID
identities unless OpenClaw/provider metadata supplies a deterministic canonical
mapping. Unknown representations remain distinct and are logged.

## Generic bound entity

The vendored Graphiti extension accepts a provider-neutral `BoundEntity`:

- canonical UUID;
- canonical name;
- entity type and labels;
- deterministic attributes;
- aliases and current display metadata;
- an explicit speaker role.

The plugin translates WhatsApp sender metadata into this structure. Participant
UUIDs use a second fixed, versioned UUIDv5 namespace with the key:

```text
canonicalGroupId + "\0" + "whatsapp:" + normalizedSenderId
```

The graph is the persistent registry: the canonical entity is merged by UUID.
Display name updates are idempotent, and aliases are maintained as a sorted,
set-like union. The canonical name and UUID never depend on display name.

## Speaker binding invariant

Binding applies only to the episode speaker. Mentioned people remain ordinary
extracted entities. Graphiti receives the bound speaker separately from episode
text and inserts it into the resolved entity set. A dedicated speaker marker in
the extraction representation identifies the speaker candidate; display-name
matching is never used to identify the speaker.

Before any entity fact is persisted, candidate UUID mappings and every edge
endpoint are normalized to the canonical bound UUID. The pipeline validates
that:

1. the bound node has the requested UUID, group, canonical name, and type;
2. no speaker candidate remains mapped to another Person UUID;
3. no edge identified as speaker-scoped points to another Person UUID.

Violation fails closed. The raw episodic node is saved for provenance while
ambiguous entity nodes and edges are not persisted, and the error is logged
with the episode UUID and violated invariant.

## Context

One provider message remains one episode. Graphiti's existing same-group,
reference-time-aware previous-episode lookup supplies extraction context. No
prior message content is copied into the current episode and no sliding-window
capture is retained.

## Concurrency

The MCP queue serializes work per canonical group. Episode existence is checked
inside that queue. Bound participants use deterministic UUIDs and database
MERGE semantics; aliases are recomputed as a set-like union on every update.
Concurrent messages and retries therefore converge on one participant.

## Verification

Unit tests cover event mapping, sender normalization, UUID stability, reply
metadata, hook registration, and removal of `agent_end`. Core tests cover bound
entity normalization and fail-closed behavior. FalkorDB integration tests query
the resulting graph for episode idempotency, participant cardinality, aliases,
homonyms, speaker/mentioned-person separation, edge endpoints, concurrency, and
restart/reprocessing stability.

A separate read-only audit reports legacy Person counts, name matches against
canonical participants, and edge/fact counts. It performs no writes or merges.
