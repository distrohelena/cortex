# Persona evidence candidate extraction design

Date: 2026-10-04. Status: specification and implementation plan approved by Helena; pure candidate admission and controlled offline test composition implemented. No real-model extractor or operational activation. See [acceptance report](../../persona-evidence-candidates-report.md).

Plan: [Persona evidence candidates](../plans/2026-10-04-persona-evidence-candidates.md).

## Purpose and scope

Continue the optional `Cortex.Personas` module with the smallest offline boundary that turns an admitted source into reviewable behavioral evidence candidates. Preserve exact passages, attribution constraints, provenance and interpretive uncertainty without silently updating an approved personality.

Helena approved beginning with a synthetic corpus and controlled model responses. The first acceptance proves contract behavior, not the extraction quality of a real model. No paid/local inference, persona publication, Filomena activation, Graphiti activity, database work or Damma modification is authorized by this slice.

Cortex owns reusable contracts and validation. Filo owns character material and eventual application composition. Damma remains a separate documentary consumer; this work must not import its mutable files or alter its domain.

## Existing implementation to reuse

The following are present in the inspected Cortex checkout at `bf62b11`, alongside the uncommitted persona relocation. These are bounded direct-source findings; Cortex is not listed in the available codebase-memory index.

- [PersonaEvidence.cs](../../../src/Cortex.Personas/PersonaEvidence.cs): scope, source/hash/origin, passage offsets, participant roles, observation, interpretation, qualitative confidence and method metadata.
- [PersonaRevisionCodec.cs](../../../src/Cortex.Personas/PersonaRevisionCodec.cs): strict version-1 JSON admission, source hashes, references, scope integrity, Unicode offsets and immutable revision snapshots. It requires a nonempty `ReviewStatus`; it does not authorize publication or establish editorial approval.
- [HostInferenceContracts.cs](../../../src/Cortex.Core/Inference/HostInferenceContracts.cs): host-owned routing/schema constraints, project scope, correlation and inference observations. Usage/cost are unavailable through its current string-returning adapter contract, not implicitly zero.
- [PersonaInferenceFixture.cs](../../../tests/Cortex.Personas.Tests/PersonaInferenceFixture.cs): controlled adapter composition through the actual Cortex authority, without a provider or transport connection.

The [relocation report](../../personas-relocation-report.md) records 85 persona cases, 21 authority/context cases and 15 architecture cases passing on 2026-10-03. Those counts are historical evidence, not tests rerun for this specification.

## Selected approach and alternatives

Use pure candidate admission inside the optional library and an offline test composition using existing host inference authority. Keep the library free of Core/provider/package dependencies. Do not build another inference framework or evaluation workbench.

Directly encoding extraction output as `PersonaRevisionBundle` would be smaller but would conflate structurally valid interpretation with approved personality. A complete extraction/consolidation/retrieval/publication service would address later goals but is unnecessary before this boundary has evidence. Both alternatives are excluded from the first slice.

The proposed flow is:

```text
host admits source, target subject and attributed passages
→ controlled extraction response
→ strict schema and provenance validation
→ immutable candidate batch marked pending_review
```

## Trusted input boundary

A request contains one source snapshot, the exact `PersonaScope`, target subject, run/configuration references and explicit extraction limits. The host supplies source identity, kind, origin, content hash, rights note and method metadata; model output cannot choose or replace them.

The first corpus uses explicitly annotated passages. Each admitted passage has a request-local ID, a nonempty `TextLocation`, and the subject/role attribution supplied by the trusted fixture composition. Attribution is not inferred from a matching display name. Only passages admitted for the requested subject are eligible behavioral anchors. Other source text can supply context but cannot change that identity binding.

This annotation is a deliberate first-slice limitation: fixtures do not prove automatic speaker/entity resolution for arbitrary documents. Sources without sufficient attribution are not silently guessed into this contract. Later ingestion must provide attribution evidence or use a separately designed unresolved-attribution path.

