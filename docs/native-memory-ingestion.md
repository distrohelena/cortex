# Native memory ingestion V1 (local implementation)

The Gateway/plugin and .NET rollout is **not deployed**. A separate, explicitly
authorized memory-only operational process is now exercising the compiled slice
(see the operational evidence below). It does not reactivate Filomena or change
credentials. Normal native-host inbound writing is disabled unless
the deployment explicitly configures both ends below. Existing read-only Graphiti
composition and brain prompts are unchanged.

## What changed

The native host stages eligible human inbound before identity enrichment,
participation and cursor advancement. Silence does not discard memory intent.
An independent outbox talks to the plugin's narrow HTTP surface; it never invokes
legacy message hooks. Observation Journal and outbound journals are unchanged.

The plugin uses the existing normalization, deterministic episode/participant IDs,
social extraction instructions and recorder, with an explicit submission/receipt
contract. The native path checks existing SpiceDB permission with full consistency;
it does **not** create its own group membership.

## Host-only configuration (future operational window)

Plugin opt-in: `OPENCLAW_NATIVE_MEMORY_INGEST_HOST_FILE` points to an absolute,
host-private JSON file. Neither project JSON nor plugin content selects this file.

```json
{
  "version": "native-memory-host:v1",
  "storePath": "<absolute private directory>/native-memory.sqlite",
  "capabilityEnv": "FILOMENA_NATIVE_MEMORY_CAPABILITY",
  "bindings": [{
    "bindingRef": "<opaque reviewed binding>",
    "conversation": {
      "channel": "whatsapp",
      "accountId": "<trusted account>",
      "nativeId": "<trusted exact conversation>",
      "kind": "group",
      "namespace": "<derived namespace>"
    },
    "notBefore": "<explicit ISO start time>",
    "selfActorIds": ["<trusted canonical self>"]
  }]
}
```

The plugin requires Node's built-in `node:sqlite` (validated with Node 24.19.0).
The store parent must already exist with restricted ACLs; SQLite stores raw
provenance and message content. Treat the DB, journal sidecars and .NET outbox as
private records, not ordinary logs. No automatic retention deletion is implemented.

The operational native host document may contain `memoryIngest` with:

- `endpoint`: loopback HTTP, exact `/plugins/memory-graphiti/native-ingest/v1` path.
- `gatewayTokenEnvironmentVariable`, `capabilityEnvironmentVariable`: separate
  existing deployment-controlled credentials; values never go into the project.
- `bindingRef`, exact `session`, `conversationNamespace` matching the host binding.
- `epoch`, `afterSequence`, `notBefore`: explicit start boundary, not history replay.
- `storeDirectory`: absolute existing private directory for this authority/window.

Missing configuration means disabled. Supplying writer configuration to the Shadow
entrypoint is rejected. A different epoch/authority requires explicit reconciliation
and an appropriately scoped store, not silent reuse. Snapshot messages never flow
through staging. Self, non-text, suppressed and pre-boundary events are not ingested.

## Protocol and status

Both routes use Gateway authentication plus `x-native-memory-capability`. No tool
is registered for a model to call. Maximum ingestion body: 64 KiB. No redirects.

- POST base path: strict `native-memory-ingest:v1` request.
- GET `/status?bindingRef=...&requestRef=...`: local receipt only, no backend write.
- POST same status URL: explicit episode/authorization reconciliation.

Receipt meanings:

| State | Meaning |
|---|---|
| pending | Durable intent; may include an interrupted submission requiring reconciliation |
| queued | Backend accepted submission; persistence not yet proved |
| persisted | Exact episode UUID, namespace and provenance observed; fragment authorization awaited |
| failed | Explicit failure, with controlled diagnostic and retry disposition |

`persisted` does not assert that useful facts were extracted. A stored episode can
legitimately yield zero facts. Recall and fact provenance are checked separately.
An absent/invalid response is not a valid empty result.

The plugin claim is transactional across connections. Duplicate identical IDs
reuse receipts; conflicting content or cross-account collisions fail closed.
Known authorization failure **before submission** allows safe retry. A network
failure/crash that could follow submission requires reconciliation, never automatic
re-extraction. The current bounded lookup checks the latest 100 episodes; absence
from that window is unresolved, not proof the write was lost. Older unresolved
work now widens that read-only window geometrically up to 12,800 episodes. Hitting
the bound remains unresolved, never authority to resend or delete receipts.

The .NET outbox retries independently of the conversation, in a single cancellable
maintenance task rather than awaited inside the brain/polling path. Up to 32 pending
items are visited per pass, round-robin, at most once per 30 seconds of host polling.
Shutdown cancels and drains this task before closing the private store. Backend
failure is visible and does not rerun an already executed response.
Pending files survive restart; OS file ownership prevents concurrent host writers.

## Recurring group memory recovery (local, not deployed)

Recovery is ongoing work, not a one-time migration. The plugin can recover retained
group messages on service start and periodically without the .NET conversation
runtime running. It has no sender or conversation-execution dependency. It never
consumes/removes pending ingress or replays an old message through the brain.

