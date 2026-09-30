# Graphiti speaker-name metadata repair

## Scope

Helena explicitly requested correction of Graphiti after the separate runtime-history
repair. This operation fills missing presentation metadata, not inferred facts or
canonical identity. It is limited to the host-bound group namespace; no DM namespace
was touched. Display names do not confer authority.

## Evidence and selection

The read-only audit found 94 Person nodes, but only 10 carried canonical stable IDs.
Person nodes without verified identity were not matched by name, merged or renamed.
Three canonical registrations already had names and were preserved.

Native records were read with SQLite readOnly from the exact observation scope.
Selection required exact group, non-self inbound, verified actor reference, an
unambiguous PN from native participant/alternate fields, and a bounded provider
display name. Canonical participant and episode UUIDs were calculated using the
existing versioned UUID namespaces. Episode selection additionally required exact
channel, account, conversation, provider message ID, stable/canonical sender ID
and unchanged message text.

Result of the pre-write audit:

- 10 canonical actors supported by retained native records.
- 7 canonical Person records with missing names.
- 517 recognized inbound episodes; 493 lacked sender display names.
- All 493 had matching native evidence; zero contract mismatches.

## Applied changes

- Filled `Person.display_name_current` on the seven selected canonical nodes.
- Filled `sender.display_name` inside the 493 selected episode content envelopes.
- Preserved message text, reply payloads, timestamps, IDs, namespaces, relationships,
  extracted facts, summaries, aliases and embeddings.
- Created no episodes or facts; performed no re-extraction or model calls.

Each write required the original value still to match. Existing nonempty names
were not overwritten. The private plan contains the original values/content,
native evidence and a SHA-256 manifest; an applied-record log identifies each write.
Rollback uses compare-and-set against the repaired value and refuses concurrent
changes rather than overwriting subsequent work.

Private backup:
`C:\Users\Helena\.config\openclaw-observation-ops-20260925-member-dms\graphiti-name-repair-20260928`

Its ACL was restricted to Helena and SYSTEM before saving the evidence. Original
episode content is retained there; the native journal was not modified.

## Verification

Eight local plan tests passed, covering exact matching, preservation of non-name
content, mismatched account/session/actor/message/text and existing names. The
positive test was observed failing before the implementation.

Read-back verified all seven Person values and all 493 complete episode envelopes
against the prepared result. A separate read-only verifier used the operational
`GraphitiReadClient` and `GraphitiParticipantReader`; all seven names passed exact
canonical UUID and display-name checks through the real MCP API.

The first verifier attempt lacked the App's shared ASP.NET framework and failed
before making the read. Adding that framework reference fixed the harness;
the subsequent API verification passed. This was not a Graphiti outage.

No WhatsApp test message was sent and no character execution was deliberately
triggered. The already-authorized running service was not stopped for this repair.
Normal concurrent operation is separate from the effects of this repair.

## Limits

This verifies restored name metadata, not the correctness of all historical
inferences. The noncanonical Person nodes remain unclassified rather than being
assigned to group members by guesswork. Provider names are presentation labels,
not proof of real-world identity. The two unresolved runtime-history citations
from the previous repair are a separate limitation; this change does not pretend
to resolve them or regenerate facts from them.
