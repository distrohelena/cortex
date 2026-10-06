# Cortex Editorial Contracts Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline execution after plan review. No subagents or independent review are authorized. Steps use checkbox (`- [x]`) tracking.

**Goal:** Implement the approved editorial JSON contracts and load an authored script through one Edit Script node in the existing optional Video plugin.

**Architecture:** Keep document serialization and media-domain validation inside `Cortex.Video.Editing`; reuse Core's flow types, issues, node handlers and runner. Portable JSON schemas describe the stored documents; cross-document rules remain in a small domain validator. The existing TakeEditPlan and Render Cuts interfaces remain unchanged.

**Tech Stack:** .NET 10, System.Text.Json with a separate snake_case serializer, existing FlowType/ObjectType/ArrayType/NullableType, xUnit v3, JSON Schema 2020-12 documents. No new application/project/framework or model/provider dependency.

**Spec:** `docs/superpowers/specs/2026-10-06-cortex-edit-v1-design.md` — approved by the user on 2026-10-06.

## Global Constraints

- Schema IDs: `cortex.edit.v1`, `cortex.edit.plan.v1`, `cortex.edit.recording_tasks.v1`, `cortex.edit.resolved.v1`.
- English external field names, IDs, enum values, node labels and diagnostics; authored Portuguese remains unchanged.
- Every listed beat is required; incomplete plans contain no global timeline. Missing media and recording/alignment requirements remain explicit.
- `take_score` remains null when positive-weight requested metrics are unavailable; no fabricated audiovisual metrics or hidden weight redistribution.
- Source/sequence times are finite seconds with exclusive ends; images have display durations and no moving-media source timecodes.
- Generated documents bind project/script revision, raw-byte hashes and resolution ID; a resolved document pins the actual plan/task inputs it consumed.
- Intent loading does not enumerate footage folders, require future recordings to exist, start tools or access model credentials.
- Preserve the one-video legacy graphs, default-disabled plugin loading, generic graph v3 and shared settings.
- No media resolver, automatic pickup ingestion, new UI, rendering adapter or Resolve integration in this increment.
- Use scoped commits in the current main checkout, consistent with the user's existing workflow; preserve unrelated Personas changes.

## Review Focus

1. An authored script can refer to missing future footage; structural validation/loading must succeed without scanning or treating absence as a completed failed search (tasks 1–3).
2. A human changes an input without changing its revision; raw-byte fingerprints must still mark the previously derived output stale (task 2).
3. A speech clip is found while required word alignment is pending; neither aggregate beat status nor a final timeline can hide that requirement (task 2).
4. Unicode/spaces/BOM in a script file; content and raw-byte hash must remain correct, with a stable origin directory independent of the editor working directory (task 3).
5. Arbitrary author annotation metadata and null quality scores; round trips preserve them without inventing typed execution semantics for annotations (tasks 1, 3).

## Task 1: Models, serialization and portable schemas

**Files:**
- Create `src/Cortex.Video/Editing/EditingDocumentKinds.cs`, `EditingJson.cs`, `EditingDocumentModels.cs`, `EditingIntentModels.cs`, `EditingResolutionModels.cs`, `EditingSchemas.cs`.
- Create `src/Cortex.Video/Editing/Schemas/cortex.edit.v1.schema.json`, `cortex.edit.plan.v1.schema.json`, `cortex.edit.recording_tasks.v1.schema.json`, `cortex.edit.resolved.v1.schema.json` resources.
- Modify `src/Cortex.Video/Cortex.Video.csproj` to embed/copy those schema files, without new package/project dependencies.
- Test `tests/Cortex.Video.Tests/EditingSerializationTests.cs` and `EditingSchemaTests.cs`.

