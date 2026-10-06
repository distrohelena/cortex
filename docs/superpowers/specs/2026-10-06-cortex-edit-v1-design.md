# Cortex edit v1 — editorial document contract

Status: **draft for review**. This document defines the first contract increment. It does not implement media discovery, matching, pickup ingestion, a new interface, or Resolve export.

## Purpose and scope

An author describes the video they want to make, including a phrase or meaning to find in existing footage and what to record when suitable footage is missing. The Cortex graph resolves that request into inspectable choices and recording tasks. A timeline is derived after the required editorial choices and durations are known.

The documents belong to the optional media functionality already hosted by `Cortex.Video`. The generic graph format remains v3. Editorial document schemas are separate from graph definitions. All keys, identifiers, enum values and generated diagnostic/selection text use English. Author scripts, titles, transcripts and overlays retain their authored language.

The four artifacts are:

| File | `schema` | Responsibility |
|---|---|---|
| `script.json` | `cortex.edit.v1` | Authoritative editorial intent, sources and ordered beats. |
| `edit_plan.json` | `cortex.edit.plan.v1` | Per-beat media choices, candidates, evidence and unresolved requirements; no global timeline. |
| `recording_tasks.json` | `cortex.edit.recording_tasks.v1` | Explicit recording requests and their resolution state. |
| `resolved_edit_plan.json` | `cortex.edit.resolved.v1` | Ordered timeline with concrete media intervals and durations. |

The intermediate plan gets a distinct schema. A partial list of clips must not be called a resolved edit. Intermediate plans retain every beat in script order, including missing beats. A recording task never becomes a silent black clip or disappears when surrounding clips are selected.

## Common conventions

- `schema` is the application-format identifier, not the JSON Schema `$schema` keyword. External field names use `snake_case`.
- `revision` is a positive integer on the authored script. Generated documents carry `project_id` and `script_ref` with `path`, `revision` and SHA-256 of the script's actual UTF-8 file bytes. A changed script makes prior generated decisions stale, including when the author forgets to increment the revision.
- Generated artifacts from one resolution pass share a `resolution_id`. Artifacts from different passes cannot be combined as a current bundle. The resolved document pins the consumed plan and recording-task file bytes with `edit_plan_ref` and `recording_tasks_ref`; editing those inputs makes an existing timeline stale and requires deriving it again.
- `script_ref.path` resolves relative to the generated document. Source/media paths resolve relative to the referenced script's directory, rather than the shell's working directory. The four files can therefore remain together or live in a nested run directory.
- The first source types use local filesystem paths. Explicit absolute local paths are permitted; project-relative paths are preferred for portability. Remote fetching is not implied by a source declaration.
- Source, beat, candidate, media and task IDs must be unique within their respective collections. References must resolve to the same project/revision. A media ID identifies one file, while a source ID can identify a pool of files.
- Media snapshots identify the file with `source_id`, `path`, `sha256`, type and measured properties. A renamed/replaced file is not silently treated as the previously analysed footage. Accepted clip ranges must fit the measured duration of that media snapshot.
- All media and timeline times are finite seconds, with zero at the beginning of their respective file/sequence and exclusive end points: `[in, out)`. Moving clips have positive durations. A still image has a display duration, not fabricated `source_in`/`source_out` video timecodes.
- `metadata` is optional author/producer annotation. It does not change matching or rendering behavior. The documentation examples declare `metadata.example: true`; their media, scores and timecodes are fictional fixtures, not analysis results.

## 1. Authored intent: `cortex.edit.v1`

`project` supplies `id`, `title`, `format`, `aspect_ratio` and optional `target_duration_sec`. The duration is an editorial goal, not permission to truncate speech or drop beats. Format and aspect ratio must agree. `recording_defaults` may specify authored speaker/framing/orientation/delivery instructions; a beat can override them.

`sources` declares video files, video pools and image assets. A pool's path and speaker label are discovery instructions, not evidence that files have already been found or that a speaker was identified by vision. Supporting the declaration in this contract does not imply the existing one-video nodes already enumerate folders.

