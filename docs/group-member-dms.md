# Group-member DMs: local implementation and activation gates

Status: local implementation and acceptance checks passed. This document does not authorize deployment or claim that group-member DMs are enabled. The latest approval covers local correction and fail-closed tests only.

## Host entrypoint

The new entrypoint is `--native-conversation-host <host-config.json>`. It owns the existing group worker plus one worker per authenticated native member-DM scope. It is a live-capable command, not a dry run; do not invoke it against operational configuration during local verification.

Host configuration fields:

| Field | Owner / purpose |
| --- | --- |
| `groupLiveConfigFile` | Existing reviewed group live configuration; preserve its state directory. |
| `managedMemberDmTemplateFile` | Existing continuous-DM live template. Its `nativeConfigFile` points to a DM native template whose `manualConfigFile` supplies the DM-specific outbound roles. Never point this chain at the group config. |
| `memberDmStateRoot` | Private local root for separate per-conversation state. |
| `accountId`, `sourceGroupJid` | Exact native account and source group, never a display name. |
| `pinnedProjectRevision` | Existing approved project revision. |
| `foreignControlNamespace`, `foreignControlCanary` | Existing read-only memory isolation controls; no data creation. |
| `discoveryIntervalMilliseconds` | Discovery/poll scheduling interval; not an authorization lifetime. |

Credentials remain environment-variable references in the existing host configurations. Do not place credentials or native authority in the project or prompt. Scope-specific configuration files contain private route/identity evidence and must stay in the private state root, outside Git.

Group and DM outbound roles are separate host configuration, even when they use the same frozen project. The member template must match the approved project/hash, account and self identity; its candidate wiring must be reachable from the graph's DM entry and cross the normal Send boundary. The child copies those validated DM roles, not the group's generation/Boundary node bindings. Node-name prefixes are not authority. Actual peer, aliases, namespace, route, epoch and private state still come from the validated native member scope.

Interactive host commands are `/refresh` and `/quit`; cancellation drains workers. No input text here is a fabricated WhatsApp message.

## Admission and authority

1. Native membership resolves the exact account, canonical peer and validated PN/LID aliases.
2. Validate the current effective snapshot and membership lease.
3. Arm exact-DM isolation against that snapshot; inspect raw and effective paths.
4. Require aggregate `inactive`, with matching snapshot/gate revisions and recovery inventory.
5. Only then create capture and publish the DM reader, epoch and initial cursor.
6. The .NET host independently validates authenticated discovery and the child reader before starting its existing runtime composition.

An invalid snapshot, uncovered active/unknown path, changed revision, missing membership or expired lease never grants capture/execution authority. Isolation can remain fail-closed after rejected initialization; rejection is not permission to release pending input to the legacy agent.

The local authorization-order regression uses the actual native owner and dispatch reducer: before arming, aggregate dispatch is unknown even though the effective snapshot is valid; after exact isolation is armed, all required effective exclusions must be proven before capture starts. Counterproofs cover an uncovered active path, an uncovered unknown path, an invalid snapshot, and a revision change during recovery inspection. Failure must leave no child epoch or runnable discovery descriptor.

Each member-DM observation carries the immutable native admission lease reference. A later membership refresh cannot lend its new lease to an older event. Group events and ordinary DM events retain their existing contracts.

The nullable `membership_lease_ref` column is registered in the canonical state schema and its existing first-use additive migration mechanism. It remains feature-owned; older records retain null metadata and their original digest. Cold reopen must accept the canonical schema, not rely on a still-cached SQLite connection.

Ordinary/group durable event IDs retain their original hash input bytes (`remoteJid + "\n" + nativeId`). Member-specific canonical alias deduplication must not change that existing identity or invalidate persisted group replay and quote metadata.

An authenticated old-generation event is durably ignored with `member_dm_event_generation_stale`, without memory enrichment or execution, so later valid events can progress. Missing or malformed authority is still a failure, not a successful empty batch. This distinction survives process restart through the existing inbound registry.

The managed-DM sender uses the existing reviewed `whatsapp-bound-member-dm-text:v1` contract. That contract does not authorize quoted replies: a request carrying a quote target fails before the native tool call, rather than silently degrading to an unquoted message. Existing group quote support is unchanged. The local plain-text acceptance test is not evidence of managed-DM quote support.

## Local acceptance before activation

- Real native HTTP responses must pass the .NET reader/preflight, including dispatch, self/actor, route and control metadata. A negative fixture test is not positive integration evidence.
- Prove first-message retention, PN/LID dedupe and no legacy approval/pairing/agent dispatch through the real receiver path.
- Prove group plus two DMs have separate inboxes, histories, cursors, outbound journals and memory namespaces; preserve the existing group checkpoint.
- Prove removal, lease renewal, reconnect and restart do not authorize stale queued execution or stale physical sends. Quarantined work must not prevent later authorized work from progressing indefinitely.
- Run relevant regressions, builds, typechecks, scoped lint and compiled fake-boundary composition. No model, Graphiti or transport calls are necessary for these local checks.

## Future controlled activation and rollback

Local evidence (2026-09-25): native 239/239 tests, core/extensions typecheck, scoped lint/format, canonical schema verification and full build passed. Compiled isolated group-host startup passed 3/3 with capture OFF. The .NET host/composition tests passed 5/5, including one real runtime execution and exactly one fake bound-send receipt to the correct member DM; Graphiti, model and provider boundaries were simulated. App/LLM suite: 595 passed, 42 live-only tests skipped. No real outbound or active deployment occurred.

Activation remains a separate operational gate. Prepare a minimal private backup of changed artifacts and host configuration, record hashes, gracefully drain the old process, and preserve current cursor/inbox/outbound evidence. Start native capture OFF and verify actual status before explicitly enabling enrollment/capture.

Never copy an older journal or cursor over newer delivery state. Never automatically retry an uncertain send. If activation fails, stop new work, drain safely, restore only the backed-up code/configuration, and verify the original group service against the newest durable state. Do not restore credentials, memory data or unrelated personal configuration.

Before activation, validate the exact rollback build against a copy containing the new journal schema. The old canonical validator rejects unexpected columns on optional/first-use tables; therefore a bare nullable column is not, by itself, proof that an older binary can reopen this database. A rollback must preserve the current records and metadata, and may require a schema-compatible rollback artifact. Do not silently drop the membership column, remove its records, or restore an older database to make rollback start.

Only a real member-initiated DM and provider-confirmed correct-DM response can establish live success. No unsolicited canary or private message is part of local verification.
