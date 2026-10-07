# Persona evidence candidates: offline acceptance

Date: 2026-10-04. Scope: the three approved tasks in the [implementation plan](superpowers/plans/2026-10-04-persona-evidence-candidates.md). Author self-review only; no subagents, independent review, commit or push.

## Result and ownership

Implemented a pure, optional candidate admission boundary in Cortex. Filo owns character material and eventual application composition; Damma remains a separate documentary consumer. Neither application was modified or activated. Existing uncommitted relocation and unrelated changes were preserved on `feat/damma-person-extraction`, starting at `bf62b11525479a92293d23beb50b007e43ca724d`.

This acceptance proves schema, integrity/provenance and controlled host wiring. It does not establish extraction quality, source truth, historical-person fidelity or production isolation/access policy.

## Implemented contracts

Three new library files under `src/Cortex.Personas/Candidates/`, with no new packages, assembly or provider dependencies:

- `PersonaCandidateContracts.cs`: request, draft, response and candidate batch records; independent byte-backed validated wrappers; strict JSON helpers.
- `PersonaCandidateRequestAdmission.cs`: host-supplied scope/source/subject/method/limits admission. Rejects stale source hashes, generated-draft observations, wrong attribution, duplicate/overlapping/invalid passages and malformed Unicode. Empty eligible-passages lists do not guess a speaker.
- `PersonaCandidateCodec.cs`: all-or-nothing response validation and source-bound immutable artifact reopening. Model output cannot supply approval, method, scope, location, subject or candidate IDs. Distinct interpretations remain hypotheses; exact duplicate drafts are rejected.

Entrypoints are `Admit(request)`, `AdmitResponse(admittedRequest, responseJson)` and `Decode(bytes, expectedSha256, expectedRequest)`. Reopening compares the **entire** expected request and fingerprint, not merely source hash or a fingerprint claimed by the artifact. It rechecks quotes and deterministic host-derived IDs/locations/roles.

`ValidatedPersonaCandidateBatch` is not a persona revision. Its kind is `persona_evidence_candidates`, contract version is 1 and state is fixed at `pending_review`. The existing revision codec rejects candidate artifacts even with their correct hashes. There is no publication, consolidation, compiler conversion or runtime attachment.

The wrappers defensively copy retained bytes and expose fresh data/byte copies. Caller-owned collections must not be concurrently mutated during admission, as with the existing revision contract.

## Limits and uncertainty

The fixture profile supplies source UTF-8 bytes 16,384; returned-response UTF-8 bytes 32,768; candidate count 8; individual textual fields 2,048 UTF-16 code units. Boundary and one-beyond cases include metadata, quotations, alternatives, confidence notes, multibyte text and surrogate boundaries. Source context uses its separate byte cap.

The byte limit applies to the original returned response before parsing. It is not a transport-buffer guarantee, and saved artifact size is not response size. Artifacts embed source/request data and may escape Unicode differently. `Decode` checks integrity, expected binding, count/field limits and provenance; it cannot recover a discarded original wire size. A separate artifact IO cap is not introduced in this slice.

Confidence consists of nullable qualitative notes, not calibrated probabilities. Quotation correspondence does not validate interpretation. Successful `[]`, invalid output, backend failure and cancellation remain distinct; no retry, partial result or empty-on-error fallback exists.

## Test-only host composition

`CandidateExtractionFixture` admits inputs before invocation and uses existing `HostInferenceAuthority`, project grants, profiles and correlation. Instructions/schema/provider/model are fixed by test composition, with source and eligible passages passed as data. Requested provider/model are `offline-test` / `fake-v1`; observed model, usage and cost remain **unknown** under the existing string-returning adapter contract.

The profile checks routing/schema constraints, not System text. Fixed System/data separation is guaranteed by the trusted fixture construction; this report does not claim the Core profile independently authorizes source text or enforces arbitrary prompts. Source access remains a host responsibility. Project metadata and hashes are not grants.

One admitted attempt invokes the controlled adapter once. Invalid input, mismatched profile/scope and revoked grants do not invoke it. Tests preserve `CancelledBeforeInvocation`, `CancelledAfterInvocation`, `FailedAfterInvocation` and `InvalidOutput`; uncertain external outcomes are not fabricated as successful empty results.

## Controlled corpus

| Episode | Candidates | Contract result |
| --- | ---: | --- |
| contextual-support | 1 | Quote/context/interpretation retained |
| public-stranger-contrast | 1 | Different contextual reading retained |
| listening-without-advice | 1 | Observation separated from tendency |
| technical-help | 1 | Concrete utterance retained |
| own-error | 1 | Admission of error retained |
| ambiguous-intent | 1 | Alternative interpretation retained |
| other-speaker-only | 0 | Valid empty; no invented target attribution |
| embedded-instruction | 1 | Source imperative remains data, not authority |

