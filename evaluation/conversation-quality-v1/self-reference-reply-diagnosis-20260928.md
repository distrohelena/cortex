# Self-reference: quoted speaker projection

## Scope and status

Local, deterministic input repair; not deployed. No prompt, participation, voice,
model, memory, active configuration or sender changes. No inference or test messages.
This finding does not prove that all observed pronoun-resolution errors have one cause.

## Evidence

The current consolidated situation prompt already distinguishes current author,
quoted author, addressee and mentioned person. Adding another instruction without
checking the supplied data would obscure the problem.

`NativeObservationReader.ReadReply` looked up every quoted participant using
`ActorRef(sourceSession, participant)`. However, `PreflightAsync` registers self
under the separate `selfIdentityRef`, not the human participant reference. Thus a
quote whose participant is the host's own PN/LID could preserve its text and ID
while losing the quoted speaker entirely (`remetente = null`).

`NativeInboundContext.EnrichAsync` enriches human peers, not self. It cannot recover
an absent quoted speaker. `SessionFlowRuntime.MessageWithReplyIdentity` preserves
the projected message; it does not repair that missing identity either.

## Minimal repair

For citation display only, compare the normalized quoted participant against the
existing host-owned `NativeSelfMentionIdentity.AccountPn/AccountLid`. On exact
match, retain the canonical `selfActor` and host-provided display name. Otherwise
retain the existing participant lookup. Do not bind new actors from quote metadata.

The quote remains `origem = citacao_do_remetente`: this is attribution supplied
with a citation, not independent proof that an authenticated self event occurred.
It neither changes the current inbound author/fromMe nor authorizes any action.
Quoted text mentioning Filomena without matching metadata must not identify self.

## Before/after comparison

The same six parametrized tests call the real reader with a fake HTTP status service.
These are controlled protocol fixtures, not replayed production messages.

| Case | Before | After |
| --- | --- | --- |
| DM quote of own PN | Speaker null; failed | Canonical self and host name |
| DM quote of own LID | Speaker null; failed | Canonical self and host name |
| Group quote of own PN | Speaker null; failed | Canonical self and host name |
| Group quote of own LID with device suffix | Speaker null; failed | Canonical self and host name |
| Unbound foreign LID, text mentions Filomena | Unknown; passed | Still unknown |
| No participant metadata, text mentions Filomena | Unknown; passed | Still unknown |

All cases also preserve current actor, fromMe=false, text, exact quote ID and citation
provenance. Before: 4 failures, 2 passes; exact failure was Expected Object / Actual Null.
After: all 90 tests in `NativeObservationReaderTests.cs` passed, zero skipped.

## Verification limits and reproduction

The normal `Filomena.OpenClaw.Tests` project was attempted but could not compile:
`FrontendBuildTests` references missing `Filomena.App.Frontend` types and
`SettingsStoreTests` references missing `Filomena.App.Settings`. These unrelated
files were not changed. This is not a green full-suite claim.

A temporary local harness at
`C:\Users\Helena\filomena-self-reference-tests\ReplyProjection.Tests.csproj`
links the unchanged full reader test file and references the real OpenClaw/Core
projects, avoiding unrelated App test compilation. Command:

```powershell
dotnet test C:\Users\Helena\filomena-self-reference-tests\ReplyProjection.Tests.csproj --no-restore --verbosity quiet
```

The reader and its production dependencies compiled in this test run.
`git diff --check` also passed (existing CRLF conversion warnings only).
The codebase-memory MCP was unavailable (`Transport closed`); references were
checked directly in source instead.

## Remaining evaluation

This closes a proven input-projection gap, not the broader semantic benchmark.
Before claiming improvement on historical “ela” cases, compare actual/reconstructed
model inputs explicitly, including self, another woman, unresolved pronouns and
replies to older messages. Preserve reconstruction labels and baseline/candidate
outputs. Do not turn every feminine pronoun or every reply into direct involvement.
No active artifact was replaced in this slice.

## Follow-up: speaker coverage validation

The expanded offline harness also links the real `NativeInboundContext` and
`GraphitiParticipantReader`, the existing eight participant tests and eight new
`NativeSpeakerContextTests` cases. Total: **106 passed, zero skipped/failed**.
Cases cover DM/group current author, another quoted person, same quoted person,
self, unresolved attribution and absent registered names. Current author, quoted
author, native IDs, quote text/provenance and sequence remain separate. Existing
negative tests reject foreign namespace, wrong UUID/actor, wrong entity type and
backend error. External Graphiti is faked; this is not live data acceptance.

