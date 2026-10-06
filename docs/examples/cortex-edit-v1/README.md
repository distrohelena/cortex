# Cortex edit v1 contract fixtures

These are **illustrative design examples**, not output from a real media search. The footage, source snapshots, hashes, scores, OCR rectangle, pickup acceptance and word-alignment times are fictional. Each document marks `metadata.example: true`. Do not treat these files as an executable edit or evidence that Cortex has already found or analysed Helena's recordings.

- [script.json](script.json): the supplied seven-beat editorial intent, with English field names and Portuguese authored text. The introduction includes the proposed weighted score policy; unmeasured qualities are not filled in later.
- [edit_plan.json](edit_plan.json): every beat remains in order. Four example spoken clips are proposed; `difference` and `outro` explicitly need recordings. Spoken overlay alignment and human review remain pending. There is no global timeline.
- [recording_tasks.json](recording_tasks.json): two outstanding recording tasks. A semantic fallback retains semantic matching; the authored outro requires exact wording.
- [resolved_edit_plan.json](resolved_edit_plan.json): a later hypothetical state where both pickups, selection review and spoken-word alignment have been accepted. It references [the updated plan](after-pickups/edit_plan.json) and [updated tasks](after-pickups/recording_tasks.json) from the same `resolution_id`, with their actual fixture-file digests. Every beat is represented, with a 39.32-second derived timeline. Images have display durations rather than fabricated video source timecodes.

The SHA-256 in `script_ref` is the actual digest of this example's `script.json`, so the cross-document reference can be checked. The all-zero media hashes are deliberately fictional fixture values; real resolver output must fingerprint the real media bytes.

The root plan/tasks show the state before pickups; the `after-pickups` pair and resolved document show the later state. They are two snapshots of the same four artifact kinds. Do not combine the earlier pending tasks with the later resolved timeline as if they were one resolution pass.

The resolved example requests multiple files, images, graphics, text, focus/zoom and word alignment. The current **Render Cuts** node cannot execute those requirements. It continues to accept its existing one-video `TakeEditPlan` until a compatible, explicit adapter is implemented.

[Normative draft and first implementation scope](../../superpowers/specs/2026-10-06-cortex-edit-v1-design.md).
