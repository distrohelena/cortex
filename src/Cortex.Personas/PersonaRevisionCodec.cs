using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cortex.Personas;

public sealed class PersonaRevisionCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public ValidatedPersonaRevision Encode(PersonaRevisionBundle bundle)
    {
        if (bundle is null) throw new InvalidDataException("invalid_persona_bundle");
        // Validate original text before the JSON encoder can replace malformed UTF-16.
        Validate(bundle);
        byte[] bytes;
        try { bytes = JsonSerializer.SerializeToUtf8Bytes(bundle, Options); }
        catch (JsonException) { throw new InvalidDataException("invalid_artifact_schema"); }
        var snapshot = Read(bytes);
        Validate(snapshot);
        return new(bytes, Hash(bytes));
    }

    public ValidatedPersonaRevision Decode(ReadOnlyMemory<byte> payload, string expectedSha256)
    {
        var bytes = payload.ToArray();
        if (!IsHash(expectedSha256) || !string.Equals(Hash(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("artifact_hash_mismatch");
        var snapshot = Read(bytes);
        Validate(snapshot);
        return new(bytes, Hash(bytes));
    }

    internal static PersonaRevisionBundle Read(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<PersonaRevisionBundle>(bytes, Options)
                ?? throw new InvalidDataException("invalid_artifact_schema");
        }
        catch (JsonException) { throw new InvalidDataException("invalid_artifact_schema"); }
    }

    private static void Validate(PersonaRevisionBundle b)
    {
        if (b.ContractVersion != 1) throw new InvalidDataException("unsupported_persona_contract_version");
        if (b.Spec is null || b.Spec.Scope is null || b.Sources is null || b.Evidence is null || b.Patterns is null)
            throw new InvalidDataException("invalid_persona_bundle");
        RequireText(b.Spec.Scope.ProjectId);
        RequireText(b.Spec.Scope.WorldId);
        RequireText(b.Spec.Scope.CharacterId);
        RequireText(b.Spec.RevisionId);
        RequireText(b.Spec.IdentityDescription);
        if (b.Spec.ParentArtifactSha256 is not null && !IsHash(b.Spec.ParentArtifactSha256))
            throw new InvalidDataException("invalid_parent_hash");
        var sources = Index(b.Sources, x => x.SourceId);
        var evidence = Index(b.Evidence, x => x.EvidenceId);
        var patterns = Index(b.Patterns, x => x.PatternId);
        foreach (var source in sources.Values)
        {
            RequireText(source.OriginKey);
            if (source.Text is null || !Enum.IsDefined(source.SourceKind))
                throw new InvalidDataException("invalid_source");
            byte[] text;
            try { text = new UTF8Encoding(false, true).GetBytes(source.Text); }
            catch (EncoderFallbackException) { throw new InvalidDataException("invalid_source_text"); }
            if (!IsHash(source.Sha256) || !string.Equals(Hash(text), source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("source_hash_mismatch");
        }
        foreach (var item in evidence.Values)
        {
            if (item.Scope != b.Spec.Scope) throw new InvalidDataException("evidence_scope_mismatch");
            if (!sources.TryGetValue(item.SourceId ?? "", out var source))
                throw new InvalidDataException("missing_source");
            if (item.Location is null || item.Interpretation is null || item.Confidence is null
                || item.ParticipantRoles is null || item.Interpretation.Method is null)
                throw new InvalidDataException("invalid_evidence");
            if (source.SourceKind == PersonaSourceKind.GeneratedDraft)
                throw new InvalidDataException("draft_is_not_observed_behavior");
            if (item.Location.Extract(source.Text).Length == 0)
                throw new InvalidDataException("empty_evidence_passage");
            RequireText(item.SubjectRef);
            RequireText(item.ObservedContext);
            RequireText(item.ObservedBehavior);
            RequireText(item.Interpretation.Text);
            RequireText(item.Interpretation.ReviewStatus);
            RequireText(item.Interpretation.Method.Kind);
            RequireText(item.Interpretation.Method.Version);
            RequireText(item.Interpretation.Method.AuthorRef);
            if (item.Interpretation.Alternatives is null) throw new InvalidDataException("invalid_interpretation");
            foreach (var alternative in item.Interpretation.Alternatives) RequireText(alternative);
            foreach (var role in item.ParticipantRoles)
            {
                if (role is null) throw new InvalidDataException("invalid_participant_role");
                RequireText(role.SubjectRef);
                RequireText(role.Role);
            }
            if (!item.ParticipantRoles.Any(role => role.SubjectRef == item.SubjectRef))
                throw new InvalidDataException("subject_role_missing");
        }
        ValidateLineage(evidence);
        foreach (var pattern in patterns.Values)
        {
            RequireText(pattern.Tendency);
            ValidateConditions(pattern.Conditions);
            if (pattern.Exceptions is null) throw new InvalidDataException("invalid_pattern");
            foreach (var exception in pattern.Exceptions) ValidateConditions(exception);
            ValidateRefs(pattern.SupportingEvidenceIds, evidence.Keys, "missing_support_evidence");
            ValidateRefs(pattern.CounterEvidenceIds, evidence.Keys, "missing_counter_evidence");
            if (pattern.SupportingEvidenceIds.Count == 0) throw new InvalidDataException("unsupported_pattern");
        }
        ValidateRefs(b.Spec.SelectedPatternIds, patterns.Keys, "missing_selected_pattern");
        ValidateAssertions(b.Spec.ValuesAndBeliefs, evidence.Keys);
        ValidateAssertions(b.Spec.VoiceGuidance, evidence.Keys);
    }

    private static void ValidateAssertions(IReadOnlyList<PersonaAssertion> assertions, IEnumerable<string> evidenceIds)
    {
        if (assertions is null) throw new InvalidDataException("invalid_assertions");
        foreach (var assertion in assertions)
        {
            if (assertion is null || !Enum.IsDefined(assertion.Basis)) throw new InvalidDataException("invalid_assertion");
            RequireText(assertion.Text);
            ValidateRefs(assertion.EvidenceIds, evidenceIds, "missing_assertion_evidence");
            if (assertion.Basis == PersonaAssertionBasis.Authorial) RequireText(assertion.AuthorRef);
            else if (assertion.EvidenceIds.Count == 0) throw new InvalidDataException("unsupported_assertion");
        }
    }

    private static void ValidateLineage(IReadOnlyDictionary<string, PersonaEvidence> evidence)
    {
        foreach (var start in evidence.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = start;
            while (true)
            {
                if (!visited.Add(current.EvidenceId)) throw new InvalidDataException("evidence_supersession_cycle");
                if (current.SupersedesEvidenceId is null) break;
                if (!evidence.TryGetValue(current.SupersedesEvidenceId, out current!))
                    throw new InvalidDataException("missing_superseded_evidence");
            }
        }
    }

    internal static void ValidateConditions(PatternConditions conditions)
    {
        if (conditions is null) throw new InvalidDataException("invalid_conditions");
        foreach (var value in new[] { conditions.CommunicativeAct, conditions.RelationshipKind, conditions.Audience, conditions.DynamicState })
            if (value is not null) RequireText(value);
    }

    private static Dictionary<string, T> Index<T>(IReadOnlyList<T> items, Func<T, string> key) where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException("invalid_item");
            var id = key(item);
            RequireText(id);
            if (!result.TryAdd(id, item)) throw new InvalidDataException("duplicate_id");
        }
        return result;
    }

    private static void ValidateRefs(IReadOnlyList<string> references, IEnumerable<string> knownIds, string code)
    {
        if (references is null) throw new InvalidDataException(code);
        var known = knownIds.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in references)
            if (id is null || !known.Contains(id) || !seen.Add(id)) throw new InvalidDataException(code);
    }

    internal static void RequireText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("required_text_missing");
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
