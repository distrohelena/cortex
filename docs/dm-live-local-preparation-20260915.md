# DM Live — local implementation slice, still not enabled

Date: 2026-09-15. Follows `dm-live-local-readiness-20260915.md`. Local source and tests
only: no deployment, Gateway/config change, capture arming, real `messages_send`, model
call, Graphiti call or memory write. Brain v3 unchanged (SHA256
`47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`).

## What changed (all behind fakes)

**Durable attempt ownership** (`OutboundJournal`, `real:v1` namespace). A real attempt
is reserved as `pending` with fsync before any external call, in the same replay index
as shadow records: an execution or inbound ID that already has a shadow or real record
is blocked (`execution_replay` / `replayed_input`), so a historical WOULD_SEND can never
become a send. Resolutions follow a fixed transition table and are appended, never
rewritten: `pending → sent | not_sent | indeterminate`, `sent → history_committed`,
`indeterminate → sent | not_sent | abandoned` only through explicit reconciliation. On
open, a `pending` record with no resolution is appended as `indeterminate`
(`restart_with_pending`). Any attempt whose latest state is `pending`, `indeterminate`
or `sent` without `history_committed` keeps the session in `reconciliation_required`.
The `pending` record keeps the authorized text and speaker, and `sent` keeps the
provider message ID, so a durable receipt can be completed into history idempotently;
the journal never records the route and is not a memory source.

**Host live route** (`ShadowOutboundHost.BindLiveRoute`). Constructor still has no
transport. An administrative binding names exactly one allowlisted session, an opaque
transport route, the expected channel/account/recipient, the speaker identity used for
the host's own speech in history, and a single-part character ceiling (≤ 4000, the
native WhatsApp chunk fallback). `Rearm(Enabled)` is effective only with complete
capabilities and a binding. Dispatch order: provenance/session/DM/project checks →
payload limit → reserve `pending` → `DescribeRouteAsync` must match channel, account
and recipient exactly (`route_mismatch` / `route_unresolved` = `failed`, `not_sent`) →
short fence rechecking kill switch, binding identity and cancellation → one
`SendTextAsync`. Any exception from the send, a missing/unverifiable receipt, or a
receipt whose channel/recipient/account disagrees with the binding is `indeterminate`,
journaled, and latches the host to Disabled. The native gateway payload carries no
`accountId`; a receipt without it is accepted only when channel and recipient match,
and the trace records `receiptAccount=unattested`. The pre-send binding is therefore
the only account guarantee.

**Confirmed-send path.** The typed send node has exits `disabled | shadowed | sent |
blocked | failed | indeterminate`. Only `sent` with a provider ID and host-owned speaker
yields a `ConfirmedSend` on the run result. The runtime appends that speech to session
history once (keyed by provider ID, direction Outbound, speaker from the binding),
applies the existing CAS transition only for a successful run that reached an exit
after confirmed speech, and then records `history_committed`. Any send attempt without
confirmation keeps every reachable exit hypothetical, exactly as in Shadow. A run that
fails after a confirmed send still commits the speech, without transition.

**Outbound commit frontier.** When an execution starts, confirmed speech of predecessor
executions with a watermark beyond the batch's frozen watermark is added to that
batch's history snapshot (trace `history-frontier`). Inbound admitted after the batch
closed is not added; the pinned project revision is untouched. History nodes include
confirmed speech regardless of the `before-current-batch` boundary, in local admission
order.

**Stale pinned batch.** A batch closed before a predecessor's confirmed transition is
never executed under that stale state decision when its flow can act externally (it
contains a send node). At execution start, with its pinned project revision, it is
reprocessed against the confirmed current state (trace `batch-repinned`, with the
previous state/version), so generation, provenance and the send all happen under the
confirmed state and the CAS transition applies normally. When the confirmed state has
no entry for that kind or the batch shape no longer fits, nothing is generated or
dispatched: the outcome fails with `stale-state-entry-unavailable` or
`stale-state-batch-shape-mismatch`, the inbound stays in history and the inbox records
the failure for reconciliation (trace `batch-blocked-stale`). Flows without a send
node keep the existing state-snapshot semantics and the existing acceptance tests are
unchanged. Executions are serialized per session, so this happens at most once per
batch; there is no retry loop.

