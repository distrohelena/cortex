using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class LunaGeneratorGateTests
{
    [Fact]
    public async Task Real_DAG_routes_both_candidates_to_configured_Luna_without_changing_upstream()
    {
        var model = new Capture();
        var run = await ExecuteAsync(Scenarios[1], true, model,
            new BoundaryGateTests.GateMemory(true), "synthetic", TestContext.Current.CancellationToken,
            projectOverride: MultiDetectorGateTests.Fixture());
        Assert.True(run.Outcome.Success, run.Outcome.Error);
        Assert.Equal(Engaged, run.Report.CurrentState);
        Assert.Equal(2, model.Candidates.Count);
        Assert.All(model.Candidates, r =>
        {
            Assert.Equal("openai", r.Provider);
            Assert.Equal("gpt-5.6-luna", r.Model);
            Assert.Equal("medium", r.ReasoningEffort);
            Assert.Null(r.MaxOutputTokens);
            Assert.Null(r.ResponseSchema);
            Assert.Contains("GROUNDING DE PESSOAS", r.System);
            Assert.Contains("Não invente estados pessoais para tornar a resposta mais empática.", r.System);
        });
        Assert.All(model.Upstream, r =>
        {
            Assert.Equal("lmstudio", r.Provider);
            Assert.Equal("google/gemma-4-e4b", r.Model);
        });
        Assert.Equal(4, model.Detectors);
    }

    private sealed class Capture : IFlowCompletion
    {
        private readonly BoundaryGateTests.GateModel generation = new();
        public List<FlowCompletionRequest> Candidates { get; } = [];
        public List<FlowCompletionRequest> Upstream { get; } = [];
        private int detectors;
        public int Detectors => detectors;
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken token)
        {
            if (r.NodeId.Contains("validator"))
            {
                Interlocked.Increment(ref detectors);
                var fields = r.ResponseSchema!.Value.GetProperty("properties").EnumerateObject()
                    .ToDictionary(p => p.Name, _ => "nao");
                if (fields.ContainsKey("relevante_ao_topico")) fields["relevante_ao_topico"] = "sim";
                else if (r.NodeId.EndsWith("-1")) fields["deprecia_experiencia_protegida"] = "sim";
                return Task.FromResult(JsonSerializer.Serialize(fields));
            }
            if (r.NodeId.EndsWith("-generation") || r.NodeId.EndsWith("-regeneration")) Candidates.Add(r);
            else Upstream.Add(r);
            return generation.CompleteAsync(r, token);
        }
    }
}
