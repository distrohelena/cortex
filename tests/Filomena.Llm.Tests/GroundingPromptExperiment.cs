using Cortex.Core.Flows;

namespace Filomena.Llm.Tests;

internal static class GroundingPromptExperiment
{
    internal static FlowCompletionRequest Variant(FlowCompletionRequest baseline, string arm, string clause)
    {
        if (arm is not ("A" or "B") || baseline.Provider != "openai" || baseline.Model != "gpt-5.6-luna" || baseline.ReasoningEffort != "medium")
            throw new ArgumentException("This experiment permits only A/B, OpenAI Luna medium.");
        return arm == "A" ? baseline : baseline with { System = baseline.System + "\n\n" + clause };
    }
}
