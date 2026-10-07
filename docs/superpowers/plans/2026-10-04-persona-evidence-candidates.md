# Persona evidence candidates implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans, task by task, with TDD for new behavior. Execution remains native, without subagents or an independent reviewer. Begin implementation only after Helena reviews this plan.

**Status:** All three tasks completed offline on 2026-10-04; see [acceptance report](../../persona-evidence-candidates-report.md). No real inference, activation or commit.

**Goal:** Admit source-bound behavioral evidence candidates offline without creating or changing an approved persona revision.

**Architecture:** Add pure request/candidate contracts and deterministic admission inside optional `Cortex.Personas`. Reuse the real Cortex inference authority only in test composition with a controlled adapter. Candidate artifacts are separate from revision artifacts; no production orchestrator or publication path is added.

**Tech Stack:** Existing .NET 10, System.Text.Json, SHA-256, xUnit v3, Cortex authority/fake-adapter fixtures and Node architecture checks. No package or assembly addition.

**Spec:** [Approved candidate extraction specification](../specs/2026-10-04-persona-evidence-candidates-design.md).

## Global constraints

- Work in the current Cortex checkout; preserve the uncommitted relocation and concurrent work. No Damma/Filo writes, branch switch, new worktree, automatic commit or push.
- `Cortex.Personas` stays dependency-free. Do not modify its project references, add a Core/provider dependency, or wire Filo to it.
- Preserve the existing revision codec/compiler and JSON version 1; the source-baseline smoke hash remains `a466eb8e6e95ed859ced9ddc7c841f898b89e856b292a9f6f024190219698481`.
- Initial fixture limits: source 16,384 UTF-8 bytes; returned response 32,768 UTF-8 bytes; 8 candidates; each text field 2,048 UTF-16 code units. These are host-owned resource bounds, not timeouts.
- Apply the code-unit limit to candidate text/quotes and request metadata, not the full source context. The source context has its separate byte limit; otherwise the required 16,384-byte boundary test would be unreachable for an ASCII source. This clarifies the specification's response-field limit without widening a response allowance.
- First acceptance uses only a synthetic corpus and controlled adapter responses. No real/local/paid inference, fallback, retry, Graphiti, memory writing, service/database activation or transport.
- Candidates remain `pending_review`. No trait consolidation, `PersonaSpec`/`BehaviorPattern` generation, revision conversion, publication, persona activation or runtime attachment.
- Source access authorization belongs to the host. Structural scope/hash/attribution validation is not an authorization grant or proof of semantic truth.

## Review focus

1. Same source text but another subject, run or config is substituted during reopening: Task 2 checks every request binding, not merely the source hash.
2. Duplicate JSON or an unknown approval field conceals promotion: Task 2 rejects it in responses and artifacts.
3. Unicode normalization or surrogate replacement makes a false quotation look valid: Tasks 1 and 2 require valid Unicode and exact ordinal correspondence.
4. Mutable input/returned collections alter previously validated proof: Tasks 1 and 2 test independent snapshots and bytes.
5. One invalid item disappears into partial/empty success after an adapter failure: Tasks 2 and 3 assert whole-batch rejection and distinct failure outcomes.

## File boundaries and exact contracts

Create three production files under `src/Cortex.Personas/Candidates/`, namespace `Cortex.Personas`:

- `PersonaCandidateContracts.cs`: request/response/artifact data records and immutable validated wrappers.
- `PersonaCandidateRequestAdmission.cs`: pure admission of the host-supplied request snapshot.
- `PersonaCandidateCodec.cs`: strict response admission plus artifact decoding against an expected request.

Create tests under `tests/Cortex.Personas.Tests/Candidates/`: `PersonaCandidateFixtures.cs`, `PersonaCandidateRequestTests.cs`, `PersonaCandidateCodecTests.cs`, `PersonaCandidateHostTests.cs`, `PersonaCandidateSmokeTests.cs`. Namespace remains `Cortex.Personas.Tests`; fixtures are test-only.

Reuse `PersonaScope`, `PersonaSource`, `TextLocation`, `EvidenceConfidence`, `EvidenceMethod` without changing them. Define:

