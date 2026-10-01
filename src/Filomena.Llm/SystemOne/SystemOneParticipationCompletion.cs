using Cortex.Core.Flows;

namespace Filomena.Llm.SystemOne;

/// <summary>Host-bound typed participation with a generative summary; other nodes remain unchanged.</summary>
public sealed class SystemOneParticipationCompletion(IFlowCompletion typed, IFlowCompletion baseline,
    string provider, string model, IReadOnlySet<string> situationNodes, IReadOnlySet<string> presenceNodes,
    string summaryField = "resumo") : IFlowCompletion
{
    private readonly HashSet<string> situations = new(situationNodes, StringComparer.Ordinal);
    private readonly HashSet<string> presences = new(presenceNodes, StringComparer.Ordinal);

    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        if (situations.Overlaps(presences) || string.IsNullOrWhiteSpace(summaryField))
            throw new InvalidDataException("Ambiguous System 1 participation binding.");
        if (presences.Contains(request.NodeId)) return await Typed(request, token);
        if (!situations.Contains(request.NodeId)) return await baseline.CompleteAsync(request, token);
        return await Cortex.Inference.SystemOne.TypedSummaryComposition.CompleteAsync(
            typed, baseline, request, provider, model, summaryField, token);
    }

    private Task<string> Typed(FlowCompletionRequest request, CancellationToken token) =>
        typed.CompleteAsync(request with { Provider = provider, Model = model }, token);
}
