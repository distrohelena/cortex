namespace Cortex.Personas;

public sealed record PatternConditions(string? CommunicativeAct = null, string? RelationshipKind = null,
    string? Audience = null, string? DynamicState = null);
public sealed record BehaviorPattern(string PatternId, PatternConditions Conditions,
    IReadOnlyList<PatternConditions> Exceptions, string Tendency,
    IReadOnlyList<string> SupportingEvidenceIds, IReadOnlyList<string> CounterEvidenceIds);