```csharp
public sealed record PersonaExtractionLimits(
    int MaxSourceBytes, int MaxResponseBytes, int MaxCandidates, int MaxTextCodeUnits);
public sealed record PersonaAttributedPassage(
    string PassageId, TextLocation Location, string SubjectRef, string Role);
public sealed record PersonaExtractionRequest(
    PersonaScope Scope, PersonaSource Source, string SubjectRef,
    IReadOnlyList<PersonaAttributedPassage> Passages,
    string RequestId, string RunId, string ConfigurationRevision,
    EvidenceMethod Method, string PromptRevision, PersonaExtractionLimits Limits);
public sealed record PersonaCandidateDraft(
    string PassageId, string Quotation, string ObservedContext, string ObservedBehavior,
    string Interpretation, IReadOnlyList<string> Alternatives, EvidenceConfidence Confidence);
public sealed record PersonaCandidateResponse(
    int ContractVersion, IReadOnlyList<PersonaCandidateDraft> Candidates);
public sealed record PersonaEvidenceCandidate(
    string CandidateId, string PassageId, TextLocation Location, string SubjectRef, string Role,
    string Quotation, string ObservedContext, string ObservedBehavior,
    string Interpretation, IReadOnlyList<string> Alternatives, EvidenceConfidence Confidence);
public sealed record PersonaCandidateBatch(
    string ArtifactKind, int ContractVersion, string ReviewStatus, string RequestSha256,
    PersonaExtractionRequest Request, IReadOnlyList<PersonaEvidenceCandidate> Candidates);
```

`ValidatedPersonaExtractionRequest` and `ValidatedPersonaCandidateBatch` are sealed classes with internal constructors, private retained byte snapshots and fresh decoded data on access. Request exposes `Snapshot` and `RequestSha256`; batch exposes `Batch`, `Bytes` as a copied `ReadOnlyMemory<byte>` and `ArtifactSha256`. Neither is an authorization grant or convertible to `ValidatedPersonaRevision`.

Exact entrypoints:

```csharp
ValidatedPersonaExtractionRequest PersonaCandidateRequestAdmission.Admit(PersonaExtractionRequest request)
ValidatedPersonaCandidateBatch PersonaCandidateCodec.AdmitResponse(
    ValidatedPersonaExtractionRequest request, string responseJson)
ValidatedPersonaCandidateBatch PersonaCandidateCodec.Decode(
    ReadOnlyMemory<byte> bytes, string expectedSha256,
    ValidatedPersonaExtractionRequest expectedRequest)
```

Use camelCase JSON, string enums with integer values disallowed, duplicate/unmapped properties disallowed and required constructor fields enforced, as in the existing codec. Candidate `confidence` is a required object with three nullable notes; unknown confidence is represented by null notes, not invented probabilities. The extraction response permits only `contractVersion` and `candidates`; it contains no method, scope, approval or routing fields.

## Task 1: Admit independent source and attribution snapshots

**Files:** Create contracts/admission files and request tests/fixtures above. **Consumes:** Existing source/scope/location types. **Produces:** The validated request used by Tasks 2 and 3.

- [x] Write request tests first. `AdmitsExactSourceAndTargetPassages` asserts the literal fixture source, target subject, source kind/hash/origin, location and role are retained. `RejectsInvalidRequest` covers missing identities/method/prompt revision, source hash mismatch, undefined kind, `GeneratedDraft`, wrong-subject/empty/duplicate/overlapping passages, invalid offsets and malformed Unicode. Assert `InvalidDataException`, not a default request.
- [x] Run `dotnet test tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --no-restore --filter FullyQualifiedName~PersonaCandidateRequestTests`. Initially expect missing-feature compile diagnostics for the new types; add only minimal contract declarations if needed, then observe assertion failures against the unimplemented admission before writing its body. Do not introduce deliberate syntax errors or a tautological implementation.
- [x] Implement `Admit`: validate identities and method metadata, positive limits, strict Unicode and byte sizes; verify the source SHA-256; resolve every passage with existing `TextLocation.Extract`. Compare overlap in UTF-16 positions even when passages use different offset units. Empty passage lists are allowed for a source with no attributed behavior; wrong-subject anchors are not silently filtered out. Every admitted quotation must fit the text limit.
- [x] Add `RequestLimitBoundariesAreEnforced` with literal 16,384/16,385 source-byte cases and 2,048/2,049 passage-code-unit cases. Include multibyte text and a surrogate-pair boundary. `InvalidLimitsAreRejected` tests zero/negative values. Observe RED before completing those checks.
- [x] Add `RequestSnapshotCannotBeMutated`: mutate the original passage list and a returned snapshot list; a fresh snapshot and `RequestSha256` remain unchanged. Observe RED before adding defensive copies. Compute the request fingerprint over a fixed serialized snapshot containing all request fields, not just source text.
- [x] Run the request tests and the whole persona project. Expected: all new cases and all 85 existing cases pass without skips. No public API consumes providers, filesystem or runtime here.

