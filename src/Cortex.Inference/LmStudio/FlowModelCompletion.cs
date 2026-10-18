using Cortex.Core.Flows;

namespace Cortex.Inference.LmStudio;

public sealed class FlowModelCompletion(LmStudioClient client, string defaultModel) : IFlowCompletion
{
    public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(request.Provider) && request.Provider != "lmstudio") throw new InvalidDataException("Provider não suportado neste executor.");
        // Constrained decoding enforces JSON shape, not semantic descriptions such as maxWords.
        // Present the very same contract to the model; local validation remains authoritative.
        var prompt = FlowInstructionPrompt.Build(request);
        return request.ResponseSchema is { } schema
            ? client.CompleteStructuredAsync(prompt, request.Inputs.GetRawText(), string.IsNullOrWhiteSpace(request.Model) ? defaultModel : request.Model, schema, request.Diagnostics, token)
            : client.CompleteResponseAsync(prompt, request.Inputs.GetRawText(), string.IsNullOrWhiteSpace(request.Model) ? defaultModel : request.Model, request.Diagnostics, token);
    }
}