Before any adapter call, admission checks required identities, valid Unicode, exact UTF-8 source hash, supported source kind, valid/nonoverlapping passage locations, unique passage IDs and matching target attribution. `GeneratedDraft` cannot be admitted as observed behavior. Preserve distinct kinds for fictional fixtures, documentary excerpts, authorial decisions and delivered utterances.

The request is copied into an independent snapshot. Authorization to access a source remains the host's responsibility; `PersonaScope`, a hash or a structurally admitted request is not an access grant. The test host uses the existing project grant and explicit fixture scope, not a new authorization system.

## Untrusted extraction response

The response has its own contract version 1 and a `candidates` array. Each candidate proposes an admitted passage ID, an exact quotation of that whole passage, observed context, observed behavior, interpretation and alternatives. Confidence uses the existing nullable qualitative notes, not a calibrated percentage.

Do not ask the model to generate scope, source IDs/hashes, canonical subject IDs, provider/credential choices, approval status, personality revision, persistent timestamps or effect permissions. Additional or duplicate JSON fields, invalid field types, unsupported versions and malformed JSON are rejected.

Offsets and source identity come from the admitted passage, not model-supplied locations. The quotation must equal `TextLocation.Extract(source.Text)` ordinally: no Unicode normalization, quote repair, substring search or fallback to a similar passage. An unknown passage ID or quotation mismatch invalidates the batch.

Require nonempty observation/interpretation fields, preserve optional unknown confidence, and validate alternative strings. Reject exact duplicate candidates within a response rather than silently counting them as extra support. Multiple nonidentical readings remain hypotheses and must not be merged into a certain trait.

Limits are host-owned and checked before parsing/invocation where applicable. For the initial offline profile, pin source size to 16,384 UTF-8 bytes, returned response size to 32,768 UTF-8 bytes, candidate count to 8, and each textual field to 2,048 UTF-16 code units. Admitted passages must fit the quotation-field limit before invocation. Reject ill-formed Unicode rather than replacing it during encoding. These are resource bounds for the fixture profile, not a general product limit or a startup timeout.

Response size refers to the UTF-8 encoding of the string returned by the existing adapter, not a promise about transport buffering before that return. The model cannot enlarge limits. No unlimited recursive interpretation or automatic retry is introduced.

A successfully validated `candidates: []` is a valid empty result. Adapter error, cancellation, invalid schema/provenance or response-limit breach is a failure, never a converted empty list. Reject the entire batch when any candidate is invalid; no partial-success fallback hides defects.

## Candidate artifact and approval separation

Introduce a distinct candidate-batch representation, not another persona revision format. It carries `artifactKind = persona_evidence_candidates`, version 1, fixed `pending_review` state, request/run/configuration references, admitted source/scope/subject metadata, candidate observations and provenance. The host supplies candidate IDs scoped to the request and ordinal, not the model.

The validated wrapper means structural integrity only. Construction must copy mutable inputs and expose independent snapshots. Reopening a saved batch requires its expected artifact hash and the matching admitted source/passage snapshot; a serialized source ID or `pending_review` field is not trusted provenance by itself. Reject changed bytes, mismatched request binding and inconsistent source/passage metadata.

Preserve source hash and origin independently of the extraction-artifact hash. Reproduced sources retain the same declared origin; the slice must not claim that cross-batch deduplication, source independence or durable exactly-once processing has been solved.

There is no implicit conversion to `ValidatedPersonaRevision`, no generator of `PersonaSpec`/`BehaviorPattern`, no call to `PersonaContextCompiler` on candidates, and no publication/activation function. The old revision codec remains unchanged. Its strict schema must reject a candidate artifact supplied as a revision artifact, even if the caller supplies the correct hash of those candidate bytes.

An exact quotation proves location/content correspondence, not that its interpretation is faithful or that the source is true. Semantic errors, unsupported generalizations and ambiguity still require review. This separation is essential to future automatic processing with authorial feedback, not a promise that future consolidation must always be manual.

