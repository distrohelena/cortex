# Cortex.Personas: offline evidence and context compilation

`Cortex.Personas` is an optional, dependency-free .NET 10 library for admitting evidence-based persona revisions and compiling situation-specific context. It is not an autonomous personality service, a model adapter, or a replacement for Cortex host authority.

## Ownership and current status

Cortex owns the reusable evidence/revision/compiler code. Filo owns Filomena's character material, authored decisions, sources and eventual application composition. Damma owns documentary investigation; it does not own personality formation.

This slice continues the characterized implementation originally committed under Damma's `personas/` tree. The original tree is historical and remains untouched pending cleanup coordinated by Damma's owner. This port is not a second separately evolving implementation.

Neither `Cortex.Core` nor Filo's production projects references this module. Adding its projects to the solution makes them buildable; it does not activate Filomena or attach a persona to an existing flow.

## Data and contracts

- `PersonaScope` identifies project, world and character for integrity checks. It is **not** an authorization grant or a substitute for memory/document access policy.
- `PersonaSource` retains content, exact UTF-8 SHA-256, source kind, origin and rights note. Copying one origin does not create independent corroboration.
- `PersonaEvidence` keeps the observed context/behavior and passage location separate from interpretation, alternatives, method, qualitative confidence and timestamps. Supersession references are validated locally within the bundle; this is not a persistent revision database.
- `BehaviorPattern` contains conditional tendencies, exceptions and supporting/counter-evidence references. Retrieval uses explicit admitted dimensions, not free-form text similarity. Unknown conditions are not treated as matches, and an uncertain exception prevents selection.
- `PersonaSpec` identifies the revision and selects eligible patterns, values/beliefs and voice guidance. Authorial assertions and evidence-backed assertions have different admission requirements.
- `PersonaRevisionCodec` admits strict contract-version-1 JSON, verifies references/scope/hashes, and produces independent snapshots. `TextLocation` explicitly distinguishes UTF-16 offsets and Unicode scalar offsets.
- `PersonaContextCompiler` produces structured **data**, including uncertainty and provenance. The host independently chooses System instructions, provider/model, permissions and output validation.
- `PersonaCandidateRequestAdmission` admits an independent source/target/passage snapshot before extraction. It validates exact UTF-8 source hashes, attribution, Unicode, offsets, nonoverlap and host-supplied limits; this is integrity, not access authorization.
- `PersonaCandidateCodec` admits strict version-1 extraction responses as immutable `pending_review` artifacts. Exact quotes refer to host-admitted passages; source/scope/subject/method/IDs are not selected by the model. Reopening requires the trusted expected artifact hash and the complete matching admitted request.

Public entrypoints:

```csharp
using Cortex.Personas;

var codec = new PersonaRevisionCodec();
// bundle: PersonaRevisionBundle made from authorized/admitted material.
// situation: PersonaSituation independently admitted by the host.
var revision = codec.Encode(bundle);
var reopened = codec.Decode(revision.Bytes, revision.ArtifactSha256);
var selection = new PersonaContextCompiler().Select(reopened, situation, maxPatterns: 4);
var projection = new PersonaContextCompiler().Compile(reopened, situation, maxPatterns: 4);
// projection.Payload is context data, not a permission to invoke a model or sender.
```

These calls are synchronous, in-memory and perform no external IO. Caller-owned input collections must not be mutated concurrently during admission; after admission, the validated revision exposes independent snapshots.

## Offline evidence candidates

The candidate boundary is implemented separately from approved revisions:

```csharp
// The host separately authorizes access to source material and supplies attribution.
var admitted = new PersonaCandidateRequestAdmission().Admit(extractionRequest);
// responseJson is an untrusted response, not a grant or approved personality.
var candidates = new PersonaCandidateCodec().AdmitResponse(admitted, responseJson);
var reopenedCandidates = new PersonaCandidateCodec().Decode(
    candidates.Bytes, candidates.ArtifactSha256, admitted);
// reopenedCandidates.Batch.ReviewStatus == "pending_review".
// No conversion, compiler input, publication or application attachment is provided.
```

The initial controlled profile uses a 16,384-byte UTF-8 source cap, a 32,768-byte UTF-8 returned-response cap, eight candidates and 2,048 UTF-16 code units per metadata/candidate text field. The full source context has its separate byte cap. Unicode must be valid; quotation matching is ordinal, without normalization or repair. These are explicit host-supplied limits, not hardcoded product policy.

The response byte cap is checked on the original adapter string before parsing. Saved artifact bytes also embed the source/request and may use different JSON escaping; they are not the original response bytes. Reopening checks the artifact hash, expected request, candidate count/field bounds and provenance, rather than inventing a response-size measurement from reserialization. The current API does not impose a separate serialized-artifact byte cap; callers must authorize and bound artifact IO.

A valid empty list is distinct from failure, cancellation or invalid output. Any invalid item rejects the whole batch. Multiple different readings remain hypotheses; exact duplicates are rejected. Exact quotations prove correspondence, not truthful sources or faithful interpretations.

Eight synthetic extraction episodes use a recording adapter through real Cortex authority **in test composition only**. Their full synthetic candidate artifacts and correlation hashes are retained in the ignored experimental TRX, not production logs. No real inference or semantic quality evaluation was performed. See [candidate acceptance report](persona-evidence-candidates-report.md).

## Synthetic example and validation limits

The test corpus represents **Mira**, a fictional fixture, not Filomena or a historical person. It includes a tendency to question a premise when a close person is frustrated privately, an exception for being overwhelmed, counter-evidence, technical help, admitting an error and requested support.

The same topic with a different relationship/audience can select no pattern. Unknown relationship/state also does not fabricate a match. The 12 smoke cases validate selection, provenance and fake-adapter integration with the real Cortex authority; they do not evaluate naturalness, psychological fidelity, live memory or production quality.

The independent frozen `SmokeBundle()` artifact hash is:

```text
a466eb8e6e95ed859ced9ddc7c841f898b89e856b292a9f6f024190219698481
```

## Offline verification

From the Cortex repository, using the installed SDK and local NuGet cache:

```powershell
dotnet restore Personas.slnx --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet build Personas.slnx --no-restore
dotnet test tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --no-restore
dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~HostInferenceAuthorityTests|FullyQualifiedName~InstructionContextTests'
node --test tests/architecture/persona-module.test.mjs tests/architecture/cortex-foundation.test.mjs tests/architecture/repository-split.test.mjs
```

The architecture check explicitly inspects the sibling `../filo/src` checkout read-only to prove no accidental consumer wiring. It fails when that checkout is absent rather than pretending the application boundary was verified. Library compilation and its own test suite do not require Filo or Damma.

## Not implemented or activated here

There is no production corpus extractor runner, embedding/semantic retriever, database adapter, durable publication/activation pointer, autonomous consolidation, feedback UI, life simulation, real model inference or application attachment. Source authorization still belongs to the host. A successful codec admission proves structural integrity, not source truth or permission to access it.

The next separately scoped experiment can evaluate real-model extraction from authorized source passages through the admitted candidate contract. It must measure semantic quality separately, distinguish candidates from approved revisions and must not silently change a running personality.

The [candidate extraction design](superpowers/specs/2026-10-04-persona-evidence-candidates-design.md) and [completed offline plan](superpowers/plans/2026-10-04-persona-evidence-candidates.md) specify the implemented admission boundary. Their approval did not grant permission for real-model extraction or activation.

See [ownership design](superpowers/specs/2026-10-03-persona-module-ownership-design.md), [implementation plan](superpowers/plans/2026-10-03-persona-module-ownership.md), and [relocation report](personas-relocation-report.md).