## Task 2: Validate responses and preserve a distinct candidate artifact

**Files:** Create codec and codec tests; extend contract wrappers as needed. **Consumes:** Task 1's admitted snapshot/fingerprint. **Produces:** Immutable `pending_review` candidate bytes, reopening only against the expected admitted request.

- [x] Write `ValidCandidateRetainsHostProvenance` and `ValidEmptyIsNotFailure`. Use independently written response JSON and exact literal quotations. Assert `ArtifactKind == "persona_evidence_candidates"`, version `1`, `ReviewStatus == "pending_review"`, original request/hash/subject, nullable confidence, interpretation/alternatives, and copied trusted location/role. First candidate ID is `candidate:<requestId>:1`; subsequent IDs use one-based invariant-culture ordinals.
- [x] Run `dotnet test tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --no-restore --filter FullyQualifiedName~PersonaCandidateCodecTests`; observe missing API/unimplemented-behavior failures, then implement only response admission needed by those tests.
- [x] Add parameterized `RejectsInvalidResponse`: malformed JSON, missing/null/extra/duplicate fields, wrong types/version, unknown passage ID, wrong/normalized quotation, empty fields/alternatives, invalid Unicode and exact duplicate candidates. Two nonidentical interpretations remain separate hypotheses. An exact duplicate means every draft field has the same structural value, including alternative order and confidence notes; no semantic similarity/dedupe engine is introduced.
- [x] Add `ResponseLimitBoundariesAreEnforced`: 32,768/32,769 returned UTF-8 bytes, 8/9 candidates, 2,048/2,049 code units in each text field, including confidence notes and alternatives. Use valid JSON padding for byte tests and distinct drafts for count tests so each failure has one cause. `InvalidItemRejectsWholeBatch` combines a valid and invalid item and asserts failure, never a shortened or empty result. Observe RED, then add those checks.
- [x] Implement artifact creation only after full response validation. Embed Task 1's snapshot/fingerprint; fill IDs, location, role and subject from admitted data. Do not generate persona evidence approval, patterns or revisions. Raw candidate strings remain untrusted interpretations even after structural validation.
- [x] Write `ArtifactReopensAgainstExactRequest`, `TamperedArtifactIsRejected`, and `DifferentRequestBindingIsRejected`. Use mutations of scope, source identity/kind/origin/hash/rights, subject, passages/roles, request/run/configuration, method/prompt revision and limits. A copied fingerprint string does not override changed embedded fields. Unsupported kind/version, changed review status/IDs/location/quotation and duplicate/extra artifact fields also fail.
- [x] Implement `Decode`: verify expected artifact hash first, strict artifact schema/kind/version/state, complete expected request snapshot equality and fingerprint, then revalidate every candidate against its trusted passage and deterministic ID. Rebuild from the expected admitted data, not authority claimed in artifact fields. Byte hash validity alone is insufficient.
- [x] Add `CandidateSnapshotsAndBytesAreIndependent` and `CandidateArtifactIsNotPersonaRevision`. Mutate original response-related collections, returned batch data and exposed byte copies; reopening still yields unchanged data. Supplying candidate bytes plus their correct hash to existing `PersonaRevisionCodec.Decode` must fail. Observe RED for new behavior, then complete defensive copies. Do not change the existing revision codec to make this test pass.
- [x] Run the codec tests and whole persona suite. Expected: all new cases and the original 85 pass; frozen revision smoke hash unchanged. No candidate acceptance is described as semantic/editorial approval.

## Task 3: Prove the offline flow through existing host authority

**Files:** Create host/smoke tests and finish fixtures; update `docs/personas-offline.md`; create `docs/persona-evidence-candidates-report.md` only after acceptance. No production composition file or sender is added.

