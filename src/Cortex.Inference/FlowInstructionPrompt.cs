using Cortex.Core.Flows;

namespace Cortex.Inference;

public static class FlowInstructionPrompt
{
    public static string Build(FlowCompletionRequest request) => request.System
        + (request.ResponseSchema is { } contract
            ? "\n\n[Contrato de saída]\nRespeite também as descrições e limites de palavras.\n" + contract.GetRawText() : "")
        + (request.Correction is null ? "" : "\nCorrija o formato: " + request.Correction);
}