`beats` is the authoritative ordered list; every listed beat is required in v1. Each beat has an ID, optional purpose, audio intent and visual intent. Enter/exit operations are explicit; `cut` is the first supported transition. Known future visual directives can be preserved as intent, but a consumer must report unsupported directives instead of silently substituting a different result.

Audio kinds:

- `none`: intentional silence, not a missing recording. The beat needs an independently specified visual/display duration.
- `find_clip`: find speech using `search`, rank according to `selection`, and apply the declared fallback if the completed search finds no suitable clip.
- `record`: create a recording task from an authored script/brief and recording instructions. Submitting a pickup does not by itself confirm its wording or suitability.

`audio.require_exact_wording: true` on a `record` intent makes the authored script an exact-wording requirement. A semantic search with a recording fallback retains its semantic requirement unless the author explicitly requires exact wording for the recording.

Matching modes:

| `search.match` | Query | Meaning |
|---|---|---|
| `exact` | `search.text` | The requested words must occur in order. Case, whitespace, Unicode normalization and punctuation may be normalized; synonyms, missing/extra words inside the requested phrase and paraphrases do not count as exact wording. |
| `semantic` | `search.meaning` | Preserve the requested proposition, including negations, qualifiers and factual meaning, while allowing different wording. |
| `intent` | `search.meaning` | Find an utterance that serves the requested editorial role, such as expressing disbelief. Evidence must disclose whether this is supported by text, audio or visual analysis. |

`search.speaker` constrains the declared speaker; optional `source_id` narrows the search to one source. `meaning` alone is not an exact-wording request. Matching a phrase inside a large transcript segment does not grant word-level timing: candidates state `timing_granularity`, and unavailable alignment remains an explicit requirement.

`selection.strategy: best_take` describes the desired selection. `max_duration_sec` is a bound on an eligible clip, not permission to invent a shorter excerpt. `prefer` contains soft preferences; it is separate from hard requirements. A `score` weight map contains nonnegative finite weights summing to 1. Supported metric names and actual evaluator capabilities must be validated before using that policy.

An optional `fallback.type: record` supplies `script` or an editorial `brief`. When there is no authored fallback script, use the requested meaning as a brief; do not silently label newly generated wording as the author's exact script.

Visual intent preserves the supplied asset/speaker/graphic/title-card directives, enter/exit behavior, focus and overlays. A `spoken_phrase` trigger remains a request until real alignment locates it. An OCR/crop target or unsupported graphic is a visual requirement, not a reason to ask for another voice recording.

## 2. Media decisions: `cortex.edit.plan.v1`

The plan contains `media` snapshots and one result for every authored beat, in the same order. It references the script rather than rewriting it. Changes to dialogue/order belong in the script; choices of takes can be edited in the plan.

Beat status distinguishes `matched`, `needs_recording`, `needs_media`, `needs_alignment`, `needs_review`, `blocked` and `stale`. Per-role decisions keep audio and visual requirements separate. `matched` means eligible media was found, not human approval or proof of unmeasured quality. A proposed selection records `selected_candidate_id`, who selected it, and its review status. Human overrides retain their provenance rather than rewriting the model's scores.

A candidate records:

- `id`, `media_id`, `source_in`, `source_out`, the actual transcript, declared speaker and timing granularity;
- the requested match mode/query and its match evidence;
- measured metric values with their method/basis, and explicit unavailable metrics/preferences;
- selection/rejection explanations and the resulting rank within that beat.

Scores are dimensionless evaluator outputs, not automatically calibrated probabilities. A `semantic_score` of 0.94 must not be presented as proof that the edit is 94% correct. The existing Jev-based operation evaluates transcripts; it cannot establish clear audio, eye contact, natural vocal performance, background noise or image quality from text alone.

Do not assign zero to an unmeasured metric or quietly redistribute its weight. `take_score` stays null when the requested weighted policy cannot be evaluated fully. A text-only recommendation can still expose its measured components and ranking basis, with pending review.

`no_suitable_clip_found` is only valid after a successful, completed search. Provider failures, unreadable sources, unsupported capabilities and missing alignment are explicit failures/pending requirements; they are not evidence that the author needs to record the line again.

