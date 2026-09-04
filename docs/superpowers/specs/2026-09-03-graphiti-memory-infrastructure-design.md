# Graphiti memory infrastructure design

## Scope

This change fixes FalkorDB persistence and conversation isolation before any
personality, ontology, reranking, or model changes. The active deployment is
versioned under `C:\dev\filomena\runtime`; the former
`C:\Users\Helena\.openclaw\workspace\graphiti-runtime` path becomes a junction
to that directory.

## Persistence

The current FalkorDB container stores its live `dump.rdb` under
`/var/lib/falkordb/data`, while its Docker volume is mounted at `/data`. Before
replacing the container, force and verify an RDB background save, copy the dump
to a timestamped backup outside Git, and seed the `filomena_falkordb_data`
volume. The canonical compose mounts that volume at
`/var/lib/falkordb/data`.

Verification records an existing episode UUID and content before migration,
recreates the FalkorDB container, and queries the same episode afterward. A
second forced recreation proves that the corrected volume survives container
replacement. Legacy graphs and the backup dump are retained.

## Canonical conversation namespace

A shared pure helper derives persistent Graphiti group IDs from the channel and
native conversation ID. For the configured WhatsApp group, the result is:

```text
whatsapp-group-5511981042361-1589823314-g-us
```

The algorithm lowercases both inputs, classifies WhatsApp JIDs ending in
`@g.us` as groups and other JIDs as direct conversations, replaces characters
outside Graphiti's safe identifier set with hyphens, collapses repeated
hyphens, and prefixes the result with channel and conversation kind. The native
JID is preferred; the canonical OpenClaw session key is only a fallback.

The same helper is imported by the OpenClaw plugin and WhatsApp importer. The
importer requires either `--jid` or an explicit `--group-id`; filenames never
determine persistent identity.

## Context-bound tools and recall

Memory tools are registered through OpenClaw tool factories so every invocation
is bound to trusted `messageChannel`, `nativeChannelId`, `sessionKey`, and
`sessionId` values. No module-global current-session variable is used.

Persistent recall searches exactly one group: the canonical namespace of the
current conversation. Optional ephemeral recall searches only the current
`session-<sessionId>` group. SpiceDB permission checks are targeted at those
group IDs; the runtime path does not enumerate every group authorized for
`agent:main`.

`memory_store` writes long-term memory to the canonical current-conversation
group and session memory to the current ephemeral group. It fails closed if a
trusted conversation context is unavailable. It never falls back to the former
global `whatsapp` group. Automatic prompt recall and the existing transitional
auto-capture follow the same namespace resolution.

The startup service no longer creates or warms a global default memory group.
Administrative CLI operations remain explicitly group-scoped.

## Legacy graph handling

The imported `wa-chat` graph is copied to the canonical group with
`GRAPH.COPY`; node and relationship `group_id` properties are changed only in
the copy. The original graph remains untouched as a rollback source. The old
session graph also remains intact but is removed from persistent recall. Its
nine overlapping auto-capture episodes are not re-extracted in this round.

## Passive capture design

OpenClaw 2026.8.1 emits `message_received` once for an accepted inbound turn,
before `before_dispatch`, and explicitly independently of reply dispatch. It is
therefore the correct hook for observing messages rejected by
`whatsapp-filomena-guard`.

The next minimal capture implementation should write one episode per inbound
message, containing the available sender name and ID, original timestamp,
message ID, reply metadata, and content. Its episode name and UUID should be
deterministically derived from `channel + conversation JID + message ID`, which
provides stable deduplication across retries and restarts. It replaces, rather
than supplements, the overlapping `agent_end` batches. This round validates the
hook and payload mapping but does not activate broad passive ingestion.

## Verification

Automated tests cover deterministic namespace derivation, explicit importer
identity, context-bound `memory_store`, current-group-only auto and explicit
recall, and an A/B isolation case. Live checks cover RDB survival, preservation
of a pre-existing episode, canonical graph availability, and import targeting.
