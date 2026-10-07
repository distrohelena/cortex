namespace Cortex.Personas;

public sealed class PersonaCandidateRequestAdmission
{
    public ValidatedPersonaExtractionRequest Admit(PersonaExtractionRequest request)
    {
        Validate(request);
        var bytes = PersonaCandidateJson.Write(request);
        // Validate the independent snapshot as well as caller-owned data.
        Validate(PersonaCandidateJson.Read<PersonaExtractionRequest>(bytes));
        return new(bytes);
    }

    private static void Validate(PersonaExtractionRequest r)
    {
        if (r is null || r.Scope is null || r.Source is null || r.Method is null
            || r.Passages is null || r.Limits is null)
            throw new InvalidDataException("invalid_extraction_request");
        var l = r.Limits;
        if (l.MaxSourceBytes <= 0 || l.MaxResponseBytes <= 0 || l.MaxCandidates <= 0 || l.MaxTextCodeUnits <= 0)
            throw new InvalidDataException("invalid_extraction_limits");
        foreach (var value in new[] { r.Scope.ProjectId, r.Scope.WorldId, r.Scope.CharacterId,
            r.SubjectRef, r.RequestId, r.RunId, r.ConfigurationRevision, r.PromptRevision,
            r.Source.SourceId, r.Source.OriginKey, r.Method.Kind, r.Method.Version, r.Method.AuthorRef })
            PersonaCandidateJson.Text(value, l.MaxTextCodeUnits);
        foreach (var value in new[] { r.Source.RightsNote, r.Method.Provider, r.Method.Model, r.Method.ConfigurationRevision })
            PersonaCandidateJson.Text(value, l.MaxTextCodeUnits, optional: true);
        if (!Enum.IsDefined(r.Source.SourceKind) || r.Source.SourceKind == PersonaSourceKind.GeneratedDraft)
            throw new InvalidDataException("unsupported_behavior_source_kind");
        if (PersonaCandidateJson.ByteCount(r.Source.Text) > l.MaxSourceBytes)
            throw new InvalidDataException("source_byte_limit_exceeded");
        if (!PersonaCandidateJson.IsHash(r.Source.Sha256)
            || !string.Equals(r.Source.Sha256, PersonaCandidateJson.Hash(PersonaCandidateJson.Utf8.GetBytes(r.Source.Text)),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("source_hash_mismatch");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var intervals = new List<(int Start, int End)>();
        foreach (var p in r.Passages)
        {
            if (p is null || p.Location is null) throw new InvalidDataException("invalid_attributed_passage");
            PersonaCandidateJson.Text(p.PassageId, l.MaxTextCodeUnits);
            PersonaCandidateJson.Text(p.SubjectRef, l.MaxTextCodeUnits);
            PersonaCandidateJson.Text(p.Role, l.MaxTextCodeUnits);
            if (!ids.Add(p.PassageId)) throw new InvalidDataException("duplicate_passage_id");
            if (p.SubjectRef != r.SubjectRef) throw new InvalidDataException("passage_subject_mismatch");
            PersonaCandidateJson.Text(p.Location.Extract(r.Source.Text), l.MaxTextCodeUnits);
            var start = Utf16Offset(r.Source.Text, p.Location.Start, p.Location.Unit);
            var end = Utf16Offset(r.Source.Text, p.Location.End, p.Location.Unit);
            if (intervals.Any(i => start < i.End && i.Start < end))
                throw new InvalidDataException("overlapping_attributed_passages");
            intervals.Add((start, end));
        }
    }

    private static int Utf16Offset(string text, int offset, TextOffsetUnit unit)
    {
        if (unit == TextOffsetUnit.Utf16) return offset;
        var result = 0;
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count++ == offset) break;
            result += rune.Utf16SequenceLength;
        }
        return result;
    }
}