A read-only audit of the running release's saved
`quote-reconciled-runtime/observation-live-context.json` found:

| Saved history subset | Inbound | Missing current name | Replies | Missing quoted speaker | Missing quoted name |
| --- | ---: | ---: | ---: | ---: | ---: |
| All retained inbound | 431 | 271 | 72 | 34 | 64 |
| Last 50 inbound | 50 | 37 | 5 | 1 | 4 |
| Last 20 inbound | 20 | 17 | 2 | 1 | 2 |

The checkpoint contains 586 messages total, including 155 outbound. Last-20 inbound
timestamps range from 2026-09-28 15:21:11Z to 16:48:21Z. One of the 72 retained replies
has no quoted text. Only aggregated counts were printed; no chat content or private
transport IDs were exported into this report. Counts describe this checkpoint,
not all messages in WhatsApp or the exact subset included in each model request.

Source wiring in `NativeShadowCommand` enriches polled inbound before
`AcceptAsync`; `NativeInboundContext` explicitly projects absent registered names
as an empty string. Restored history does not acquire names retroactively merely
because a later participant lookup has a name. These observations prove incomplete
saved context, not whether each absent name resulted from missing registration,
older ingestion or restoration. That causal split remains to be checked before
repairing historical data. Presence of a nonempty name also does not independently
prove its real-world correctness.

Conclusion: **not all messages/replies currently have names**. Successful local
mapping tests do not establish complete live identity coverage. No active files,
memory records or messages were modified during this validation.

## Follow-up repair and controlled activation

Three further losses were confirmed in source and reproduced offline:

1. Native projection always emitted an empty display name, dropping provider
   `pushName` even after independent actor/session validation.
2. Canonical enrichment replaced that presentation with an empty string when the
   registered entity had no display name.
3. Native memory staging omitted `sender.displayName` altogether.

A read-only database count found 92 Person nodes in the group namespace, only three
with a nonempty current display name. This count includes all Person nodes, not a
claim that there are 92 canonical human group members.

The reader now preserves bounded, control-character-free provider display names
as presentation only. Registered names retain precedence when present. Missing
registration does not erase the provider presentation, and backend failure still
fails rather than becoming absence. Staging carries the display name without
changing canonical identity, UUID derivation or authorization. Requests without a
name retain their previous serialization/digest. A conflicting already-staged
request still fails closed; no historical ingestion requests were rewritten.

Validation: 122 focused .NET tests passed both against source and against the exact
candidate App/OpenClaw DLLs with the existing operational dependencies. Candidate
build passed with zero warnings/errors. Four new failures (native name projection
and enrichment) and a separate staging failure were observed before their fixes.
Ten offline history-repair tests passed, including account/session/actor mismatch,
conflicting quotes, duplicate evidence and future-message rejection. This does not
replace the previously blocked full solution/test-project verification.

All 271 missing current-author names were recoverable from retained native records,
matched by exact scope/native ID, provider participant proof and canonical PN.
The offline repair preserves existing names, IDs, text, timestamps and state.
Quote attribution is filled only from an earlier exact-message match in the same
channel/account/conversation, with matching quote text and no conflicting actor.
It remains sender-supplied citation context, not a new authenticated event.

Recovered checkpoint: 431 inbound, zero missing current-author names; 64 unnamed
quotes reduced to two unresolved quotes. All 586 total history entries preserved.
Do not claim universal identity correctness: provider display names are labels,
and the two unresolved quotes were deliberately not guessed.

The active worker was drained via its existing stop.request mechanism. App and
OpenClaw DLL/PDB files plus the prepared checkpoint were replaced with private,
hash-recorded backups in `name-repair-20260928`. Journals, brain project, model
assignments, routes and permissions were not changed. Checkpoint checksum and
inbound/outbound journal fingerprints were verified before installation.

The first restart (`conversation-host-running-15`) stopped at `preflight_stale`.
Read-only inspection confirmed valid snapshot, armed revision-bound isolation,
healthy unchanged epoch and every relevant path excluded. Only the operational
approvedRevision pins and capability-file hash were refreshed against that proof,
with backups; the safety check was not removed or weakened.

`conversation-host-running-16` then passed startup/preflights, restored 586 history
entries and reached normal polling. Memory worker reported running, zero pending
at that check, with its existing partial-coverage caveats unchanged. No historical
message was re-executed or resent by this repair, and no test message was sent.
No post-deploy human inbound was used to claim end-to-end response quality.
