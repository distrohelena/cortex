using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cortex.Personas;

public sealed class PersonaContextCompiler
{
    private static readonly JsonSerializerOptions ProjectionOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public PersonaSelection Select(ValidatedPersonaRevision revision, PersonaSituation situation, int maxPatterns)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var bundle = revision.Bundle;
        ValidateSituation(bundle, situation, maxPatterns);
        return Select(bundle, situation, maxPatterns);
    }

    public PersonaProjection Compile(ValidatedPersonaRevision revision, PersonaSituation situation, int maxPatterns)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var bundle = revision.Bundle;
        ValidateSituation(bundle, situation, maxPatterns);
        var selection = Select(bundle, situation, maxPatterns);
        var byPattern = bundle.Patterns.ToDictionary(p => p.PatternId, StringComparer.Ordinal);
        var selected = selection.SelectedPatternIds.Select(id => byPattern[id]).ToArray();
        var requiredEvidence = selected.SelectMany(p => p.SupportingEvidenceIds.Concat(p.CounterEvidenceIds))
            .Concat(bundle.Spec.ValuesAndBeliefs.Concat(bundle.Spec.VoiceGuidance).SelectMany(a => a.EvidenceIds))
            .ToHashSet(StringComparer.Ordinal);
        var sources = bundle.Sources.ToDictionary(s => s.SourceId, StringComparer.Ordinal);
        var evidence = bundle.Evidence.Where(e => requiredEvidence.Contains(e.EvidenceId))
            .OrderBy(e => e.EvidenceId, StringComparer.Ordinal).Select(e => new
            {
                e.EvidenceId,
                role = "evidence_data",
                e.SubjectRef,
                e.ParticipantRoles,
                e.SourceId,
                sources[e.SourceId].Sha256,
                sources[e.SourceId].OriginKey,
                sources[e.SourceId].SourceKind,
                sources[e.SourceId].RightsNote,
                e.Location,
                passage = e.Location.Extract(sources[e.SourceId].Text),
                e.ObservedContext,
                e.ObservedBehavior,
                e.Interpretation,
                e.Confidence,
                e.OccurredAt,
                e.AcquiredAt,
                e.InterpretedAt,
                e.SupersedesEvidenceId
            }).ToArray();
        var payload = JsonSerializer.SerializeToElement(new
        {
            identity = new { characterRef = bundle.Spec.Scope.CharacterId, description = bundle.Spec.IdentityDescription, bundle.Spec.ValuesAndBeliefs },
            situation = new
            {
                situation.SituationId,
                situation.CommunicativeAct,
                situation.RelationshipKind,
                situation.Audience,
                situation.DynamicState,
                alternativeReadings = situation.AlternativeReadings ?? []
            },
            behavioralTendencies = selected.Select(p => new
            {
                p.PatternId,
                modality = "tendency",
                p.Conditions,
                p.Exceptions,
                p.Tendency,
                p.SupportingEvidenceIds,
                p.CounterEvidenceIds
            }).ToArray(),
            bundle.Spec.VoiceGuidance,
            evidence,
            uncertainty = new
            {
                interpretationsAreHypotheses = true,
                noApplicablePatterns = selected.Length == 0,
                selection.SkippedPatterns
            }
        }, ProjectionOptions);
        return new(bundle.Spec.Scope, bundle.Spec.RevisionId, revision.ArtifactSha256,
            situation.SituationId, payload, selection);
    }

    private static PersonaSelection Select(PersonaRevisionBundle b, PersonaSituation situation, int limit)
    {
        var eligible = b.Spec.SelectedPatternIds.ToHashSet(StringComparer.Ordinal);
        var selected = new List<BehaviorPattern>();
        var skipped = new List<SkippedPersonaPattern>();
        foreach (var pattern in b.Patterns.Where(p => eligible.Contains(p.PatternId)).OrderBy(p => p.PatternId, StringComparer.Ordinal))
        {
            var match = Match(pattern.Conditions, situation);
            var exceptions = pattern.Exceptions.Select(e => Match(e, situation)).ToArray();
            var reason = match switch
            {
                ConditionMatch.No => "condition_mismatch",
                ConditionMatch.Unknown => "condition_unknown",
                _ when exceptions.Contains(ConditionMatch.Yes) => "exception_matched",
                _ when exceptions.Contains(ConditionMatch.Unknown) => "exception_uncertain",
                _ when selected.Count >= limit => "host_limit",
                _ => null
            };
            if (reason is null) selected.Add(pattern);
            else skipped.Add(new(pattern.PatternId, reason));
        }
        var origins = b.Evidence.ToDictionary(e => e.EvidenceId, e => b.Sources.Single(s => s.SourceId == e.SourceId).OriginKey, StringComparer.Ordinal);
        return new(Array.AsReadOnly(selected.Select(p => p.PatternId).ToArray()), Array.AsReadOnly(skipped.ToArray()),
            OriginKeys(selected.SelectMany(p => p.SupportingEvidenceIds)),
            OriginKeys(selected.SelectMany(p => p.CounterEvidenceIds)));

        IReadOnlyList<string> OriginKeys(IEnumerable<string> ids) => Array.AsReadOnly(ids.Select(id => origins[id])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateSituation(PersonaRevisionBundle b, PersonaSituation situation, int maxPatterns)
    {
        if (maxPatterns <= 0) throw new ArgumentOutOfRangeException(nameof(maxPatterns));
        if (situation is null || situation.Scope != b.Spec.Scope) throw new InvalidDataException("situation_scope_mismatch");
        PersonaRevisionCodec.RequireText(situation.SituationId);
        foreach (var value in new[] { situation.CommunicativeAct, situation.RelationshipKind, situation.Audience, situation.DynamicState })
        {
            if (value is null) continue;
            PersonaRevisionCodec.RequireText(value.Value);
            PersonaRevisionCodec.RequireText(value.OriginRef);
        }
        if (situation.AlternativeReadings is not null)
            foreach (var reading in situation.AlternativeReadings) PersonaRevisionCodec.RequireText(reading);
    }

    private static ConditionMatch Match(PatternConditions conditions, PersonaSituation situation)
    {
        var dimensions = new (string? Expected, SituationValue? Observed)[]
        {
            (conditions.CommunicativeAct, situation.CommunicativeAct),
            (conditions.RelationshipKind, situation.RelationshipKind),
            (conditions.Audience, situation.Audience),
            (conditions.DynamicState, situation.DynamicState)
        };
        var unknown = false;
        foreach (var (expected, observed) in dimensions)
        {
            if (expected is null) continue;
            if (observed is null) unknown = true;
            else if (!string.Equals(expected, observed.Value, StringComparison.Ordinal)) return ConditionMatch.No;
        }
        return unknown ? ConditionMatch.Unknown : ConditionMatch.Yes;
    }

    private enum ConditionMatch { Yes, No, Unknown }
}