**Interfaces:**
- `EditingDocumentKinds` exposes the four exact schema ID constants.
- `EditingJson.Options` is independent of `VideoNodes.JsonOptions`; snake_case names and explicit null quality results are preserved.
- `EditingJson.Parse(JsonElement value) -> EditingDocument` dispatches only those four schema IDs; `EditingJson.Serialize(EditingDocument document) -> JsonElement` round-trips authored content.
- Four root records: `EditScriptDocument`, `EditPlanDocument`, `RecordingTasksDocument`, `ResolvedEditPlanDocument`, sharing an `EditingDocument` base. Typed headers/references, intent/source/beat/recording and candidate/timeline models use the approved fixture fields. `metadata`, procedural `recipe` and explicitly extensible requirement/evidence payloads may retain JSON data; do not silently discard it.
- `EditingSchemas.Get(string schemaId) -> JsonElement` exposes the embedded portable schema. The resources constrain discriminants, field types, required shape and scalar bounds; arithmetic, media references and cross-file checks belong to task 2.

- [x] Write failing round-trip tests for all six committed fixture snapshots; assert schema/project IDs, seven ordered beats, authored accents, null `take_score`, nested metadata and pickup source ranges survive.
- [x] Add rejection tests for unknown schema IDs, ambiguous/duplicate JSON property names and invalid scalar types; preserve extension data only in explicitly open annotation/recipe/evidence fields.
- [x] Run `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj --filter "FullyQualifiedName~EditingSerializationTests|FullyQualifiedName~EditingSchemaTests"`; observe the missing API/failing assertions.
- [x] Implement the listed models/serializer and four portable schemas. Keep audio/visual discriminant fields explicit; no opaque catch-all replacement for the whole document. Publish strict portable shape for known semantic fields and open shape only for annotations/declared extension payloads.
- [x] Validate fixture shapes and schema syntax using the already available Python `jsonschema` 4.26.0 `Draft202012Validator`; record results under this plan's ignored workspace. No install or runtime dependency is needed. Check malformed variants against both that schema validator and the C# structural checks.
- [x] Run the scoped tests to green and commit only the owned contracts/schemas/tests.

## Task 2: Editorial and cross-document validation

**Files:**
- Create `src/Cortex.Video/Editing/EditingContractValidator.cs`, `EditingValidationContext.cs`, `EditingReferenceValidation.cs`, `EditingTimelineValidation.cs`.
- Test `tests/Cortex.Video.Tests/EditingContractValidationTests.cs` and `EditingBundleValidationTests.cs`.

**Interfaces:**
- `EditingContractValidator.Validate(EditingDocument document, EditingValidationContext? context = null) -> IReadOnlyList<FlowIssue>` returns stable codes, JSON-pointer paths and English messages.
- `EditingContractValidator.ThrowIfInvalid(EditingDocument document, EditingValidationContext? context = null)` fails explicitly using those issues.
- `EditingValidationContext` supplies the authored script, current script reference, optional current plan/task documents and their actual byte digests, plus caller-supplied media fingerprints. It does not discover/probe media or call a provider.
- Reference/timeline helpers are implementation details of the same validator, not a separate execution engine.

- [x] Write failing script tests: duplicate/missing source or beat IDs, unknown asset/source references, inconsistent format/aspect ratio, exact matching without `search.text`, semantic/intent without `search.meaning`, incompatible audio fields, silent beat without display duration, invalid/nonfinite weights and durations. Assert pointer/code, not just an exception.
- [x] Write pending-plan/task tests: every authored beat remains in order; selected IDs exist in that beat's candidates; ranges fit the referenced media; required recording IDs resolve; incomplete plans reject global timeline fields; failed/unavailable search cannot be represented as `no_suitable_clip_found`; missing alignment cannot be cleared by an audio-only match.
- [x] Write metric tests: unavailable positive-weight metrics require null `take_score`; no renormalized fabricated full score; known values remain finite/in range. Zero-weight unavailable metrics do not impose an evaluation requirement.
- [x] Write resolved tests: pending recordings/alignment or missing/duplicated/reordered beats reject completeness; moving ranges have positive in-bounds duration; images reject source timecodes; contiguous timeline durations match their chosen clip/display durations; linked speaker audio/video stay synchronized; authored target duration does not cause implicit padding/truncation.
- [x] Write bundle tests using the actual fixture byte digests: later plan/tasks/resolved IDs agree; the earlier snapshot is rejected when mixed with the later one; changing whitespace/selection/media fingerprint makes an existing derived result stale even with the same script revision. Metadata alone cannot turn fictional/missing media into verified measurements.
- [x] Run the new test classes and observe red; implement the validation rules with existing FlowIssue conventions.
- [x] Run the task 1–2 test classes to green and commit the validator/context/tests. Update fixture wording only if review uncovers a contract contradiction; preserve the approved semantics.

