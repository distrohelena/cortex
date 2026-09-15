using System.Text.Json;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GroundingPromptExperimentTests
{
    [Theory]
    [InlineData("group-generation")]
    [InlineData("group-regeneration")]
    public void B_appends_only_clause_and_A_preserves_all_request_fields(string node)
    {
        var source = new FlowCompletionRequest(node, "frozen prompt", JsonSerializer.SerializeToElement(new { mensagens = new[] { "input" } }),
            ResponseSchema: null, Model: "gpt-5.6-luna", Provider: "openai", Correction: null, ReasoningEffort: "medium", MaxOutputTokens: 1024);
        var a = GroundingPromptExperiment.Variant(source, "A", "grounding clause");
        var b = GroundingPromptExperiment.Variant(source, "B", "grounding clause");
        Assert.Equal(source, a);
        Assert.Equal("frozen prompt\n\ngrounding clause", b.System);
        Assert.Equal(source, b with { System = source.System });
    }

    [Theory]
    [InlineData("C", "gpt-5.6-luna", "medium")]
    [InlineData("A", "another-model", "medium")]
    [InlineData("B", "gpt-5.6-luna", "high")]
    public void Unsupported_arm_model_or_effort_never_reaches_provider(string arm, string model, string effort)
    {
        var source = new FlowCompletionRequest("generation", "frozen", JsonSerializer.SerializeToElement(new { }),
            ResponseSchema: null, Model: model, Provider: "openai", Correction: null, ReasoningEffort: effort, MaxOutputTokens: 1024);
        Assert.Throws<ArgumentException>(() => GroundingPromptExperiment.Variant(source, arm, "clause"));
    }
}
