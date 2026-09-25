# Native memory ingestion V1 (local implementation)

This slice is **not deployed**. It does not reactivate Filomena, start SpiceDB,
change credentials, or backfill chat history. Inbound writing is disabled unless
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
work requires an operator-reviewed exact lookup/reconciliation extension, not
blind resend or deletion of receipts.

The .NET outbox retries independently of the conversation, in a single cancellable
maintenance task rather than awaited inside the brain/polling path. Up to 32 pending
items are visited per pass, round-robin, at most once per 30 seconds of host polling.
Shutdown cancels and drains this task before closing the private store. Backend
failure is visible and does not rerun an already executed response.
Pending files survive restart; OS file ownership prevents concurrent host writers.
No automatic retry is performed by plugin startup or GET status.

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