## Inference composition and records

The first acceptance uses one controlled adapter invocation per valid request through the existing `HostInferenceAuthority`/`HostInferenceProfile` boundary; denied or invalid inputs do not invoke it. Real-model extraction, provider selection, cloud disclosure, retries/fallback and paid-call limits require separate authorization later.

Extraction instructions and response schema belong to trusted composition. Source text is supplied as data, never interpolated as permission or routing policy. Source instructions asking to change project/model, publish personality or send messages cannot alter the host grant or profile.

Retain correlation, extraction/schema/prompt revision, requested provider/model, outcome and artifact/source hashes in experimental records. Do not label requested model identity as observed deployment identity. Usage/cost unavailable from the adapter remains unknown. Successful fake calls prove wiring only; they are not model-quality measurements.

Artifacts are private experimental data, not ordinary production logs. If later source material is private, access/retention and export require host policy. This first slice uses only synthetic material; it does not implement a production artifact store or feedback UI.

## Synthetic corpus and acceptance

Use approximately eight small annotated episodes, reusing fictional Mira where useful: contextual support, the same topic with different relationship/audience, listening without advice, technical help, admitting her own error, ambiguous intent, another person's utterance and an embedded instruction attempting to change the host's behavior. Keep expected passages and readings manually specified.

The following tests define acceptance of the offline implementation:

- Valid extraction retains exact passage, subject, source/origin/hash, observation versus interpretation, alternatives and unknown confidence.
- Wrong quotation, unknown/wrong-subject passage, wrong source/scope, stale hash, empty passage and malformed Unicode fail closed; no repair/search fallback.
- Input/response byte limits, item count and text-field limits are exercised at the boundary and one beyond. Duplicate IDs/candidates and invalid/extra/duplicate JSON fields are rejected.
- Valid empty stays distinguishable from adapter failure, invalid output and cancellation; invalid batches are not partially admitted.
- Host profile/scope mismatch or revoked grant denies invocation. Injected source instructions cannot change routing, System composition or permissions.
- Mutating caller-owned collections or exposed data after validation cannot change the stored batch; encode/decode preserves content/hash and revalidates against the expected request snapshot.
- A candidate batch cannot be used as a persona revision or reach a publication/runtime/sender path through this slice. No trait inference or automatic promotion is introduced.
- Repeated-origin fixtures do not become asserted independent corroboration. Contradictory/ambiguous readings remain visible rather than establishing permanent identity.
- Preserve all 85 existing persona cases, frozen smoke artifact hash, delimited authority/context regressions and optional-module architecture checks. Build and scoped formatting remain valid.

Test reports must distinguish contract/provenance acceptance from semantic extraction review. Scripted responses and synthetic material do not establish live quality, source truth, historical-person fidelity or integration coverage.

## Compatibility and exclusions

No change to existing persona JSON version 1, hashing, offset semantics, compiler selection, approved brain, prompts/models used by Filomena, memory/Graphiti semantics, transport, outbound or Damma's working tree. Keep the original persona files and relocation provenance intact.

No new assembly, service, database, migration, scheduler, feedback UI, generic agent router, embedding store, consolidation engine or automatic personality evolution is part of this slice. A local candidate artifact is not durable workflow recovery. No code, corpus fixtures, inference or operational activation is performed while writing this specification; no commit/push is implied.

The written specification and the three-task implementation plan were approved before implementation. Native execution and the no-subagent/no-independent-review constraint were preserved. Offline acceptance is recorded separately; real-model extraction, consolidation and application attachment remain outside this slice.

## Related records

- [Offline module overview](../../personas-offline.md)
- [Ownership design](2026-10-03-persona-module-ownership-design.md)
- [Relocation implementation plan](../plans/2026-10-03-persona-module-ownership.md)
- [Relocation acceptance](../../personas-relocation-report.md)
