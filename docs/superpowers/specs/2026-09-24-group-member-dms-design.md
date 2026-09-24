# Group-member DMs alongside the active group

Status: written architecture approved in chat on 2026-09-24; no live changes applied.

## Outcome and scope

Members of the currently authorized WhatsApp group may initiate a private
conversation with Filomena and receive replies. The group keeps working at the
same time. This grants responsive DM access, not unsolicited private messages,
access for strangers, access to other groups, or access to another conversation's
history or memories. Membership comes from the native WhatsApp account, never
display names, message text, project JSON or a model.

Reuse the current project, models, participation flow, generation, enforcement,
session runtime and bound sender. No brain tuning, memory writes, new transport
framework or legacy-agent routing is part of this feature.

## Existing boundaries verified in source

OpenClaw worktree:

- `src/channels/message/native-observation-bootstrap.ts` reserves the entire
  state directory for one bootstrap and accepts one `nativeSession`.
- `src/channels/message/native-inbound-isolation.ts` permits one isolation policy
  per channel/account. Receiver bindings retain denial through drain and cannot
  acquire proof for a policy installed after the receiver was created.
- `src/channels/message/native-observation.ts` already keys captures by database,
  queue and session, and journal scopes by channel/account/session. Closing a
  reader does not close the shared SQLite owner. Keep these persistence contracts.
- `extensions/whatsapp/src/inbound/group-metadata-cache.ts` owns native group
  metadata, PN/LID information, participant-change invalidation and reconnect
  cache behavior. Its existing mention/display projection is not itself a DM
  authorization contract.
- `src/gateway/observation-startup.ts` currently starts one bootstrap.

Filomena worktree:

- `src/Filomena.App/NativeLiveCommand.cs` supports continuous group delivery, but
  bounded DM windows only; authorization is bound to one exact route.
- `src/Filomena.App/NativeShadowCommand.cs` composes one reader, cursor, runtime,
  history and sender. Reuse this composition per conversation.
- `src/Filomena.App/NativeObservationHostBinding.cs` validates exact DM identity
  and namespace, but requires per-participant memory probes with existing canaries.
  A new member's DM cannot silently inherit the group's probes or namespace.

## Chosen composition

Use one host-owned conversation owner for the native account and a small .NET
supervisor of the existing per-session composition. Do not launch a second
WhatsApp connection per member. Do not repurpose the group runtime for DMs.

Rejected alternatives:

1. Open the Gateway DM allowlist: this grants the legacy lifecycle access and
   does not connect private inbound to the new runtime.
2. Run separate Gateways for each member: duplicates account ownership and
   complicates reconnect, credentials and deduplication.

### Native membership authority

The deployment pins the account and source group. A narrow native-owner surface
returns membership evidence from that account's authenticated provider metadata:
source group, canonical PN and supported LID aliases, owner generation and
membership generation. Model-facing tools receive none of this authority.

Missing, ambiguous or conflicting identity is not authorized. An unavailable or
invalidated roster is not an empty roster and does not grant new access. Changes
and reconnect invalidate old evidence. Check membership again at execution and
at the send boundary; removal prevents later sends, including a generated reply
that has not yet crossed the send boundary. A send already accepted by the
provider cannot be recalled by this policy.

### Native session ownership and isolation

Expand the existing bootstrap/isolation owner to multiple exact scopes while
retaining one owner per account and one owner per journal scope. A receiver binds
the account owner before intake begins; trusted membership updates may add DM
scopes through that same owner, not through late installation of an unrelated
gate. The existing singleton-session configuration stays supported.

For a member's first DM, resolve trusted native identity and reserve that exact
scope before the first legacy side effect. Complete isolation and explicit host
capture authorization before normal durable admission. Pending initialization
must not lose the first message or dispatch it through the legacy agent. Failed
initialization is an explicit deferred/failure outcome, not successful processing.

Startup remains capture OFF. The explicitly authorized host supervisor may arm
each eligible session only after its own valid snapshot/revision and dispatch
proof. Arming one session does not approve another. Retain raw/effective dispatch
evidence, runtime/recovery barriers, quarantine and safe drain. Removing a member
revokes responsive-DM access without dumping pending work into the legacy agent.

No journal schema, cursor protocol or atomic queue-plus-observation change is
required by this design. Prove that independent scopes coexist on the shared DB.

### Per-conversation runtime

The .NET supervisor creates a session only from a native, authorized incoming DM
scope. It reuses the ordinary session runtime and bound sender. Each conversation
has its own binding, memory namespace, inbox, cursor, outbound journal, history,
participation state and in-flight execution. A single host admission path prevents
duplicate workers for the same canonical DM, including PN/LID aliases.

Continuous DM operation becomes an explicit host capability; the existing bounded
DM and continuous-group entrypoints retain their semantics. A failure in one DM
is reported for that DM and must not reset the group's cursor or history. Never
retry an indeterminate send automatically. No proactive or startup messages.

### Memory readiness

Derive each DM namespace from its canonical native identity using the existing
identity functions. Never copy group history, the operator's DM history, canaries
or recall results into another namespace.

Existing populated namespaces use the current provenance/isolation preflight.
For a previously unused namespace, readiness must prove a successful scoped
absence through the backend's existing typed absence contract. An unresolved
Person, error, timeout or unscoped empty result is not proof. If the real backend
cannot provide sufficient provenance, that DM remains explicitly not ready;
do not fabricate canaries, create memory, or bypass checks to enable it.

## Verification gates

1. Two DMs plus the group on one account: separate isolation approvals, captures,
   cursor streams and cleanup; unrelated scope unchanged.
2. Valid member allowed; stranger denied; forged display name denied; PN/LID
   alias conflict denied; wrong account/group denied; self never executes.
3. First DM during initialization retained once; concurrent first messages create
   one runtime; retry/replay do not duplicate execution or delivery.
4. Member removal, roster failure and reconnect invalidate send authority; test
   removal while generation is in flight.
5. Stale snapshot/gate/raw evidence cannot authorize dispatch or capture; legacy
   normal/replay/recovery/imperative paths remain blocked for owned scopes.
6. DM A cannot read DM B/group history or memory; independent restart and failure
   do not reset another session. Scoped absence differs from outage.
7. Real composition with fake provider/model/Graphiti boundaries proves inbound
   to bound response with no unsolicited send and no actual external messages.
8. Relevant native and .NET regressions, typecheck, lint and build must pass
   before deployment. Live proof requires a member to send a real DM; report
   separately from local evidence.

## Migration and rollback

Keep the current group process running throughout local implementation. Before
activation, preserve only changed artifacts and private host configuration with
restricted ACLs. Stop the runtime at an idle boundary, retain the newest inbox,
cursor, outbound journal and history, then migrate the native owner and supervisor
under the existing controlled deployment procedure. Do not promise uninterrupted
service during the actual required restart.

Start with capture OFF, verify native readiness and exact scopes, then explicitly
arm the authorized sessions. Resume the group from its current checkpoint, never
from an older backup. Validate one member DM before declaring the feature live.

On failure, stop new work and safely drain. Preserve uncertain sends and pending
DMs for reconciliation; rollback must not silently deliver accumulated work via
the legacy agent. Never restore an older cursor to resend already handled input.

## Approval boundary

Concept approved in chat: simultaneous group and member DMs with separate history
and memory. This document records the cross-owner changes needed to implement it.
Written architecture review precedes the implementation plan and code changes.
