# Inbound Observation V1 — upstream investigation / blocked implementation

Date: 2026-09-11. Status: **NOT READY**. This is source evidence and a proposed
minimum contract, not a live capability attestation or a completed implementation.
No source adapter, journal, RPC, runtime behavior, installed plugin or configuration
was changed by this investigation. No listener, model, Graphiti query, agent or
outbound call was started.

## Finding that prevents claiming a reliable observation source

Native WhatsApp identity exists. The available public bridge boundary is not a
complete durable observation stream. Adding sequence numbers after its lossy hook
would detect gaps in the new journal, but **not messages lost before the journal**.
No capability may turn green on that basis.

Installed source roots inspected (code only, not live message data/configuration):

- `C:/Users/Helena/.openclaw/extensions/whatsapp/dist/`
- `C:/Users/Helena/AppData/Roaming/npm/node_modules/openclaw/dist/`

These bundled filenames/line numbers describe the inspected installation, not a
stable upstream API. Source existence does not establish active runtime behavior.

## Metadata inventory

| Field | Concrete upstream evidence | Stability / current exposure | Phase 0 |
|---|---|---|---|
| Native message ID | `monitor-CySzv38g.js`, `inspectWhatsAppIngressMessage`, around 307: requires `message.key.remoteJid` and `message.key.id` | Durable ingress rejects missing keys. Public normalized hook has optional `messageId`; later context construction has a random fallback when native ID is absent. Native provenance cannot be assumed from the hook field alone. | Critical |
| Durable native dedupe | Same file, `createWhatsAppDurableInboundMessageId`: SHA-256 of remote JID + newline + native ID; queue account-scoped | Native-key-based, not content/time. Queue completion retention is bounded (7 days / 5,000 completed entries in inspected monitor). This is not a permanent observation log. | Critical |
| Exact route | `msg.key.remoteJid`, account-scoped queue, lane derived from remote JID | Present before normalization. Public hook conversation is derived through `OriginatingTo/To/From`; cannot equate any such field with peer DM identity without binding. Existing adapter contains a DM normalization workaround. | Critical |
| Sender | `key.participant` for groups; `resolveInboundJid(remoteJid)` for DM, `senderE164` | Public hook exposes sender fields but is not a complete trusted identity bundle. No display-name fallback is acceptable. | Critical |
| `fromMe` | Native `msg.key.fromMe`; normalized WhatsApp message explicitly copies boolean | Not projected by `deriveInboundMessageHookContext` / public received-event contract / current HTTP bridge. Some recent outbound echoes are skipped upstream. | Critical |
| Self PN/LID | Normalized message copies `selfJid`, `selfLid`, `selfE164` around 1292 | Exists in socket/account context; not carried by current bridge. Resolver-backed aliases may bind one self actor; matching names or guessing a PN from LID may not. | Critical |
| Original timestamp | `messageTimestamp` parsed into milliseconds in native normalizer | Available when provider supplies it. Queue `receivedAt` is a different timestamp. Neither provides a safe cursor. | Preserve when present |
| Ordering | Queue is explicitly described as FIFO-ish; has `receivedAt`, lanes, claims | No durable read-only per-session observation sequence/cursor API found. A lane used by the dispatch worker is not a snapshot cursor. | Critical |
| Reply provenance | Native reply/context metadata; hook `replyToId`, sender/body/quote projections | Partial public projection. No need to solve group/thread semantics for this DM slice. | Deferred |
| Dispatch | `dispatch-from-config-D-R0cBMI.js:635` invokes `before_dispatch`; `handled:true` short-circuits that reply path | Existing guard can suppress by channel with `transportOnly`; active registration/configuration is not attested by an observation API. Other handler/lifecycle paths are not automatically excluded. | Critical |

## What the current bridge loses

`plugins/openclaw-memory-graphiti/index.ts` subscribes to `message_received` and
normalizes it before `event-bridge.ts` posts schema V1. That POST forwards provider
ID, conversation, sender and content/time/reply data. It does not forward native
`fromMe`, the self alias bundle, native-ID provenance, a durable observation cursor,
or effective dispatch-isolation evidence.