The same private host config may explicitly add:

```json
{
  "recovery": {
    "sourceDatabasePath": "<absolute native state>/openclaw.sqlite",
    "storePath": "<private directory>/memory-recovery.sqlite",
    "intervalMs": 30000
  }
}
```

Each authorized group binding also needs `historyNotBefore` (an explicit original
provider-time lower bound). This does not weaken `notBefore` on live HTTP ingestion.
Only the internal recovery worker may submit historical requests. Account, group,
namespace, canonical speaker and the existing SpiceDB permission checks still apply.
No wildcard group discovery or permission grants are performed. Deployment must
enumerate the trusted group bindings; DMs are not included by this feature.

For LID senders, `historyIdentityDirectory` selects the **existing account-owned**
Baileys mapping directory. Only the specific `lid-mapping-<lid>_reverse.json` and
reciprocal `lid-mapping-<pn>.json` are read, never session keys. Both directions
must agree. Missing mappings remain unresolved; disagreement fails closed.
Names, message text and a project's claims never resolve identity.

The source adapter reads retained native journal and ingress rows in a SQLite
read-only snapshot, bound to the native hashed account/scope and exact group. It
verifies journal record digests. Neither Observation capture nor its cursor changes.
It discovers by native ID on every pass, so late arrivals with an older timestamp
are not skipped. Duplicates share the existing deterministic episode receipts;
conflicting content for the same ID stops that group rather than overwriting it.

The recovery store durably holds discovered messages before submission. Its
per-group cursor is a **contiguous confirmed discovery ordinal**, not the last
message timestamp and not the response cursor. `queued` does not advance it.
Round-robin bounded retry allows later items to persist without jumping the cursor
over unconfirmed earlier items. Already staged work remains reconcilable if the
source goes offline. Stop waits for the active pass before closing the stores.

GET `/plugins/memory-graphiti/native-ingest/v1/recovery?bindingRef=<opaque>` uses the
same Gateway authentication and host capability. It returns counts, cursor,
diagnostics and coverage gaps, without message contents or triggering a scan.
`OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES=true` suppresses the recovery
worker as well during provisioning; status exposes `suppressed=true`.

### Coverage limitations (do not claim full history)

- This source recovers **locally retained** history, not an on-demand WhatsApp
  history fetch. Stale appends discarded before retention, deleted payloads and
  periods without capture cannot be reconstructed from these stores. A trusted
  provider-history retention/feed is still needed for those gaps. Status therefore
  stays `coverage=partial`, even when `pending=0`.
- Self/account-sent messages are counted separately, not reclassified as human
  memory or attributed automatically to Filomena. Non-text/unsupported media and
  unresolved actor identities are explicit gaps, not invented content.
- Source scans above 50,000 rows per selected source/group fail visibly rather than
  truncating history. Confirmation beyond the 12,800-episode window also remains
  unresolved. These bounds require a paginated/exact source/API extension before
  claiming support for arbitrarily large histories.
- This change does not start SpiceDB, grant permissions, enable live ingestion,
  reactivate Filomena, or prove successful extraction/recall against a live backend.

Local verification covers restart, receipt dedupe, queued versus persisted,
late old arrivals, source outage, immutable scope, starvation, journal corruption,
self/quote separation, authenticated status and recurring startup/stop composition.

The 2026-09-25 opt-in **read-only** source audit of the configured group found 360
distinct eligible text messages after trusted LID/PN resolution, 123 self rows and
102 unsupported/non-text rows. Source counters may overlap between stores; they
are not a claim about the total WhatsApp conversation. No Graphiti write was made.
Payload-retention gaps remain visible. This audit did not verify every group or
claim that the messages have been indexed.
GET status never performs a retry. The explicitly configured recovery service
does schedule recurring passes; provisioning suppression prevents its startup.

## Operational evidence: 2026-09-25 (local date)

This is a memory-only operational window, not a conversational deployment or a
claim that all history has been indexed. Private artifacts and SQLite receipts
are in the existing restricted host operations directory, accessible only to
Helena and SYSTEM. No tokens, transport IDs or message bodies belong in this doc.

- Started the existing PostgreSQL and SpiceDB containers, without replacing data,
  schema, credentials or grants. Health and existing `access`/`contribute` grants
  passed for both explicitly configured group namespaces.
- The live source audit found 398 eligible text messages in group ref `8ef80a2457`
  and 42 in `b8cbf01709`. Those 440 messages were durably staged by the real
  `NativeMemoryRecovery`; this number is **discovered**, not **persisted**.
- One real historical message went through `NativeIngestService` and the real
  recorder. Confirmation proved the exact episode content, namespace, canonical
  participant and authorization. Repetition reused its persisted receipt. Its
  exact person recall was valid empty; that is not a semantic-quality PASS.
