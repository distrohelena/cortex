# WhatsApp Business multi-conversation design

## Scope

Filomena uses one linked WhatsApp account but treats each native WhatsApp chat as an isolated conversation. A chat may be a group (`@g.us`) or a direct message. The linked account identity and display names are metadata only; neither participates in the Graphiti namespace.

## Conversation identity

The shared resolver produces:

```text
channel = whatsapp
conversation_jid = normalized native provider JID
conversation_kind = group | dm
namespace = whatsapp-<kind>-<normalized conversation JID>
```

The existing group JID `5511981042361-1589823314@g.us` remains `whatsapp-group-5511981042361-1589823314-g-us`. Direct-message device qualifiers are removed and `@c.us` is normalized to `@s.whatsapp.net`; unresolved LIDs remain LIDs and are never guessed from display names.

All persistent paths use this resolver: inbound/outbound capture, semantic and exact recall, `memory_store`, authorization, and later importers. Runtime memory operations require a native conversation identifier; session keys remain only a compatibility fallback for explicit/offline callers.

Participant and Filomena UUIDs remain conversation-scoped UUIDv5 values. Consequently the same contact in a DM and a group has distinct actor nodes. The linked account number is deliberately absent from these UUID inputs, so relinking does not change an existing group namespace.

## Observe and respond policies

OpenClaw channel admission is the observation boundary. WhatsApp `message_received` is emitted before `before_dispatch`, but only for inbound messages admitted by `dmPolicy`, `groupPolicy`, and the group map. To observe all chats, the channel admits all DMs and groups and enables `pluginHooks.messageReceived`.

The Filomena guard is the independent response boundary:

- configured DMs may dispatch normally;
- configured groups retain leading `@Filomena` or verified reply-to-Filomena behavior;
- unknown DMs and groups are handled silently in `before_dispatch`, before model execution;
- `message_received` has already run, so suppression cannot prevent passive capture.

DM sessions use `per-account-channel-peer` to prevent OpenClaw transcript context from crossing peers. Graphiti isolation does not depend on this session setting.

## Capture and recall

Inbound capture remains one provider message to at most one deterministic episode, with raw text, provider ID, original timestamp, reply metadata, account ID, conversation JID/kind, sender ID, and display metadata.

Outbound capture remains post-send only. The canonical Filomena Agent is scoped to the conversation namespace. A DM peer may be bound as addressee from the native DM conversation JID only; display names are never used. Group addressees still require exact run-bound correlation. If structural identity is unavailable or conflicts with configured self identities, the outbound episode is preserved without addressee.

Semantic recall, exact neighborhood recall, and `memory_store` receive exactly the current conversation namespace. No global contact or cross-chat retrieval is introduced.

## Operational account migration

The current linked-device state lives under `~/.openclaw/credentials/whatsapp/default/`, principally `creds.json`, `creds.json.bak`, and Baileys key material. Relinking replaces that account session but does not modify FalkorDB or Graphiti namespaces. Before relinking, copy the entire `default` directory to a dated sibling backup and preserve `openclaw.json`.

OpenClaw 2026.8.1 uses the same Baileys linked-device QR flow for a WhatsApp Business app account; there is no separate Business channel implementation.

