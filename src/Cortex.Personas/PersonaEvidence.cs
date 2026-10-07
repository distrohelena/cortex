namespace Cortex.Personas;

/// <summary>Integrity scope, not an authorization grant.</summary>
public sealed record PersonaScope(string ProjectId, string WorldId, string CharacterId);
public enum PersonaSourceKind { FictionalFixture, DocumentExcerpt, AuthorialDecision, GeneratedDraft, DeliveredUtterance }
public sealed record PersonaSource(string SourceId, string OriginKey, string Sha256, string Text,
    PersonaSourceKind SourceKind, string? RightsNote = null);
public enum TextOffsetUnit { Utf16, UnicodeScalar }
public sealed record TextLocation(int Start, int End, TextOffsetUnit Unit)
{
    public string Extract(string text)
    {
        if (text is null || Start < 0 || End < Start || !Enum.IsDefined(Unit))
            throw new InvalidDataException("invalid_text_location");
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[++i]))
                    throw new InvalidDataException("invalid_source_text");
            }
            else if (char.IsLowSurrogate(text[i])) throw new InvalidDataException("invalid_source_text");
        }
        if (Unit == TextOffsetUnit.Utf16)
        {
            if (End > text.Length || SplitsPair(Start) || SplitsPair(End))
                throw new InvalidDataException("invalid_text_location");
            return text[Start..End];
        }
        var boundaries = new List<int> { 0 };
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            offset += rune.Utf16SequenceLength;
            boundaries.Add(offset);
        }
        if (End >= boundaries.Count) throw new InvalidDataException("invalid_text_location");
        return text[boundaries[Start]..boundaries[End]];

        bool SplitsPair(int index) => index > 0 && index < text.Length
            && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]);
    }
}
public sealed record ParticipantRole(string SubjectRef, string Role);
/// <summary>Nullable qualitative notes, not calibrated probabilities.</summary>
public sealed record EvidenceConfidence(string? Transcription, string? Attribution, string? Interpretation);
public sealed record EvidenceMethod(string Kind, string Version, string AuthorRef,
    string? Provider = null, string? Model = null, string? ConfigurationRevision = null);
public sealed record EvidenceInterpretation(string Text, IReadOnlyList<string> Alternatives,
    EvidenceMethod Method, string ReviewStatus);
public sealed record PersonaEvidence(string EvidenceId, PersonaScope Scope, string SourceId,
    string SubjectRef, IReadOnlyList<ParticipantRole> ParticipantRoles, TextLocation Location,
    string ObservedContext, string ObservedBehavior, EvidenceInterpretation Interpretation,
    EvidenceConfidence Confidence, DateTimeOffset? OccurredAt = null, DateTimeOffset? AcquiredAt = null,
    DateTimeOffset? InterpretedAt = null, string? SupersedesEvidenceId = null);
