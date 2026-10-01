using Cortex.Core.Arbitration;

namespace Filomena.Llm.Arbitration;

/// <summary>Evaluation-local references. Never used as persistent identity.</summary>
public sealed class ArbitrationReferences(TurnArbitrationRequest request)
{
    public IReadOnlyDictionary<string, string> Messages { get; } = request.Messages
        .Select(m => m.ProviderMessageId).Distinct(StringComparer.Ordinal)
        .Select((id, index) => (id, reference: $"m{index + 1}"))
        .ToDictionary(x => x.reference, x => x.id, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Actors { get; } = request.Messages
        .Where(m => m.Sender is not null).Select(m => m.Sender!.StableId).Distinct(StringComparer.Ordinal)
        .Select((id, index) => (id, reference: $"p{index + 1}"))
        .ToDictionary(x => x.reference, x => x.id, StringComparer.Ordinal);

    public string MessageRef(string id) => Messages.FirstOrDefault(pair => pair.Value == id).Key ?? "unavailable";
    public string ActorRef(string? id) => Actors.FirstOrDefault(pair => pair.Value == id).Key ?? "unresolved-actor";

    public string? Resolve(TurnDecision local, out TurnDecision resolved)
    {
        var errors = new List<string>();
        string? anchor = null, target = null;
        if (local.AnchorRef is not null && !Messages.TryGetValue(local.AnchorRef, out anchor))
            errors.Add("anchorRef must be an available message ref (m1, m2, ...) or null");
        if (local.TargetRef is not null && !Actors.TryGetValue(local.TargetRef, out target))
            errors.Add("targetRef must be an available participant ref (p1, p2, ...) or null, never a message ref");
        if (local.Decision != TurnAction.Wait && local.RecheckAfterSeconds is not null)
            errors.Add("recheckAfterSeconds must be null for respond/ignore; only wait accepts a positive integer");
        resolved = local with { AnchorRef = anchor, TargetRef = target };
        return errors.Count == 0 ? null : string.Join("; ", errors);
    }
}