All responses were manually scripted. `semanticQuality = not_evaluated` for every episode. Repeated-origin cases retain the same origin; they do not create an independence score or claim global deduplication.

Example **excerpt** from the retained `own-error` artifact, not a standalone importable artifact:

```json
{
  "artifactKind": "persona_evidence_candidates",
  "contractVersion": 1,
  "reviewStatus": "pending_review",
  "candidates": [{
    "candidateId": "candidate:own-error:1",
    "passageId": "p1",
    "location": { "start": 0, "end": 23, "unit": "Utf16" },
    "subjectRef": "person:mira",
    "role": "speaker",
    "quotation": "Mira: I got that wrong.",
    "observedContext": "A correction of her previous statement",
    "observedBehavior": "Admits her own error",
    "interpretation": "May accept correction without deflecting",
    "alternatives": [],
    "confidence": { "transcription": null, "attribution": null, "interpretation": null }
  }]
}
```

The full artifact also contains the admitted request and fingerprint. Recorded source SHA-256: `289df48f19fdf9e95dcc1e3fcbed663d47afdc27c51104dd49f786863b2f5f53`; request fingerprint: `97395beaf83d4985902354c90448a1816b77ac9d7d6ce62be011b1a8fe4d0cb3`; artifact SHA-256: `69056393291c5c62aa60f73e39424052aecb0302a7e879792e90d1df6dd9f255`.

Full synthetic artifacts and per-case hash records are retained in `.superpowers/sdd/2026-10-04-persona-evidence-candidates/results/candidates.trx`, an ignored experimental workspace. No private real-source corpus or ordinary production log was used.

## Verification executed

Commands ran in `C:/dev/cortex`; restore used the local NuGet cache only. Results:

| Check | Result |
| --- | --- |
| `dotnet restore Personas.slnx --source 'C:\Users\Helena\.nuget\packages' -p:NuGetAudit=false` | Successful; no remote feed |
| `dotnet build Personas.slnx --no-restore` | 0 warnings, 0 errors |
| Full `Cortex.Personas.Tests` with TRX logger | 278 passed; 0 failed/skipped |
| Existing persona cases | All 85 retained |
| New request admission cases | 75 passed |
| New codec cases | 100 passed |
| New host/smoke cases | 18 passed, including eight episodes |
| Core authority/context filtered suite | 21 passed; 0 failed/skipped |
| Three Node architecture files named in plan | 15 passed; 0 failed/skipped |
| Scoped `dotnet format --verify-no-changes --no-restore --include <explicit persona .cs paths>` | Clean after formatting only new files |
| Whitespace check | Clean |

TDD: 54 request cases, 89 codec cases and 18 composition cases were observed failing against missing implementations before their bodies were implemented. Initial test analyzer errors were corrected without disabling analyzers. A hand-counted fixture expectation was corrected from 22 to 23 UTF-16 units; the production location was correct. Additional existing-boundary checks were added during self-review.

Self-review found one local defect: `Decode` initially compared a reserialized/escaped response size with the original returned-response quota. A four-candidate Unicode reproduction failed with `response_byte_limit_exceeded` despite valid admitted response bytes. The check was removed from reserialization, preserving the actual admission limit; the regression now passes. This distinction is documented above rather than claiming artifact/wire sizes are equivalent.

The 14 relocated C# file bodies were re-compared with Damma's immutable source commit after namespace/line-ending normalization and are unchanged. The frozen revision smoke hash remains `a466eb8e6e95ed859ced9ddc7c841f898b89e856b292a9f6f024190219698481`. No old revision semantics or Core code changed. The historical relocation report retains its original acceptance counts.

## Remaining scope and effects

No production extractor runner, semantic adjudication, persona approval/consolidation, durable artifact store, feedback UI, autonomous evolution or Filo attachment is implemented. The next useful experiment is small, explicitly authorized real-model extraction with human review of observations versus interpretations; automatic promotion is not implied.

Real/local/paid model invocations: **0**. Graphiti reads/writes: **0**. Memory writes, service/database activation, sender calls and outbound: **0**. Filo/Damma changes: **0**. These are the effects of this work, not a claim that unrelated processes on the machine were inactive.

See [offline module usage](personas-offline.md), [approved specification](superpowers/specs/2026-10-04-persona-evidence-candidates-design.md) and [implementation plan](superpowers/plans/2026-10-04-persona-evidence-candidates.md).