## Task 3: Edit Script node, packaging and real editor inspection

**Files:**
- Create `src/Cortex.Video/Editing/EditScriptReader.cs`, `LoadedEditScript.cs`, `EditingFlowTypes.cs`, `EditScriptNode.cs`.
- Modify `src/Cortex.Video/VideoNodes.cs`, `cortex-video.plugin.json`, `tools/install-cortex-editor.ps1` and the relevant optional-plugin tests.
- Test `tests/Cortex.Video.Tests/EditScriptNodeTests.cs`; reuse `tests/Cortex.Editor.Tests/ColdPluginTests.cs`.
- Add `docs/cortex-edit-v1.md` describing current support and schema locations; mark the design spec's approval/implementation status accurately.

**Interfaces:**
- `EditScriptReader.ReadAsync(string path, CancellationToken token) -> Task<LoadedEditScript>` reads at most 4 MiB, parses/validates only an authored `cortex.edit.v1`, and records absolute source path, revision and SHA-256 of unmodified file bytes. Accept a UTF-8 BOM for parsing while retaining it in the raw-byte digest; reject invalid UTF-8.
- `LoadedEditScript` contains the typed document, original JSON text and `EditingDocumentReference`. Resolve future local paths against the referenced file directory, not the shell working directory.
- `EditScriptNode.Create() -> FlowNodeHandler`: kind `editScript`, display name **Edit Script**, block 1, `Pure`, entry with no inputs and `then`; required string config `path`, label **Script file**.
- Outputs: `script` is a concretely typed execution projection containing the complete semantic intent; `scriptJson` preserves the complete original JSON including arbitrary annotation metadata; `scriptRef` contains path/revision/hash. Annotations are deliberately kept in the lossless original rather than extending Core with an arbitrary-JSON flow type. Document this distinction and preserve all authored semantic fields in the projection.
- `EditingFlowTypes.Script` / `ScriptReference` use the existing flow types. Discriminant/cross-field strictness stays in the media-domain validator; no new Core union/any type is introduced.

- [x] Write failing reader/node tests: real committed script fixture loads and exposes all seven beats and semantic/intent/exact modes; missing raw media does not prevent intent loading; Unicode/space/BOM paths retain content and byte hash; malformed/oversized/wrong-version/generated-plan inputs fail before publishing outputs; cancellation does not publish partial results.
- [x] Test lossless arbitrary metadata in `scriptJson`, semantic projection in `script`, correct `scriptRef`, and the existing kernel compiling/running a one-node entry graph without Whisper/Jev/FFmpeg or credentials.
- [x] Test registration exposes Edit Script plus the five current Video nodes without starting tools; disabled plugin keeps its DLL/dependencies unloaded. Legacy three-node and saved-cut graphs still compile/run.
- [x] Run the tests to observe red, then implement reader/types/node/registration and package the portable schemas with the optional plugin.
- [x] Run `dotnet test tests/Cortex.Video.Tests/Cortex.Video.Tests.csproj`, `dotnet test tests/Cortex.Editor.Tests/Cortex.Editor.Tests.csproj`, and the Depois do Slogan contract tests. Broaden only on a new failure or relevant production change.
- [x] Install a fresh generation preserving shared settings. Open a one-node graph pointing to the authored fixture; validate/run, inspect typed intent and raw JSON/reference outputs in the existing editor. Label examples as illustrative; do not claim media was found or recordings resolved.
- [x] Commit owned changes, report tests/current capabilities and stop at this increment.

## Completion and execution review

This plan ships the standard's storage/validation and an input node. It does not implement the central media resolver yet. The next increment requires real footage with one matching beat and one genuine missing line before expanding its scope.

Execution remains inline in the current checkout under the user's existing preference. A fresh independent review requires the user's separate approval and is not started by this plan. Plan approved; the scoped increment is implemented and verified. Video83, Editor12 and consumer6 tests passed; portable schemas validated with jsonschema4.26.0, and Edit Script ran in the installed editor with seven beats and no external tools.