More importantly, the installed WhatsApp `emitWhatsAppMessageReceivedHooks` is
opt-in and uses `fireAndForgetBoundedHook` (concurrency 8, queue 128, timeout 2s).
It is not an acknowledged native receive seam. Retrofitting the POST receiver with
a durable log cannot reconstruct events which never reached that receiver.

## Read-source comparison

| Surface | Why it is insufficient as V1 canonical source |
|---|---|
| `messages_read` | Calls `sessions.get`; transcript, not atomic native snapshot plus durable inbound cursor. |
| `events_poll` | MCP process-local mixed-event queue and cursor; optional projected message ID/sequence. Does not prove native identity/self or restart continuity. |
| `events_wait` | Waiting on the same event mechanism does not strengthen its identity/durability guarantees. |
| Current bridge POST / `message_received` | Lossy bounded delivery; missing self/native provenance and no snapshot boundary. |
| Native ingress queue read-only access | Real native IDs and durable pending records, but only pending/claims/failed listings. Completed records are tombstones without message payload; retention pruning exists. No complete read cursor or atomic context/tail API. |

The queue type is in installed `ingress-queue-D6zAUTem.d.ts`. Its `access:read-only`
option explicitly avoids creation/migration/configuration of the state database.
That is useful but does not turn `listPending` into a complete observation stream.
**Do not claim/drain/release/recover that queue to observe it**: these are lifecycle
mutations and could steal work or dispatch the old agent.

No installed database was opened during this investigation.

## Minimum missing upstream seam

Before implementing a usable downstream source, expose an independent read-only
observation of the native durable admission, preserving:

1. Account + exact native route + native message key, with explicit native origin;
   reject missing keys, never use the later random `eventId` fallback.
2. Native `fromMe` plus authoritative actor/self alias resolution. Unresolved
   aliases remain unresolved and cannot pass self-identity readiness.
3. A retained payload and durable observation order independent of dispatch queue
   claim/completion/pruning. A transactional outbox at native durable admission is
   one implementation option; an acknowledged replayable seam is another. Neither
   is implemented here and neither should invoke agent dispatch.
4. Atomic context snapshot/tail or a single observation journal supplying both.
   Coverage start, retention floor, capture discontinuity and invalid epoch must be
   explicit. Startup cannot claim historical completeness it does not possess.
5. Effective route exclusion evidence from active lifecycle registration/admission,
   bound to the current process/config revision; not a caller-supplied assertion.

The next missing contract is therefore **native durable admission observation**,
not a nicer transcript parser. The installed upstream contains the required raw
identity, but the worktree bridge does not own a reliable export of it. This slice
must not silently patch the installed monitor or read private DB tables as an
undocumented substitute.

## Proposed downstream contract (not implemented / not advertised)

Version: `inbound-observation:v1`, exact match; unknown versions fail closed.

```text
observations_status(trustedRoute)
  -> resolvedSession, capabilities, dispatchStatus, sourceRevision, continuity

observations_open(trustedRoute)
  -> resolvedSession, snapshot[], snapshotCursor, coverage

observations_poll(trustedRoute, afterCursor)
  -> events[], nextCursor, continuity

event:
  stableEventId, providerMessageId, sessionRef, actorRef, fromMe,
  channel, occurredAt?, receivedAt, content, sequence
```

- Read APIs have no agent/transport-send dependency. They do not create executions.
- Session binding includes account/channel/exact DM. Opaque refs are projections
  of trusted identity, not authority originating in a project or request payload.
- Event identity derives from framed native-key scope, never text/time/position.
  Event updates require explicit future semantics rather than silently overwriting
  one message's content under a creation ID.
- Cursor binds durable journal epoch, exact session and monotonic append sequence.
  Sequence is local observation order, **not claimed provider chronological order**.
- Snapshot is a committed prefix through C; poll after C contains only later
  committed observations. Truncated context must declare its coverage.
