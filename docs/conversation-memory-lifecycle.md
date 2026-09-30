# Conversation-owned memory lifecycle

## Operational contract

The approved local group pilot starts through `scripts/run-native-conversation-host.ps1`.
`MemoryConfig` and the approved `AppFile` are explicit host-owned arguments. The CLI
runner refuses missing memory configuration; it does not silently fall back to a
read-only conversational service. Existing inference binaries and project revisions
are unchanged.

`tools/native-conversation-runner.mjs` owns both the conversation child and memory
maintenance in its own process. It opens memory before launching the child, keeps it
through child reconnect attempts, and drains/closes it before reporting a terminal
state. `stop.request` stops both; a pre-existing request starts neither. Memory
startup failure prevents the conversation child from launching.

`tools/conversation-memory.mjs` composes the existing `NativeIngestStore`,
`NativeIngestService`, `NativeMemoryRecovery`, recorder and history source. It does
not create another ingestion engine or invoke legacy message hooks. It selects
only the exact account/group present in the host's conversation configuration,
even when the private memory configuration lists additional groups. DMs are not
implicitly ingested or enabled.

The maintenance pass is asynchronous and sequential. The existing durable receipts
and recovery checkpoint survive process restart. Unknown backend acceptance is
reconciled, never blindly resubmitted. It never advances the conversational cursor
or triggers replies for historical messages. Eligibility includes retained human
text independent of whether Filomena replied.

## Evidence and limitations

`memory-status.json` in each private run directory reports discovered, persisted,
pending, coverage and diagnostic separately. Pending/outage is `degraded`, not
healthy/learned. A persisted episode does not prove useful or correct extraction.
The source covers retained local history only, not arbitrary missing WhatsApp
history; unsupported media and missing payloads remain explicit gaps.

The currently integrated path discovers retained messages periodically; it does
not claim atomic memory staging at the instant of native admission. Native
observation remains the durable source. The existing .NET direct outbox path is
not enabled by this composition. Memory is coupled operationally to the supervisor,
not to the language model's decision to speak.

Startup-probe suppression on the Gateway is unchanged. This explicitly authorized
supervisor-owned memory maintenance is not an automatic Graphiti startup probe.
It can invoke the existing Graphiti extraction/embedding models.

## Attribution repair, 2026-09-28

Three facts associated with Helena were checked against their original episodes:
an engineering suggestion about Filomena's Reply behavior became a request for
Helena to use Reply; a subjectless chatbot comparison became a personal
characterization; and a description of the conversational system became a property
of its speaker. Only these reviewed facts were expired. No episode/entity was
deleted and no substitute biography was invented.

Private backup contains complete prior edge properties, original episodes, hashes,
review reasons and the applied expiration timestamps. A read-only recall afterward
returned five instead of eight active facts. Repair does not certify all other
facts or solve extraction quality universally. The generic get/delete-by-ID MCP
surface did not select the namespace used by exact recall; the repair therefore
used explicit, exact-namespace storage maintenance with content/expiry preconditions.

Undo requires checking the current edge still matches the reviewed UUID, namespace,
content and this repair's expiration timestamp, then restoring only the previous
expiration property. Never clear a later invalidation or replay source episodes to
recreate a deleted fact.

## Verification

Local supervisor/memory tests: 24 passed, including startup failure, exact-scope
selection, stop/restart, visible degradation and drain-before-terminal-status.
Plugin unit suite: 295 passed, 15 opt-in tests skipped. Typecheck, emitted JS build
and compiled module imports passed. Tests used controlled inputs, not live writes.

The separately authorized live activation reached conversation `run_started`,
passed Transport and 180 Graphiti checks, discovered retained pending work and
confirmed progress in persisted recovery receipts. Backlog remains pending; full
ingestion and semantic-quality acceptance are not claimed.

## Operations

Use the canonical launcher with private host/memory configuration and the reviewed
application binary. The earlier operational launcher delegates to this one and
requires the existing private memory configuration. It has a private rollback copy.
Do not start the former standalone memory worker alongside this supervisor.

For shutdown, create the current run's `stop.request`; wait for memory `stopped`,
worker exit and runner-lock removal. Preserve receipt/recovery databases and
conversation journals. Rolling back the launcher does not undo accepted Graphiti
work or the separately audited attribution repair.

## Member-DM enrollment reconciliation (2026-09-30)

The same supervisor optionally owns `tools/member-dm-enrollment.mjs`. Explicit
host policy is stored at `<hostFile>.member-dms.json`, outside project configuration:
`version: member-dm-enrollment-policy:v1`, `enabled`, exact `accountId`, `groupJid`,
opaque `sessionRef`, absolute `observationConfigFile` and `nativeConfigFile`.
Credentials remain in the existing ACL-protected observation configuration; the
policy contains no credential. No policy means no automatic enrollment control.

Every five seconds, a non-overlapping reconciliation reads current policy and
authenticated loopback status. Enabling requires the exact account/group/session,
supported valid effective snapshot, matching armed isolation proof, inactive legacy
dispatch, armed capture and healthy journal. The only write is the existing exact
group enrollment endpoint, followed by authenticated read-back. Actual DM admission
still resolves fresh membership, lease, isolation and reader evidence. No old
epochs, leases or child scopes are restored. No proactive messages are sent.

Set `enabled:false` in the policy to revoke automatic enrollment and disable new
enrollment on the next successful pass. A one-off API disable while policy remains
true is not durable revocation. Removing policy stops reconciliation but does not
undo an already-enabled setting. Neither operation drains existing DM workers;
use the normal host shutdown/reconciliation procedure for that. Policy and secret
files must remain writable only by trusted deployment principals.

The companion drains with the supervisor and records sanitized state in
`member-dm-enrollment-status.json`. Failed checks remain `not_ready`; it never arms
capture, refreshes approval pins, changes routes, replays messages or repairs
journals. A Gateway restart that invalidates other transport prerequisites still
requires their approved recovery procedure; enrollment restoration is not a claim
of unattended recovery from every Gateway failure.

Validation: 43 local tests passed across enrollment, supervisor and memory lifecycle,
including a real loopback HTTP test of owner replacement, authenticated restoration,
policy revocation and stop. Syntax checks passed. No model calls or real messages
were needed for these tests.

Activation: run 23 drained cleanly. Run 24 restored enrollment but correctly stopped
at `preflight_stale`. The existing reviewed `prepare-simple-turn-revision.mjs`
procedure revalidated the same route/epoch, valid snapshot and all ten excluded or
inactive paths, backed up prior pins, and refreshed only transport approval pins
and capability-evidence hash (`pre-refresh-resume-24`). Run 25 passed Transport and
180 Graphiti read-only checks but then failed MCP connection initialization with
`TimeoutException`, before `run_started`, with zero send-boundary attempts. Run 26
failed the pinned revision check: the channel producer changed while snapshot,
isolation instance, registry generation and epoch remained the same. It was not
treated as automatic permission. The existing reviewed procedure revalidated the
new producer and backed up pins again (`pre-refresh-resume-26`) before run 27.
Enrollment remained enabled. Brain/project and conversation journals were not
replaced. No live Gateway restart or end-to-end DM response test was performed in
this correction. Preflight completion alone is not proof of a running worker.

Final activation check: run 27 emitted `run_started` and subsequently `interaction`;
the supervisor was `running`. The enrollment companion is part of this running
supervisor, not a detached one-shot enable command. Synthetic owner replacement is
covered offline; no claim is made that a real Gateway restart was exercised live.