**Reconciliation surface** (host, explicit, never automatic).
`PendingSpeechCommits(session)` lists durable receipts whose speech never reached local
history; `CompleteSpeechCommit(session, attempt, transitionApplied)` closes one after
the caller appended it once. `Reconcile(session, attempt, verdict, evidenceRef,
providerMessageId?)` applies an operator verdict (`Sent` needs the provider message ID
from the evidence and then still needs a speech commit; `NotSent`; `Abandoned`) to an
`indeterminate` attempt only, recording `reconciled:<evidence>`. Nothing in this
surface dispatches; an indeterminate attempt cannot be re-dispatched by any API and a
restart cannot reopen it.

**Live composition, separate from Shadow.** `RuntimeHandoffCore` holds the common
composition (pinned project, trusted inbox, host, serial admission) behind a fixed
ceiling rechecked on every admission. `ShadowRuntimeHandoff` is bound to an effective
Shadow host and therefore can never reach a sender; `ManualShadowRun` and
`ObservedShadowRun` still construct hosts with `Unproven` capabilities and are
unchanged. `LiveRuntimeHandoff` requires an effective Enabled host (complete
capabilities and a bound route), first restores pending speech commits into history
once through the runtime's existing outbound admission (audit `speech_recovered`, echo
of that provider ID is self-suppressed), records their transition as not reapplied
because the FSM context is not restored across restarts, and refuses to start while an
attempt still needs explicit reconciliation. `LiveRouteBindings.FromShadow` derives the
route binding from the reviewed session binding (session, channel, account, the single
self actor as speaker stable ID); only the opaque transport route, the speaker's
display name, the native recipient JID and the verified single-part ceiling are
supplied. `OpenClawMcpTransport` and `OpenClawMcpSender` implement `IOutboundTransport`;
`DescribeRouteAsync` reads `conversation_get` as evidence on the same connection a send
would use. It does not pin the route: the bridge resolves it again inside
`messages_send`. No App command or listener was added for the live path.

## Native sender inspection (openclaw ea806575, read-only)

- `messages_send` accepts only `session_key` and `text`; the bridge resolves
  `sessions.describe` per call and generates `idempotencyKey: randomUUID()` per call.
  There is no caller idempotency and the destination is re-resolved inside the send.
- Gateway `send` runs `sendDurableMessageBatchCore` with `skipQueue` only for callers
  holding agent runtime authority; otherwise the durable queue may own a retry
  (`recoveryOwnedRetry`) for proven-not-sent failures. A thrown error after dispatch
  does not prove absence of a later delivery, hence `indeterminate` for any exception.
- WhatsApp adapter splits text via `markdownToWhatsAppChunks` at
  `min(configured limit, 4096)` with a 4000 fallback and sends trailing chunks as
  separate messages; the returned `messageId` is the first part only. One MCP call is
  not proof of one physical message above the configured limit.
- The gateway delivery payload carries `runId`, `messageId`, `channel` and, when the
  adapter reports it, `toJid`; never `accountId`.

## Capabilities are not a waiver

`TransportSafetyCapabilities` stays `Unproven` in every host the code constructs; only
tests pass a complete record. Enabled remains blocked. Per flag, what is closed locally
and what is still a gap:

| Flag | Closed locally | Still a gap |
|---|---|---|
| StableOrigin / StableInboundIdentity | durable inbox with epoch/sequence and provider ID dedupe | proof of the live source's contiguity is a preflight, not code |
| RouteBinding | exact binding, pre-send describe, receipt comparison | not atomic in the bridge: the route is re-resolved inside `messages_send` |
| SelfSuppression | self actor at the trusted inbox, provider-ID echo dedupe in the runtime | bridge still lacks `fromMe`/self LID; live echo never observed |
| SendReceipt | preserved as evidence, never inferred | no `accountId` in the native payload |
| EchoCorrelation | nothing beyond provider-ID dedupe | transcript mirror IDs are not proven equal to provider IDs |
| SinglePhysicalMessage | host ceiling ≤ 4000 characters | a ceiling bounds, it does not prove, one message; chunk mode/limit must be read from the account's effective config |

