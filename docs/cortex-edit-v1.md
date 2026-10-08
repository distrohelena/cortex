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
| Editorial decisions (read-only, see below) | `cortex.edit.decisions.v1`, `cortex.edit.decisions.v2` |

The complete derived timeline (`cortex.edit.resolved.v1`) was removed with the decisions compiler: renders now go
through the `helengine.video.edit.v1` document, so no producer or consumer of a resolved timeline remained.

Portable JSON Schema 2020-12 files are embedded in `Cortex.Video.dll`, copied beside its build output under `Editing/Schemas`, and installed under `plugins/video/schemas`. They describe field types, discriminants and structural constraints. The C# media-domain validator checks relationships and arithmetic that portable schemas cannot express on their own.

`EditingJson.Parse` / `Serialize` dispatch and round-trip the document models with snake_case fields, explicit enum codes, preserved extension evidence/annotations and null unmeasured quality values. Unknown fields outside declared extension data, duplicate JSON properties and invalid scalar types fail explicitly.

`EditingContractValidator.Validate` returns existing `FlowIssue` values with English codes/messages and JSON-pointer paths. Generated documents require an `EditingValidationContext` with the authored script and its actual fingerprint. Caller-supplied media fingerprints are compared when available; structural validation does not prove a media file was analysed or a quality metric was measured.

Validation preserves ordered required beats, distinguishes pending recordings/alignment, checks concrete media intervals, and rejects stale or mixed resolution snapshots. A failed provider search is not a recording requirement. Missing positive-weight quality metrics cannot become a fabricated full `take_score`. Intent validation permits declarations of future/nonexistent recordings.

The [seven-beat example](examples/cortex-edit-v1/script.json) and [other fixture snapshots](examples/cortex-edit-v1/README.md) are illustrative: their scores, media and alignment are fictional. Loading them in the editor demonstrates the document contract, not media discovery.

The existing **Suggest Takes** and **Render Cuts** still use their one-video `TakeEditPlan`. This increment supplies contracts, validation and an intent-input node. The single-video Resolve Clips increment accepts video/transcript wires and a script file/source ID. It preserves segment timing, uses local exact matching and the existing Jev decision transport for semantic/intent matching, and emits plan/tasks with pending review. Unsupported source coverage and alignment remain pending; provider failures do not become missing-clip recording tasks. Pickup acceptance and renderer adaptation remain subsequent nodes. The transport is covered with fixtures; no private footage was searched in this increment.


## Initial recording requests

`script_requests_recording` identifies an author-requested recording before any media search. It is distinct from `no_suitable_clip_found`, which requires completed search evidence, and `script_requires_exact_wording`, which requires an exact authored script.

The consumer-owned `DepoisDoSlogan.CortexPlugin` reuses these models and existing `FlowNodeHandler`/`IFlowCompletion` contracts for `Video Brief -> Draft Script -> Recording Plan`. It creates `cortex.edit.v1` intent and an initial recording-task bundle. The generation nodes live in the optional **Depois do Slogan** library, not in the generic Cortex runtime. The generated plan is editable editorial intent; scene duration targets are estimates, and no resolved timeline is produced. See the consumer README for installation and execution.

## Editorial decision plans

`cortex.edit.decisions.v1` adds a proposal for every script beat before recordings are selected. The Video library supplies strict models, schema validation, human overrides, review states, focus resolution and image-motion rendering. Proposals bind the current script fingerprint; source images bind their own SHA-256 and natural dimensions. Human overrides survive compatible replanning and become explicit conflicts if a scene's role changes.

The initial image catalog supports cut/fade entrance, none/zoom motion, linear/ease-in-out curves and center/point/text-region focus. Text-region motion requires a region resolved on the current image. Caption and overlay layers remain separate from image transforms.

Depois do Slogan no longer writes decision plans: its AI proposes edits in the `helengine.video` vocabulary and stores
them in the video edit (see `helengine-media-composition.md`). The decisions models, schemas, merge and validation stay
because Flux Studio reads plans stored before that change to migrate those videos into the video edit once. Nothing in
Cortex compiles decisions any more (`HelengineCompositionCompiler` was removed); `EditorialCompositionCatalogValidation`,
`EditorialDecisionFlowTypes`, `EditorialDecisionCompiler`, the ffmpeg image-motion renderer and `EditorialDecisionExport`
have no caller outside tests and go with the decisions types in a later cleanup.
