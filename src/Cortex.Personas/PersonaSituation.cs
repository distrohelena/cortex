using System.Text.Json;

namespace Cortex.Personas;

/// <summary>A host-admitted observation, not a permission. Null means the dimension is unknown.</summary>
public sealed record SituationValue(string Value, string OriginRef);
public sealed record PersonaSituation(PersonaScope Scope, string SituationId,
    SituationValue? CommunicativeAct = null, SituationValue? RelationshipKind = null,
    SituationValue? Audience = null, SituationValue? DynamicState = null,
    IReadOnlyList<string>? AlternativeReadings = null);
public sealed record SkippedPersonaPattern(string PatternId, string Reason);
public sealed record PersonaSelection(IReadOnlyList<string> SelectedPatternIds,
    IReadOnlyList<SkippedPersonaPattern> SkippedPatterns,
    IReadOnlyList<string> SupportingOriginKeys, IReadOnlyList<string> CounterOriginKeys);
public sealed class PersonaProjection
{
    private readonly JsonElement payload;
    internal PersonaProjection(PersonaScope scope, string revisionId, string artifactSha256,
        string situationId, JsonElement payload, PersonaSelection selection)
    {
        Scope = scope;
        RevisionId = revisionId;
        ArtifactSha256 = artifactSha256;
        SituationId = situationId;
        this.payload = payload.Clone();
        Selection = selection;
    }
    public PersonaScope Scope { get; }
    public string RevisionId { get; }
    public string ArtifactSha256 { get; }
    public string SituationId { get; }
    public JsonElement Payload => payload.Clone();
    public PersonaSelection Selection { get; }
}
