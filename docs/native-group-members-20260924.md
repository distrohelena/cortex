# Native members of the authorized group

Host authorization `allowNativeGroupParticipants` must match the hashed live capability
evidence and requires exact group delivery. It defaults to false. The initial participant
map remains a set of memory-preflight anchors, not an exhaustive group membership list.

For the enabled policy, the authenticated observation reader verifies each non-self
event's exact group, fromMe flag, native participant hash and PN/LID aliases. The canonical
PN comes from the provider key, never pushName or message text. Alias conflicts, foreign
groups and unresolved identity still fail closed. Device suffix normalization matches
the native projection. This does not grant another group or DM access.

The host-only binding policy is separate from the existing durable checkpoint lineage:
the exact group, account, project and initial identity anchors do not change. The policy
does not mutate that hash while learning native actors. A narrower binding cannot accept
new peers or restore their records. Model output cannot construct trusted input.

Known image/video/document captions remain text. Known uncaptioned media and reactions
become durable `ignored` inbox events: native identity and sequence retained, no brain,
no history text invented, no send. Unknown payload schemas still fail closed. `ignored`
advances the durable cursor and is deduplicated after restart, but consumes no execution
budget. The native journal itself is unchanged.

Regression: 1,276 passed / 50 skipped on 2026-09-24, including new-member authorization,
foreign actor/group, self collision, alias conflict, device suffix, unknown payload,
media followed by text, repeat delivery and durable restart. No models run by these tests.

Operational change: the .NET runtime artifact and private host/evidence documents,
plus the narrowly scoped Graphiti read-result correction below. No Gateway redeploy,
project/prompt change or memory write. The existing
same-epoch cursor resumes pending journal records; processed events are not resubmitted.
Private rollback copies reside beside the launcher in `before-native-group-members`.
Never restore an older reader and reset the cursor after new sends: stop at an idle
boundary and retain inbox, outbound journal and context for reconciliation instead.

## Additional blockers exposed by actual group participation

The first replay reached the real runtime but exact memory lookup failed because an
authenticated new group participant had no persisted Person node. The backend had
collapsed `NodeNotFoundError` into an undifferentiated ErrorResponse. It now returns
`entity-neighborhood-absence:v1` with the exact requested namespace and subject UUID,
only after that typed absence from the scoped lookup. No Person is created. Connection
errors, timeouts, foreign subjects and malformed absence evidence still fail closed.
The .NET reader validates the explicit absence against the exact query. The canonical
participant preflight remains stricter and does not treat this as a resolved Person.
Four isolated Python tests pass; six C# absence/provenance cases are in the totals above.

The failed batch had zero send-boundary attempts and an unchanged outbound fingerprint.
A reviewed one-off reconciliation retained the entire old lineage (`...reconciled-03`)
and created `...reconciled-04` from the last exact checkpoint prefix, with an audit. This
is not automatic retry of failed or uncertain sends. The four text events were retried
once there; the response was provider-accepted and history-committed, and cursor 8 saved.

The following native self echo exposed another adapter bug: `agent:filomena` was passed
to the human Person constructor before self suppression. It now retains the trusted
agent identity and is suppressed by the durable inbox before memory/brain execution.
The response is not resent; subsequent restart resumes after cursor 8.

Final live verification: runtime resumed, processed the subsequent self/non-text events
without execution, advanced to cursor 10 and continued healthy empty polls with zero
new send attempts. The prior batch has one provider-accepted, history-committed reply.
The continuous exact-group process remains active by the user's authorization.

Rollback of the Graphiti code consists of restoring the two backed-up Python files to
their original container locations and restarting that API container. This changes no
stored memories. Do not downgrade the runtime while resuming unknown participants;
stop at an idle boundary if rollback is needed, keeping all committed journal evidence.
