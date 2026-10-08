# Cortex edit v1 contract fixtures

These are **illustrative design examples**, not output from a real media search. The footage, source snapshots, hashes, scores, OCR rectangle, pickup acceptance and word-alignment times are fictional. Each document marks `metadata.example: true`. Do not treat these files as an executable edit or evidence that Cortex has already found or analysed Helena's recordings.

- [script.json](script.json): the supplied seven-beat editorial intent, with English field names and Portuguese authored text. The introduction includes the proposed weighted score policy; unmeasured qualities are not filled in later.
- [edit_plan.json](edit_plan.json): every beat remains in order. Four example spoken clips are proposed; `difference` and `outro` explicitly need recordings. Spoken overlay alignment and human review remain pending. There is no global timeline.
- [recording_tasks.json](recording_tasks.json): two outstanding recording tasks. A semantic fallback retains semantic matching; the authored outro requires exact wording.
- [after-pickups/edit_plan.json](after-pickups/edit_plan.json) and [after-pickups/recording_tasks.json](after-pickups/recording_tasks.json): a later hypothetical state where both pickups have been recorded, from the same `resolution_id`.

The SHA-256 in `script_ref` is the actual digest of this example's `script.json`, so the cross-document reference can be checked. The all-zero media hashes are deliberately fictional fixture values; real resolver output must fingerprint the real media bytes.

The root plan/tasks show the state before pickups; the `after-pickups` pair shows the later state. They are two snapshots of the same artifact kinds. Do not combine the earlier pending tasks with the later plan as if they were one resolution pass. (The resolved-timeline document kind, `cortex.edit.resolved.v1`, was removed; renders go through the `helengine.video.edit.v1` document.)

[Normative draft and first implementation scope](../../superpowers/specs/2026-10-06-cortex-edit-v1-design.md).
