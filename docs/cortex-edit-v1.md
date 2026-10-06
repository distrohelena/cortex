# Cortex editorial documents

The optional **Video** plugin now includes **Edit Script** (`editScript`). It loads an authored `cortex.edit.v1` JSON and validates the editorial intent without accessing its declared footage, calling a model or rendering media.

```text
Edit Script
   ├─ script      typed execution intent
   ├─ scriptJson  complete original JSON text
   └─ scriptRef   file path, revision and raw-byte SHA-256
```

Set **Script file** to the local JSON path. Files must be UTF-8, with an optional UTF-8 BOM, and at most 4 MiB. A generated plan is not an authored script and is rejected by this input node. All authored speech and overlay text retain their language.

`script` contains all semantic intent fields in the existing flow types. Arbitrary annotation `metadata` remains in the lossless `scriptJson` output and the original file rather than becoming executable graph fields. `scriptRef.path` is absolute; relative media paths are anchored to that file's directory, not the editor process's working directory. The hash covers original file bytes, including a BOM when present.

## Versioned files and schemas

| Document | Schema ID |
|---|---|
| Editorial intent | `cortex.edit.v1` |
| Candidate decisions and pending requirements | `cortex.edit.plan.v1` |
| Recording tasks | `cortex.edit.recording_tasks.v1` |
| Complete derived timeline | `cortex.edit.resolved.v1` |

Portable JSON Schema 2020-12 files are embedded in `Cortex.Video.dll`, copied beside its build output under `Editing/Schemas`, and installed under `plugins/video/schemas`. They describe field types, discriminants and structural constraints. The C# media-domain validator checks relationships and arithmetic that portable schemas cannot express on their own.

`EditingJson.Parse` / `Serialize` dispatch and round-trip the four document models with snake_case fields, explicit enum codes, preserved extension evidence/annotations and null unmeasured quality values. Unknown fields outside declared extension data, duplicate JSON properties and invalid scalar types fail explicitly.

`EditingContractValidator.Validate` returns existing `FlowIssue` values with English codes/messages and JSON-pointer paths. Generated documents require an `EditingValidationContext` with the authored script and its actual fingerprint. Complete timelines also require their current plan/tasks and byte references. Caller-supplied media fingerprints are compared when available; structural validation does not prove a media file was analysed or a quality metric was measured.

Validation preserves ordered required beats, distinguishes pending recordings/alignment, checks concrete media intervals and audio/video synchronization, and rejects stale or mixed resolution snapshots. A failed provider search is not a recording requirement. Missing positive-weight quality metrics cannot become a fabricated full `take_score`. Intent validation permits declarations of future/nonexistent recordings.

The [seven-beat example](examples/cortex-edit-v1/script.json) and [other fixture snapshots](examples/cortex-edit-v1/README.md) are illustrative: their scores, media and alignment are fictional. Loading them in the editor demonstrates the document contract, not media discovery.

The existing **Suggest Takes** and **Render Cuts** still use their one-video `TakeEditPlan`. This increment supplies contracts, validation and an intent-input node. Matching real footage against beats, generating recording tasks, accepting pickups and adapting a resolved edit to a renderer remain subsequent nodes.