**Consumes:** Tasks 1/2 entrypoints and existing `HostInferenceAuthority`, `HostInferenceProfile`, `FlowCompletionRequest`, `InferenceCorrelation` and test-only recording adapters. **Produces:** Scripted end-to-end contract evidence and a report with limits, not a live extraction benchmark.

- [x] Add a test-only composition that admits a request before building a completion request. Bind through the existing project grant; pin the fixture provider/model, extraction step, response schema and output validator. Pass source/eligible-passage context as Inputs data, not System authority. Use fixed extraction instructions and schema revision; record requested model separately from observed model, which is unknown under the existing adapter contract. Do not use `ValidatedCompletion` retries/fallback or modify Cortex.Core.
- [x] Write `OneValidRequestMakesOneControlledInvocation`, `InvalidInputNeverInvokesAdapter`, `WrongScopeOrRevokedGrantIsDenied`, `ProfileMismatchIsDenied`, and `InjectedSourceCannotChangeAuthority`. Assertions cover the real authority outcome, unchanged routing/System/profile, and the resulting candidate batch; the fake only replaces the external effect.
- [x] Write `FailureAndCancellationNeverBecomeEmpty`: transport failure, cancellation before invocation, cancellation after invocation and invalid output retain their existing distinct stages/unknown outcomes. Assert zero calls before denial/cancellation, one on admitted attempts, no retries, no candidate success on failure, and unknown usage/cost rather than measured zero. Observe RED for missing fixture composition, then complete that test-only composition.
- [x] Add eight explicit smoke episodes with manually annotated target passages and scripted responses: `contextual-support`, `public-stranger-contrast`, `listening-without-advice`, `technical-help`, `own-error`, `ambiguous-intent`, `other-speaker-only`, `embedded-instruction`. The other-speaker source has no eligible target anchors and returns a valid empty list. Ambiguity keeps alternatives, not a permanent trait. Record case ID, request/source/artifact hashes, candidate count and `semanticQuality = not_evaluated` in test output. Never derive expected observations from the implementation under test.
- [x] Add `RepeatedOriginDoesNotAssertIndependentSupport`: two admitted synthetic source records share an origin and outputs retain that origin; no independence score or global dedupe claim is created. Preserve the existing origin/compiler regressions rather than redesigning retrieval.
- [x] Run all offline acceptance commands below, inspect their real results, and self-review the diff for authority, accidental conversions/publication, partial success, mutable proof and hidden operational calls. No independent reviewer or subagent session is authorized.
- [x] Document the new pure admission APIs, example pending-review artifact, exact commands/counts, scripted versus semantic coverage, byte-limit meaning, and remaining real-model extraction/approval/persistence/UI gaps. Preserve the historical relocation report; do not rewrite its old test counts as current acceptance.

## Offline acceptance commands

Use the installed SDK and local NuGet cache only. Results belong to this plan's ignored workspace; no private source corpus is read.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore Personas.slnx --source 'C:\Users\Helena\.nuget\packages' -p:NuGetAudit=false
dotnet build Personas.slnx --no-restore
dotnet test tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj --no-restore --logger 'trx;LogFileName=candidates.trx' --results-directory .superpowers/sdd/2026-10-04-persona-evidence-candidates/results
dotnet test tests/Cortex.Core.Tests/Cortex.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~HostInferenceAuthorityTests|FullyQualifiedName~InstructionContextTests'
node --test tests/architecture/persona-module.test.mjs tests/architecture/cortex-foundation.test.mjs tests/architecture/repository-split.test.mjs
$personaFiles = @(rg --files src/Cortex.Personas tests/Cortex.Personas.Tests -g '*.cs' | Where-Object { $_ -notmatch '[\\/](bin|obj)[\\/]' })
dotnet format Personas.slnx --verify-no-changes --no-restore --include $personaFiles
git diff --check
```

Expected: build zero warnings/errors; all persona cases, including the original 85 and new tests, pass with zero skips; 21 delimited authority/context cases and 15 architecture cases remain green; scoped formatting/whitespace pass. Report the actual expanded test count, not a forecast. Fix only failures caused by this slice; identify unrelated failures without overriding another agent's changes.

## Review and execution boundary

This plan carries the approved design into three locally testable deliverables. It does not authorize a real-model extraction run or personality activation. After written-plan approval, execute all three tasks natively without intermediate permission handoffs for resolvable local issues. Keep changes uncommitted unless Helena separately requests a commit.
