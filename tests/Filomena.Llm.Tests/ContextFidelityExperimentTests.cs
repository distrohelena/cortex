using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ContextFidelityExperimentTests
{
    [Fact]
    public async Task Frozen_current_prompt_and_inputs_differ_only_by_fidelity_clause()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var data = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs/context-fidelity-ab-inputs.json"), TestContext.Current.CancellationToken));
        using var project = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs/manual-shadow-project-v1.json"), TestContext.Current.CancellationToken));
        var d = data.RootElement;
        var p = project.RootElement.GetProperty("project");
        var state = p.GetProperty("states")[0];
        var node = state.GetProperty("graph").GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "dm-generation");
        var system = "[Personality core]\n" + p.GetProperty("personalityCore").GetString()
            + "\n\n[State participation guidance]\n" + state.GetProperty("participationGuidance").GetString()
            + "\n\n" + node.GetProperty("prompt").GetString();
        Assert.Equal(system, d.GetProperty("generationPrompt").GetString());
        Assert.Equal(12, d.GetProperty("cases").GetArrayLength());
        foreach (var fixture in d.GetProperty("cases").EnumerateArray())
        {
            var original = new FlowCompletionRequest("dm-generation", system, fixture.GetProperty("inputs"), null,
                "gpt-5.6-luna", "openai", null, "medium", 1024);
            var a = GroundingPromptExperiment.Variant(original, "A", d.GetProperty("clause").GetString()!);
            var b = GroundingPromptExperiment.Variant(original, "B", d.GetProperty("clause").GetString()!);
            Assert.Equal(original, a);
            Assert.Equal(original, b with { System = a.System });
            Assert.Equal(a.System + "\n\n" + d.GetProperty("clause").GetString(), b.System);
            Assert.Empty(a.Inputs.GetProperty("limites").EnumerateArray());
            Assert.Empty(a.Inputs.GetProperty("memorias_sociais").EnumerateArray());
            Assert.Equal("dm", a.Inputs.GetProperty("tipo_conversa").GetString());
        }
    }
}