This plan has no global `start`/`end`. A later beat does not receive invented destination timecodes while an earlier recording's duration is unknown.

## 3. Recording requests: `cortex.edit.recording_tasks.v1`

Each task has a stable project-scoped ID, `beat_id`, `reason`, `status`, script/brief, optional authored `target_duration_sec` and recording instructions. Tasks reference the same script revision as the plan, which references their IDs.

First reasons include `no_suitable_clip_found` and `script_requires_exact_wording`. A duration target is an instruction; an estimate must be explicitly marked as an estimate, and is not a measured recording duration.

Task states are `needs_recording`, `recorded`, `needs_review`, `resolved` and `stale`. Marking a file as recorded means it was submitted. It becomes resolved only when accepted media intervals satisfy its script/meaning and requirements. `resolved_by` records the media ID, actual source range, accepted evidence and selection provenance. Pickups re-enter the same media-resolution path, rather than a second framework.

Rerunning the resolver updates the same task IDs. It does not duplicate pending tasks or silently reuse a resolved task whose script/media fingerprint changed.

## 4. Derived timeline: `cortex.edit.resolved.v1`

A resolved timeline is produced once all required beat media and durations are settled. Every required beat remains represented in the same order. Source references point to the selected concrete files; stills/procedural visuals remain explicitly typed. Audio/video from the same speaking clip use synchronized source intervals unless the author explicitly requests another visual.

Each item has `beat_id`, sequence `start`/`end`, typed audio/visual bindings and the evidence/provenance of the chosen speech. The end of one item equals the start of the next. Its duration comes from chosen media or an authored display duration, not from a model inventing timecodes. Deviations from target duration are reported; they are not fixed by padding or deleting material implicitly.

`render_requirements` identifies the visual/transition/style capabilities needed to execute the plan. A resolved editorial timeline does not imply that every renderer supports it. The renderer/exporter must check its capabilities and preserve/report unsupported features. Spoken overlay triggers must be aligned before a renderer that requires their timing can execute them; supplied illustrative word times are not a substitute for real alignment.

A preview that deliberately omits unresolved beats must be explicitly marked partial with the omitted IDs. It must not be emitted under `cortex.edit.resolved.v1` as a complete final edit. No black/silent placeholders are created implicitly.

## Compatibility with the current slice

The current Suggest Takes output and Render Cuts input remain the existing `TakeEditPlan` format. They describe selecting speech from one already-transcribed video; they are not reinterpreted as authored intent. The new formats coexist until an explicit adapter is implemented.

An adapter to Render Cuts can only accept a complete, compatible, one-video sequence with linked audio and direct cuts. It must reject multiple source files, images, graphics, unsupported aspect/framing changes, transitions and overlays instead of dropping those requirements. The current renderer preserves the source resolution/frame rate and reports measured duration, including frame/AAC quantization differences.

The graph/compiler/runtime, plugin loader, existing SystemOne client and offline tools remain the integration points. Media-specific contracts do not move into the generic graph core. No second scheduler, agent framework or new UI is required to establish this contract.

## Concrete review artifacts

[The JSON fixtures for the four artifact kinds](../../examples/cortex-edit-v1/README.md) demonstrate the provided seven-beat script, an incomplete plan, two recording tasks, their pickup resolutions, and a complete derived timeline. The `after-pickups` folder supplies the later plan/task snapshots consumed by the resolved example. They use English fields and generated explanations while preserving Portuguese authored content.

## First implementation increment after contract approval

Implement the versioned models and structural/cross-reference validation in the existing optional Video module, with portable JSON schemas and round-trip fixtures. Validation checks discriminants, IDs/references, modes, weights, durations, missing tasks, supplied input/media fingerprints and complete timeline requirements. Intent validation does not scan its declared folders or require that future recordings already exist. Register one **Edit Script** file-input node so the intent can be loaded and inspected in the existing Cortex editor, using the existing flow types for its ports and media-domain validation for document rules.

The media resolver is a subsequent increment: it must demonstrate one successful match and one genuine recording requirement using real footage before expanding to pools, automatic pickup ingestion or a Resolve adapter. This spec does not authorize implementing that whole pipeline at once.
