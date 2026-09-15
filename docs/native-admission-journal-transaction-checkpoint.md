# Native Admission Journal — transaction checkpoint

2026-09-11. Source investigation complete for the admission transaction; implementation
not started. This document does not advertise observation capabilities or live readiness.

## Reproducible source baseline

An isolated sparse checkout of upstream `openclaw/openclaw`, tag `v2026.8.1`,
commit `ea806575e6450e4d1efdfc72c19f04be982a1b9b`, was obtained at
`C:/Users/Helena/.config/superpowers/worktrees/openclaw/native-observation-v1`.
Branch: `feature/native-observation-v1`. No installed OpenClaw file was modified.
The checkout is clean and contains no implementation changes.

The source paths below belong to that checkout. This avoids treating installed
hashed bundle names as maintainable source paths.

## Actual admission order

1. `extensions/whatsapp/src/inbound/message-delivery.ts:486`,
   `handleMessagesUpsert`, receives native `WAMessage`. The coordinator already
   holds `socketSession.self`, account and resolver dependencies. Some special
   reaction handling happens before queue admission; the journal would cover
   durable admissions, not every socket packet.
2. At line 547, `durableInboundMonitor.admit` receives the native message,
   upsert type, skip flags, receive timestamp and process-local receive order.
   Self PN/LID is **not** included in the persisted admission payload today.
3. `extensions/whatsapp/src/inbound/durable-receive.ts:56` rejects missing native
   `key.remoteJid` / `key.id`. The durable event ID is derived from these keys,
   under the account-scoped queue. It is not derived from text or timestamp.
4. That module's serializer (line 109) serializes the native message into the
   queue payload, preserving native message fields. It does not invoke the agent.
5. `src/channels/message/ingress-monitor.ts:562`, `admitOnce`, calls
   `getQueue().enqueue` (line 573). The monitor serializes admission/claim access
   and has existing bounded persistence retries; no observer should add its own
   implicit retry policy.
6. `src/channels/message/ingress-queue.ts:644`, `enqueue`, enters
   `runOpenClawStateWriteTransaction` (line 656). It inserts the pending row in
   `channel_ingress_events`, with uniqueness on queue scope and event ID.
   The shared writer runs a synchronous immediate SQLite transaction.
7. Only later, `processDurableInboundMessage` (message-delivery.ts:405) obtains
   normalized identity, enrichment and debounce/reply-lane delivery. The normalized
   platform object contains `selfJid`, `selfLid`, `selfE164` and native `fromMe`.
   The public received hook is later still and is not a reliable capture boundary.
8. `ingress-queue.ts:1203`, `complete`, records completion and clears the pending
   payload/metadata. Retention can subsequently prune the tombstone.

## Metadata at the chosen boundary

- Native message key and raw payload: already available before the transaction.
- Account and exact native route: available, but account scoping used by the queue
  must not be confused with a user-facing session ID or a Graphiti namespace.
- Actor: native route for peer DM, native participant where applicable. Native
  `fromMe` distinguishes self. Missing identity must not become a guessed actor.
- Self PN/LID: available in the coordinator, needs an explicit bounded snapshot
  passed into admission. Re-resolving it after restart would not preserve admission-time evidence.
- Timestamp: native `messageTimestamp` when supplied; `receivedAt` is separate.
- `receiveOrder`: process-local counter, **not a durable cursor**.
- User-facing enriched body: may be produced later. A raw native payload and an
  enriched conversational message are different contracts; unsupported native
  message types must not silently turn into text executions.

## Atomicity finding

There is a concrete transaction owner where an additive observation row could be
inserted in the same SQLite commit as the native pending row. No queue replacement
is necessary. This requires an upstream schema/queue change, not a post-hook plugin
append. No claim of implemented or fault-tested atomicity is made yet.

The straightforward atomic design has a material consequence:

```text
insert native admission
insert observation
observation insert fails
rollback both
```

The existing caller ultimately logs `message dropped` after exhausted persistence
retries (`message-delivery.ts:559–568`). Thus an observation failure could introduce
an additional cause of native admission failure. The isolated checkout would not
change the running WhatsApp, but future installation of this design would have
that effect when capture is enabled.

An append after `enqueue` is not an equivalent alternative: a crash can occur
between the commits, while a legacy worker may subsequently tombstone the payload.
A non-atomic design needs an authoritative durable obligation/gap marker which
survives completion and pruning. Such a mechanism does not exist in the public
queue contract and has not been implemented here.

### Decision needed before adopting the persistent-store change

Accept the atomic projection's failure coupling for explicitly enabled experimental
capture, or require native admission to continue on observer failure, in which case
the next design must specify a durable pending-observation/gap obligation at the
native transaction owner. Do not implement a catch-and-ignore fallback.

This checkpoint follows the user's requirement to document admission impact before
adoption and the upstream repository's approval gate for material SQLite store designs.

## Dispatch inspection evidence

The installed `before_dispatch` runner accepts the first handled claim. However,
the dispatcher also has fast-command and plugin-owned-binding paths; these are
not covered merely by finding the later `transportOnly` hook.

`extensions/whatsapp/src/inbound/access-control.ts` also cannot be called as a
read-only probe: its pairing path can persist a pairing request and send a pairing
reply. Inspecting policy requires separating or using pure resolution, not invoking
that effectful entrypoint with a dummy message.

Consequently a future inspector must derive its answer from an authoritative
active route/config/handler view, without running hook callbacks or access-control
effects. The intended semantics remain:

- `inactive`: all relevant legacy agent-dispatch paths for the exact route are
  proven excluded under the current authoritative runtime view.
- `active`: an applicable legacy processing path is demonstrably available;
  this is not a claim that every possible message produces a reply.
- `unknown`: evidence is incomplete, stale, content-dependent or opaque.

No dispatch inspector has been implemented. No active configuration was read or
changed; neither active nor inactive is asserted for Helena's DM.

## Status

- Journal/API/cursor/retention/capability advertisement: not implemented.
- Bootstrap/non-resumable run changes: not implemented.
- Existing preflight remains fail-closed. No Graphiti adapter or brain changes.
- No new tests run; no mock results presented as native evidence.
- No agent dispatch, outbound, listener, Graphiti live query, live database access,
  Gateway mutation, installation, commit or push.
- Live transport preflight / Graphiti live preflight / first Shadow Run: NOT READY.
- Network activity was limited to reading the public Git repository into the
  isolated source checkout; no remote repository mutation occurred.
