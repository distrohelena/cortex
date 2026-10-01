using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GeneratorComparisonTests
{
    [Fact]
    public async Task Actual_request_keeps_raw_limits_and_voice_for_all_models()
    {
        var r = await GeneratorComparison.Capture(1, TestContext.Current.CancellationToken);
        Assert.Null(r.ResponseSchema);
        Assert.Contains("Superioridade é presença", r.System);
        Assert.Contains("não brincar nem provocar sobre trabalho", r.Inputs.GetProperty("limites")[0].GetProperty("conteudo").GetString());
        Assert.False(r.Inputs.TryGetProperty("compiled_boundary", out _));
        foreach (var model in new[] { "gemma", "luna", "terra" })
        {
            var mapped = GeneratorComparison.ForModel(r, model);
            Assert.Equal(r.System, mapped.System);
            Assert.True(JsonElement.DeepEquals(r.Inputs, mapped.Inputs));
        }
    }

    [Theory]
    [InlineData(false, "allow", "allow", 0, 1, "FINAL")]
    [InlineData(true, "allow", "allow", 2, 1, "FINAL")]
    [InlineData(true, "block", "allow", 4, 2, "FINAL")]
    [InlineData(true, "block", "block", 4, 2, "ABORT")]
    [InlineData(true, "error", "allow", 2, 1, "EnforcementFailure")]
    public async Task Frozen_pipeline_composes_existing_enforcement(bool limits, string first, string second, int detectors, int generations, string final)
    {
        var id = limits ? 1 : 15;
        var frozen = await GeneratorComparison.Capture(id, TestContext.Current.CancellationToken);
        var fake = new Fake(first, second);
        var result = await GeneratorComparison.RunPipeline(id, frozen, fake, TestContext.Current.CancellationToken);
        Assert.Equal(final, result.Final);
        Assert.Equal(detectors, fake.Detectors);
        Assert.Equal(generations, fake.Generations);
    }

    private sealed class Fake(string first, string second) : IFlowCompletion
    {
        public int Detectors;
        public int Generations;
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken t)
        {
            if (!r.NodeId.Contains("validator")) { Generations++; return Task.FromResult("candidata sintética"); }
            Interlocked.Increment(ref Detectors);
            var behavior = r.NodeId.Contains("-1") ? first : second;
            if (behavior == "error") throw new HttpRequestException("synthetic");
            return Task.FromResult(JsonSerializer.Serialize(r.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Name == "expoe_limite" && behavior == "block" ? "sim" : "nao")));
        }
    }

    [Fact]
    public async Task No_boundary_and_context_have_no_expected_label_leakage()
    {
        Assert.Equal(20, GeneratorComparison.Cases.Length);
        var r = await GeneratorComparison.Capture(15, TestContext.Current.CancellationToken);
        Assert.Empty(r.Inputs.GetProperty("limites").EnumerateArray());
        Assert.DoesNotContain("Expected", r.Inputs.GetRawText());
        Assert.DoesNotContain("ResponsePlan", r.System);
    }
}