## Operational data

Nothing was transcribed or hardcoded. `LiveRouteBindings.FromShadow` takes session,
channel, account and self actor from the reviewed `ShadowSessionBinding` already used by
the manual/native shadow configs. The native recipient JID is the existing
`NativeShadowConfig.NativeConversationId`. Exactly three values do not exist in any
current artifact and must be supplied in the future authorized window: the opaque MCP
`session_key` of the DM (transport route), the speaker's display name for the host's own
history entries, and the single-part ceiling verified against the account's effective
chunk configuration. All values are revalidated by the host at bind time and by
`DescribeRouteAsync` before each dispatch.

## Graphiti capture and the live inbound path (audit only, nothing changed)

In `plugins/openclaw-memory-graphiti/index.ts`, `cfg.autoCapture` gates two Graphiti
writers: `message_received → messageRecorder.recordInbound` and
`message_sent → messageRecorder.recordOutbound` (normalized with `selfActorIds`). The
inbound HTTP forward to Filomena is gated separately by `cfg.eventBridge.enabled`
(`message_received` is subscribed when either is on). The example config ships
`autoCapture: true`. Consequences for the live lifecycle, derived from the code:

- A confirmed DM Live send would be captured into Graphiti by the plugin if
  `autoCapture` is on. The host adds no writer; this is the only outbound write path
  and its state must be declared for the window, not assumed.
- Inbound capture and outbound permission are independent. The five-message shadow
  window received inbound through `native-observation-v1`, not through the event
  bridge; the trusted inbound path for DM Live is that same native source and does not
  require `autoCapture`. Disabling `autoCapture` stops Graphiti writes without
  stopping trusted inbound; disabling `eventBridge` affects only the legacy HTTP
  bridge, which the live composition does not use.
- "Capture disarmed" therefore means: `autoCapture` state declared and, if Graphiti
  read-only must hold, off for the window; observation source armed as in the shadow
  window; legacy bridge irrelevant to the live path.

## Verification

Worktree: Core tests 538 passed, 10 live experiments skipped; OpenClaw 71 passed;
LLM 355 passed, 35 skipped. All new tests use fake transports and fake completion; none
opens a connection. Each of the three organized commits was exported to a clean
directory, built (`Filomena.slnx`) and had the three test projects run there.

Covered locally: confirmed receipt; missing binding; wrong/unresolved route;
ambiguous failure; cancellation before and after dispatch; kill switch after
reservation; crash after reservation with restart; receipt persisted before local
commit with restart and idempotent recovery; explicit reconciliation of indeterminate
(`sent`, `not_sent`, `abandoned`) with the old attempt never re-dispatched; duplicate
attempt (per execution, replayed execution, replayed input, two send nodes); shadow
record never promoted; shadow with bound transport; receipt route mismatch/unverified/
unattested account; payload ceiling; incomplete capabilities; forged provenance; two
queued turns with frontier, no future inbound, no project repin, stale pin reprocessed;
stale pin blocked when the confirmed state has no entry; echo of own provider ID
duplicate/self-suppressed; unconfirmed exits hypothetical; shadow handoff refuses an
Enabled host and the live handoff refuses anything but Enabled; disarmed live host
preserves inbound; route description on transport and shared sender.

## Remaining gates before any real send

1. The three operational values above, supplied and revalidated in a reviewed window.
2. Every capability gap in the table. None can be closed by declaration; route
   atomicity, echo correlation and self identity on the bridge need evidence or a
   bounded native change approved separately.
3. FSM context is not restored across restarts: recovered speech enters history, its
   transition is recorded as not applied, and the inbox still requires a new
   non-resumable run. Operational procedure for `Reconcile` (who reads what evidence)
   is undefined.
4. No App command or listener composes `LiveRuntimeHandoff`; the live path exists only
   as a tested Core composition with fakes.
5. `autoCapture` state for the window must be declared; the plugin was not changed.

DM Live remains **NOT READY** and **NOT ENABLED**. Nothing was sent.
