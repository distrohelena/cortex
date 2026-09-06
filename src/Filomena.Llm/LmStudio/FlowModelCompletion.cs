using Filomena.Core.Flows;

namespace Filomena.Llm.LmStudio;

public sealed class FlowModelCompletion(LmStudioClient client, string defaultModel) : IFlowCompletion
{
    public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(request.Provider) && request.Provider != "lmstudio") throw new InvalidDataException("Provider não suportado neste executor.");
        var prompt = request.System + (request.Correction is null ? "" : "\nCorrija o formato: " + request.Correction);
        return request.ResponseSchema is { } schema
            ? client.CompleteStructuredAsync(prompt, request.Inputs.GetRawText(), string.IsNullOrWhiteSpace(request.Model) ? defaultModel : request.Model, schema, token)
            : client.CompleteResponseAsync(prompt, request.Inputs.GetRawText(), string.IsNullOrWhiteSpace(request.Model) ? defaultModel : request.Model, token);
    }
}