- Repeated polls/restart preserve identities and cursor meaning. Old epoch,
  retention loss, upstream capture uncertainty or conflicting native identity
  quarantines observation; do not continue as an intact conversation.
- Native `occurredAt` is optional. `receivedAt` is never relabeled original time.
- Capabilities remain false/unknown until backed by the actual source guarantees.
  Fixtures demonstrate implementation behavior only, not deployed capability.

## Dispatch isolation and manual operation

`plugins/whatsapp-filomena-guard/index.ts:125` already registers `before_dispatch`.
With `transportOnly:true` and the matching channel it returns `{handled:true}`
without reply text. The inspected hook runner uses first handled claimant wins.
The corresponding dispatcher can send a handler's returned text, so another prior
claimant cannot be assumed safe merely because this guard also exists.

Potential operator setting, **not a sufficient proof or an instruction to change
it now**: the installed `whatsapp-filomena-guard` plugin entry's config
`transportOnly=true`, channel `whatsapp` (manifest declares this field). This is
channel-wide and may affect more than the one DM. Personal configuration was not
opened, so no active file/value/registration is asserted here.

Before asking Helena to change anything, a status implementation must report the
active guard/admission revision, matching scope, lifecycle path and competing
handlers relevant to that route. `disabled` must mean no legacy agent dispatch for
this exact route under that active configuration. Unknown or stale evidence fails.
Changing WhatsApp `dmPolicy` to disabled is not a proven substitute: access control
can reject the inbound before it becomes observable.

No personal/Gateway changes were made. Merely reading a config file or receiving a
human declaration must not pass the eventual runtime check.

## Non-resumable Shadow run — approved semantics, implementation deferred

The approved model removes full FSM recovery as a Phase 0 design prerequisite:

- Each explicit start creates a fresh ShadowRunId, preflight, pinned project and
  snapshot cursor; participation begins at project default.
- Prior executions are not resumed. Durable seen native IDs still prevent replay.
- Ambiguous previous processing is preserved for audit, never retried automatically.
- Snapshot may hydrate history only through a context-only path preserving true
  authorship; never feed historical messages into normal inbound admission or
  relabel historical human messages as outbound to avoid triggering execution.
- No shadow speech enters history or changes spoken participation state.
- DM Live still requires stronger crash/history/state recovery semantics.

Current `ShadowInboundJournal.RequiresRuntimeRestore` and handoff behavior were
not changed in this blocked investigation. Non-resumable run startup remains work
to implement alongside the actual source binding, not a claimed completed feature.

## Implementation/test sequence after the source seam is available

1. Native capture tests: missing key, identical replay, PN/LID/fromMe, route scope,
   missing actor and capture interruption; zero dispatch/send dependencies.
2. Durable observation journal tests: snapshot race, immediate post-snapshot event,
   repeated poll, restart, retention floor, epoch change, duplicate/conflicting
   identity, ordered batches and explicit gaps.
3. Status tests: unknown/enabled legacy path fails; competing handler or changed
   active revision invalidates proof; projects cannot assert capabilities.
4. C# V1 strict adapter and preflight binding, then non-resumable run lifecycle.
   Keep Graphiti and SendGuard unchanged.
5. Fake integration: snapshot opens zero executions; new native event exactly once;
   restart replay zero; self zero; other DM/group/channel rejected; gap quarantines.
   Shadow result creates no outbound/history/spoken-state side effect.
6. Only after local tests, operator-authorized live read-only transport preflight,
   then existing Graphiti reachability/namespace/canary/empty-outage checks.

## Delivery status

- Completed: concrete source investigation and this blocker/contract document.
- Not completed: V1 adapter/journal/RPC/bootstrap implementation and its new tests.
- Existing fail-closed preflight remains unchanged; no synthetic readiness claim.
- Live Graphiti preflight as a combined next gate: **NOT READY** until transport
  proof is available. No claim that Graphiti itself failed (it was not queried).
- First Shadow Run: **NOT READY**. No listener or execution started.
- Zero outbound, zero agent dispatch, zero external mutation during this turn.
- No implementation commit and no push.
