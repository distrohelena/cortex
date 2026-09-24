# Unprocessed inbound recovery

## Fixed

Continuous exact-group live operation now stores `native-live-read-cursor.json` in its private journal directory. This records the last successfully handled native cursor, bound to session and pinned project revision, with checksum, write-through flush and atomic replacement.

On startup, the current source snapshot is used for validation, not as a replacement for the saved processed position. Messages arriving during a runtime interruption remain after that cursor and are offered to the existing runtime. Its durable native-ID registry deduplicates already completed work; its context/outbox checks still refuse uncertain execution/send recovery.

The cursor advances only after successful runtime handling, including intentional silence, duplicates and self suppression. It does not mean WhatsApp's visual read receipt and does not depend on whether a reply was sent. Failed execution does not advance this checkpoint.

Missing initial authority, corruption, wrong session/project, cursor regression or changed capture epoch fail explicitly. There is no fallback that silently skips to the latest snapshot. A changed capture epoch still requires explicit reconciliation; this patch does not claim automatic cross-epoch recovery.

## Historical limitation

The group ingress entry received at 13:36:55 on September 24 occurred after the previous capture closed (approximately 13:36:33) and before the next capture started (13:44:02). Its legacy ingress row is completed with a null payload; it is absent from the observation journal. The native session transcript inspected does not contain this new message. The remaining evidence does not establish that this was Andreas's message or retain its content.

This patch cannot reconstruct that historical message. It also does not enable capture automatically after Gateway restart, bypass the approved host arm, or claim to recover messages received while capture was OFF. The current continuous window stays armed while the application is upgraded; stopping capture would again be a separate availability boundary requiring reconciliation.

## Verification and deployment

- Full solution: Core 603 passed/10 skipped; OpenClaw 128 passed; LLM 517 passed/40 skipped. Total 1,248 passed, 50 skipped, zero failures.
- Tests cover saved-position restart, newer snapshot not skipping pending records, invalid session/project/epoch, future cursor, corruption, monotonic writes and missing initial anchor. Existing uncertain-send, duplicate and context-restart regressions remain green.
- Old continuous runtime stopped through its idle-boundary `/quit` control; `run_closed`, zero executions and zero send attempts verified. Capture and isolation were not disarmed.
- New launcher: private `drift-continuous/run-group-live-recovery.ps1`, separate `state/unread-recovery-build-20260924` artifacts. The temporary stop marker was removed after clean shutdown; no message/journal data was removed.
- Transport and all 180 Graphiti preflight checks passed. `pending_inbound_resume` emitted; position zero persisted; `run_started` and thirteen healthy polls verified, no abort. No synthetic message or test reply was sent.

Brain, models, routing, participant roster, native journal and outbound semantics were not changed.