- Subsequent samples from both groups returned episode-linked facts and verified
  canonical participant metadata. Nine confirmed samples yielded five linked
  facts. Samples also showed low-value presence questions being retained as facts;
  extraction usefulness/filtering is not declared complete by this evidence.
- The operational process calls the **existing** `composeNativeMemoryIngest`
  service; no second recovery engine, HTTP listener, Gateway startup, sender or
  conversational runtime is started. It uses the same durable receipt/recovery
  stores across restarts. Its temporary operational launcher is not an OS boot
  service and is not a replacement for the final plugin/host deployment.
- A controlled worker stop/drain/restart passed. The PID changed, durable counts
  did not regress, and the controlled persisted receipt remained unchanged.
- At 00:52:49 UTC on September 26, receipts showed 10 persisted and 145 queued;
  remaining staged messages had not yet been submitted. These are a point-in-time
  observation, not final completion counts. The memory-only worker was running;
  the conversation supervisor remained stopped.
- Graphiti extraction uses the configured local Gemma model and local embedding
  model. No Luna conversational generation or WhatsApp outbound was invoked.
- Fresh local verification: 294 plugin tests passed, 15 explicit opt-ins skipped;
  TypeScript typecheck passed. Live writes were performed only through the separate
  operational probe, never the ordinary test suite.

Stop the operational worker by creating `native-memory/worker.stop` in its private
operations directory; it drains the current service pass before closing stores.
Already accepted backend extraction work is independent and can finish after the
worker stops. Preserve receipts and journal files. Restart requires an explicit
operator action; a stop request is not silently removed. No memory deletion is
part of rollback.

Still outstanding: completion/error reconciliation of the actual backlog, final
deployment/start-on-restart wiring, provider history for retention gaps, and
semantic filtering quality. Unsupported media and missing payloads remain explicit
coverage gaps. Do not report memory as complete or reactivate conversational replies
on the strength of these partial results.

## Local verification

Final run, 2026-09-25: plugin 275 passed / 13 skipped (includes the explicit
cross-language test); .NET Llm/App 607 passed / 43 skipped; Core 621 passed /
10 skipped; OpenClaw adapter 175 passed / 0 skipped. Live opt-ins/credentials
were removed from regression subprocesses. TypeScript typecheck, fresh .NET
build, changed-file .NET whitespace verification and `git diff --check` passed.
No dedicated plugin lint command exists; typecheck is not presented as a separate
linter. Author self-review was performed; no independent reviewer was launched.

Unit suites exclude live opt-ins. The cross-language test additionally starts a
temporary loopback server hosting the **real** route/service/recorder with fake
Graphiti and SpiceDB. It runs a .NET test using the real outbox, HTTP adapter and
`GraphitiMemoryReader`, proving identity and provenance across languages.

After building the .NET test project with `-c NativeMemory`:

```powershell
# From plugins/openclaw-memory-graphiti, in a test-only shell:
$env:NATIVE_MEMORY_CROSS_LANGUAGE_TEST = '1'
node node_modules/vitest/vitest.mjs run native-ingest-composition.test.ts
```

Fake extraction does **not** prove social judgment: sarcasm, third-party claims,
corrections and subjective filtering still need a separately authorized model-backed
test against an isolated database. No new personality/profile inference was added.

Six additional controlled source cases (preference, joke, identity claim, third-party
claim, correction, banal message) prove original text/structural speaker/instructions
are preserved; they do not award the extractor a semantic-quality PASS.

### Implementation decisions and costs

- Plugin receipts use native SQLite instead of hand-rolled file-lock recovery:
  requires the supported Node runtime, but supplies crash-safe atomic claims.
- Uncertain remote acceptance never auto-resubmits: some work can require manual
  reconciliation rather than risking duplicate extraction.
- Native authorization checks do not create membership: permissions must be
  provisioned explicitly; unavailable SpiceDB blocks writing.
- GET is read-only; POST status reconciles authorization: operational callers must
  deliberately choose the latter instead of treating status reads as mutations.
- Remote maintenance is one cancellable task: memory is eventually available, not
  guaranteed to be ready for the immediate next generation. Local intent is durable.
- Independent review and real-model selection validation were not silently launched:
  review evidence is weaker than a separate review, and semantic quality remains open.

## Before any live activation

1. Keep Filomena offline while reviewing this slice.
2. Verify SpiceDB is available and the exact agent/namespace permissions exist;
   the previous audit found no listener at the configured port. Do not bypass it.
3. Review private ACLs, exact account/conversation/self binding and start boundary.
4. Authorize a controlled write/recall window separately. Graphiti ingestion can
   invoke its extraction model; this is different from read-only preflight.
5. Prove one inbound -> episode -> correctly scoped fact/valid empty, then replay
   without duplicate extraction or conversational execution. Do not backfill yet.

Rollback: stop the native worker safely, remove only this slice's opt-ins and
restore the prior plugin/host artifacts. Preserve receipts/outbox/DB for
reconciliation. Removing configuration does not undo memories already written;
deletion is a separate reviewed operation. Do not unconditionally reset stores.
