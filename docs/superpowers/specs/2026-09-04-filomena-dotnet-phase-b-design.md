# Filomena .NET Phase B Design

## Scope

Phase B turns the milestone-A observer into a conversation scheduler and turn
arbiter. It still never sends a channel message, never invokes the full
Filomena generation prompt, and does not move Graphiti orchestration into C#.
The existing OpenClaw passive-memory hook remains the sole inbound Graphiti
writer during this phase.

## Runtime flow

```text
OpenClaw events_wait
  -> normalized InboundConversationEvent
  -> ConversationTurnManager
  -> isolated per-conversation queue
  -> quiet-window/max-burst aggregation
  -> recent-context projection
  -> Gemma turn arbiter
  -> logged Ignore | Wait | Respond decision
```

No Phase-B component calls `messages_send`.

## Conversation isolation and concurrency

The manager owns one single-reader queue and one processor task per canonical
conversation namespace. There is no global scheduling lock. Messages arriving
while an arbitration call is in flight remain queued for the next burst and
are never appended retroactively to the burst being evaluated.

Pending state is intentionally ephemeral. A restart discards an unevaluated
burst; the OpenClaw MCP cursor/recovery behavior remains responsible for
transport recovery. Provider-message deduplication stays in the transport.

## Burst policy

Timing is selected from typed configuration:

| Context | Quiet window | Maximum burst |
| --- | ---: | ---: |
| Allowlisted DM | 1.8 seconds | 6 seconds |
| Directly addressed group burst | 1.5 seconds | 8 seconds |
| Normal group burst | 3.5 seconds | 10 seconds |

A direct address is a deterministic input signal, not a final response rule.
Initially it consists of a structural reply marker when available or a
configured textual Filomena alias. Timing never participates in persistent
identity or provenance.

The arbiter receives at most 16 messages, 3,000 characters, and 10 minutes of
recent context. Speakers and reply references are preserved when structurally
available.

## Arbiter contract

The core contract is a typed `TurnDecision` with `Respond`, `Wait`, or `Ignore`,
an optional anchor provider-message ID, an optional target actor stable ID, and
a bounded reason enum. The LM Studio implementation calls the already loaded
`google/gemma-4-e4b` through its OpenAI-compatible chat-completions endpoint at
low temperature and requests one compact JSON object.

Output is accepted only when enum values are known and anchor/target values
refer to actors/messages present in the supplied context. Invalid output gets
one retry. If both attempts fail, the arbiter fails closed to `Ignore`, except
for an allowlisted DM with a deterministic clear reply expectation, which
falls back to `Respond`.

`Wait` keeps the evaluated burst pending for one configurable reevaluation.
New messages join that pending burst before reevaluation. A second `Wait`
finalizes as `Ignore`, preventing an immortal pending turn.

## Application hosting

`Probe:Mode=phase-b` selects a dedicated Generic Host worker. It maintains one
long-lived OpenClaw MCP connection, feeds events into the manager, and logs
structured decisions. Existing milestone-A inspect/list/wait modes remain
available and unchanged. LM Studio endpoint, model, aliases, timing, limits,
and allowlisted DM native IDs are typed configuration.

## Failure behavior

- Missing conversation identity: skip the event and log a warning.
- LM Studio timeout/network failure: retry once, then apply the conservative
  fallback.
- Invalid JSON or references: retry once, then apply the fallback.
- MCP disconnect: reconnect using the existing worker pattern while retaining
  the in-memory provider-message registry for the process lifetime.
- Shutdown: cancel the listener and all per-conversation processors cleanly.

## Phase boundary

Phase C will consume `Respond` decisions, build the full Filomena prompt, and
call OpenClaw `messages_send`. Phase B only proves scheduling and arbitration.

