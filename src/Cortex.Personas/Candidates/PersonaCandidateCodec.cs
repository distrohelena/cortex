using System.Globalization;
using System.Text.Json;

namespace Cortex.Personas;

public sealed class PersonaCandidateCodec
{
    public ValidatedPersonaCandidateBatch AdmitResponse(ValidatedPersonaExtractionRequest request, string responseJson)
    {
        if (request is null) throw new InvalidDataException("admitted_request_required");
        var snapshot = request.Snapshot;
        if (PersonaCandidateJson.ByteCount(responseJson) > snapshot.Limits.MaxResponseBytes)
            throw new InvalidDataException("response_byte_limit_exceeded");
        var response = PersonaCandidateJson.Read<PersonaCandidateResponse>(PersonaCandidateJson.Utf8.GetBytes(responseJson));
        if (response.ContractVersion != 1) throw new InvalidDataException("unsupported_candidate_contract_version");
        var candidates = BindCandidates(snapshot, response.Candidates);
        var batch = new PersonaCandidateBatch("persona_evidence_candidates", 1, "pending_review",
            request.RequestSha256, snapshot, candidates);
        return new(PersonaCandidateJson.Write(batch));
    }

    public ValidatedPersonaCandidateBatch Decode(ReadOnlyMemory<byte> bytes, string expectedSha256,
        ValidatedPersonaExtractionRequest expectedRequest)
    {
        if (expectedRequest is null) throw new InvalidDataException("admitted_request_required");
        var copy = bytes.ToArray();
        if (!PersonaCandidateJson.IsHash(expectedSha256)
            || !string.Equals(expectedSha256, PersonaCandidateJson.Hash(copy), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("candidate_artifact_hash_mismatch");
        var batch = PersonaCandidateJson.Read<PersonaCandidateBatch>(copy);
        if (batch.ArtifactKind != "persona_evidence_candidates" || batch.ContractVersion != 1
            || batch.ReviewStatus != "pending_review")
            throw new InvalidDataException("invalid_candidate_artifact_contract");
        var expected = expectedRequest.Snapshot;
        if (batch.Request is null || batch.RequestSha256 != expectedRequest.RequestSha256
            || !Same(batch.Request, expected))
            throw new InvalidDataException("candidate_request_binding_mismatch");
        if (batch.Candidates is null || batch.Candidates.Any(c => c is null))
            throw new InvalidDataException("invalid_candidate_array");
        var drafts = batch.Candidates.Select(c => new PersonaCandidateDraft(c.PassageId, c.Quotation,
            c.ObservedContext, c.ObservedBehavior, c.Interpretation, c.Alternatives, c.Confidence)).ToArray();
        // The response byte quota applies to the original adapter string at admission,
        // not this artifact's different JSON escaping or its embedded source snapshot.
        // Reopening rechecks candidate count/fields/provenance and the trusted artifact hash.
        var bound = BindCandidates(expected, drafts);
        if (!Same(batch.Candidates, bound)) throw new InvalidDataException("candidate_provenance_mismatch");
        return new(copy);
    }

    private static PersonaEvidenceCandidate[] BindCandidates(PersonaExtractionRequest request,
        IReadOnlyList<PersonaCandidateDraft> drafts)
    {
        if (drafts is null) throw new InvalidDataException("invalid_candidate_array");
        if (drafts.Count > request.Limits.MaxCandidates) throw new InvalidDataException("candidate_count_limit_exceeded");
        var passages = request.Passages.ToDictionary(p => p.PassageId, StringComparer.Ordinal);
        var seen = new List<JsonElement>();
        var result = new List<PersonaEvidenceCandidate>();
        foreach (var d in drafts)
        {
            if (d is null || d.Confidence is null || d.Alternatives is null)
                throw new InvalidDataException("invalid_candidate_item");
            foreach (var text in new[] { d.PassageId, d.Quotation, d.ObservedContext, d.ObservedBehavior, d.Interpretation })
                PersonaCandidateJson.Text(text, request.Limits.MaxTextCodeUnits);
            foreach (var text in d.Alternatives) PersonaCandidateJson.Text(text, request.Limits.MaxTextCodeUnits);
            foreach (var text in new[] { d.Confidence.Transcription, d.Confidence.Attribution, d.Confidence.Interpretation })
                PersonaCandidateJson.Text(text, request.Limits.MaxTextCodeUnits, optional: true);
            if (!passages.TryGetValue(d.PassageId, out var p)) throw new InvalidDataException("candidate_passage_unknown");
            if (!string.Equals(d.Quotation, p.Location.Extract(request.Source.Text), StringComparison.Ordinal))
                throw new InvalidDataException("candidate_quotation_mismatch");
            var json = JsonSerializer.SerializeToElement(d, PersonaCandidateJson.Options);
            if (seen.Any(previous => JsonElement.DeepEquals(previous, json)))
                throw new InvalidDataException("duplicate_candidate");
            seen.Add(json);
            var id = "candidate:" + request.RequestId + ":" + (result.Count + 1).ToString(CultureInfo.InvariantCulture);
            result.Add(new(id, p.PassageId, p.Location, p.SubjectRef, p.Role, d.Quotation,
                d.ObservedContext, d.ObservedBehavior, d.Interpretation, d.Alternatives, d.Confidence));
        }
        return result.ToArray();
    }

    private static bool Same<T>(T actual, T expected) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(actual, PersonaCandidateJson.Options),
        JsonSerializer.SerializeToElement(expected, PersonaCandidateJson.Options));
}
