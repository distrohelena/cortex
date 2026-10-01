using Cortex.Core.Flows;

namespace Cortex.Inference;

public sealed class ProviderFlowCompletion(IReadOnlyDictionary<string, IFlowCompletion> providers) : IFlowCompletion
{
    public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "lmstudio" : request.Provider;
        if (!providers.TryGetValue(provider, out var completion))
            throw new InvalidDataException("Provider unavailable in this executor.");
        return completion.CompleteAsync(request, token);
    }
}
