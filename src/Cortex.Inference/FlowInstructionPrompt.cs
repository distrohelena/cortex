using Cortex.Core.Flows;

namespace Cortex.Inference;

/// <summary>
/// The system text of a prompt node: the node's own instructions, then the output contract and, on a retry, the
/// correction. The text this layer adds is English whatever language the node's instructions use, so a consumer's
/// prompt language is never nudged by the kernel.
/// </summary>
public static class FlowInstructionPrompt
{
    public const string ContractHeading = "[Output contract]";
    public const string ContractNote = "Respect the field descriptions and word limits as well.";
    public const string CorrectionPrefix = "Fix the format: ";

    public static string Build(FlowCompletionRequest request) => request.System
        + (request.ResponseSchema is { } contract
            ? "\n\n" + ContractHeading + "\n" + ContractNote + "\n" + contract.GetRawText() : "")
        + (request.Correction is null ? "" : "\n" + CorrectionPrefix + request.Correction);
}
