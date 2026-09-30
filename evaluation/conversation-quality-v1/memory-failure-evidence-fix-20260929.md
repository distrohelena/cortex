# Native memory failure evidence — 2026-09-29

## Scope and result

Local source correction only. No deployment, service restart, live ingestion,
receipt reset, memory rewrite or outbound was performed by this correction.
The six historical `submission_unconfirmed` receipts remain unresolved.

## Changes

- `GraphitiClient.callTool` distinguishes HTTP, JSON-RPC and tool rejection,
  interrupted transport, and invalid/incomplete response using typed errors.
- `NativeIngestService` persists bounded `submissionFailure` and
  `confirmationFailure` evidence independently in the existing JSON receipt.
- Evidence contains only a fixed category and a valid HTTP status when known.
  It does not copy exception messages, payloads, credentials or endpoint URLs.
- Confirmation failure does not erase original submission evidence.
- These classifications do not grant retry authority. Only the existing
  pre-submission authorization failure remains safely retryable.
- Existing receipts without the additive fields remain readable. No SQLite
  migration is required. Unknown errors stay explicitly `unclassified`.

## Backend reconciliation finding

`vendor/graphiti/graphiti_core/graphiti.py::Graphiti.add_episode` checks for an
existing episode UUID and returns early when it exists. This is not a durable
submission receipt and does not prove the absence of an in-flight request when
an episode is absent. The early return also does not compare the submitted
payload with the existing episode.

`vendor/graphiti/mcp_server/src/services/queue_service.py::QueueService` uses
in-memory per-group asyncio queues. Processing errors are logged and the task
is marked done; there is no durable per-request failure receipt in this path.
Therefore an absent episode cannot distinguish not accepted, still processing,
failed extraction or lost queued work. No historical cause has been inferred
from absence alone, and no automatic replay was enabled.

Before replaying the six uncertain items, reconciliation must establish no
remaining in-flight attempt and inspect any partial durable effects under the
exact namespace/identity, or use a backend contract that resolves those states.
This is separate from the diagnostic fix and is not claimed complete here.

## Verification executed

- Red: new tests failed on absent structured evidence and missing transport
  classification before implementation.
- `npx.cmd vitest run`: 305 passed, 15 skipped; 27 files passed, 2 skipped.
  The default configuration excludes external e2e tests.
- `npx.cmd tsc --noEmit`: passed.
- `git diff --check`: passed (line-ending warnings only).
- Tests cover durable reopen, preservation across confirmation failures,
  secret omission, no automatic resubmission, HTTP 400/503, RPC/tool errors,
  interrupted transport and malformed response.

These are offline results, not proof of a deployed fix or repaired live memory.
