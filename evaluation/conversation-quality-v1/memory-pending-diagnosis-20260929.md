# Six unresolved memory submissions: read-only diagnosis

Date: 2026-09-29. No resubmission, receipt mutation, Graphiti/SpiceDB data write, restart, or model call by this investigation. Ordinary authorized service activity is separate.

## Observed state

The active group's receipt store contains 494 persisted and six failed records. All six are `submission_unconfirmed` with `retryDisposition=reconcile`. Their source times range from 2026-09-29T00:36:31Z to 02:12:15Z. These are source-message timestamps, not measured submission-attempt timestamps.

Read-only `get_episodes` against the configured Graphiti returned 549 episodes with the expected namespace. None of the six deterministic episode UUIDs appeared. This is not proof that an external submission never occurred, that no processing is pending, or that a resubmission is safe. No receipt was changed to persisted, safe, or success.

The other binding in this shared store has seven persisted and 35 queued records. It is not the selected group's six-item backlog and was not modified or expanded into this investigation.

Containers report healthy. MCP tools/list exposes the expected add_memory fields, including episode_uuid, reference_time, bound_entities, extraction_body and custom_extraction_instructions. Current contract compatibility does not reconstruct historical request results.

## Causal findings

`plugins/openclaw-memory-graphiti/native-ingest.ts::submitValidated` claims the receipt before calling the recorder. Authorization errors receive a specific safe-retry code; every other thrown exception becomes `failed/submission_unconfirmed/reconcile`. No original error class, HTTP outcome or effect-phase detail is retained in that receipt.

`NativeIngestService.reconcile` only advances to persisted after `confirmInbound` proves the episode. Missing episodes leave the existing receipt unchanged. `native-memory-recovery.ts::reconcilePending` does not submit an already claimed uncertain request again; it checks confirmation and reports `ingestion_not_yet_confirmed`. Thus this is a stable unresolved-submission state, not evidence of ongoing extraction progress.

`message-lifecycle.ts::confirmInbound` checks exact episode ID, namespace, name and body before writing authorization relationships and acknowledging persistence. This audit did not call that method, because its successful branch writes SpiceDB; it used only the underlying episode read.

The historical external failure's exact cause remains unknown. Backend log review found no retained error/failed lines explaining these six receipts. Generic substring occurrences of `404` were timestamp digits, not HTTP failures; they were rejected as evidence. A stricter HTTP-status scan found three 400 lines but no per-receipt correlation, so they are not assigned as the cause. No claim of stale MCP session or model failure is justified from this audit.

## Next minimal correction, not implemented here

Preserve bounded, sanitized submission diagnostics distinguishing authorization denial, explicit pre-acceptance rejection, accepted work, and uncertain transport failure. Maintain fail-closed retry semantics; an error or missing episode must not become automatic retry permission.

For these existing six records, recovery requires authoritative backend outcome/reconciliation evidence or a separately reviewed replay procedure with real backend idempotency guarantees. Do not clear `claimed`, rewrite retryDisposition to safe, delete receipts, or blindly submit the same messages again to clear the counter.

## Evidence and verification

Disposable helper: `C:/Users/Helena/filomena-self-reference-tests/audit-pending-memory-20260929.mjs`. SQLite opened readOnly; MCP calls limited to initialization, get_episodes, tools/list and session close. Final execution exited 0. Early helper-only failures (Windows ESM URL and incorrect close method name) were corrected; neither executed a write tool. No product code change or integration-test pass claimed.

Codebase-memory MCP was unavailable (Transport closed); relevant source was read directly. Report omits message contents, complete transport IDs, credentials and facts. No commit or push.
