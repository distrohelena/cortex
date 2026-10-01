using System.Text.Json;
using Cortex.Core.Flows;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

// Test-only composition; production project and enforcement remain unchanged.
internal static class GroundingComposition
{
    internal sealed record Result(string Final, string? Text, object[] Decisions, object? Trace);
    internal static async Task<Result> Run(int id, JsonElement inputs, string generation, string regeneration, string clause, int budget, IFlowCompletion live, CancellationToken token)
    {
        var messages = inputs.GetProperty("mensagens").EnumerateArray()
            .Select(m => new Line(m.GetProperty("remetente").GetProperty("nome").GetString()!, m.GetProperty("texto").GetString()!)).ToArray();
        var hasBoundary = inputs.GetProperty("limites").GetArrayLength() > 0;
        foreach (var boundary in inputs.GetProperty("limites").EnumerateArray())
            if (boundary.GetProperty("conteudo").GetString() != "Helena pediu para Filomena não brincar nem provocar sobre trabalho.")
                throw new InvalidDataException("Synthetic memory mismatch.");
        var item = new GeneratorComparison.Case(new(id, "grounding-B", Engaged, messages), hasBoundary, false,
            inputs.GetProperty("ambiente").GetString()!, inputs.GetProperty("necessita_cuidado").GetBoolean(),
            inputs.GetProperty("resumo_situacao").GetString()!, "");
        var routing = new Routing(item, inputs, generation, regeneration, clause, budget, live);
        string? text = null;
        var run = await ExecuteAsync(item.Scenario, true, routing, new GeneratorComparison.Memory(item), "synthetic-grounding-B", token,
            projectOverride: MultiDetectorGateTests.Fixture(), nodeStarting: (visit, t) =>
            {
                if (BoundaryGateTests.IsSink(visit.NodeId))
                    text = visit.Outputs[BoundaryGateTests.CandidateNode(visit.NodeId)]["texto"].Value.GetString();
                return Task.CompletedTask;
            });
        var decisions = run.Outcome.Outputs.Where(p => p.Key.Contains("validator"))
            .Select(p => (object)new { node = p.Key, outputs = p.Value }).ToArray();
        var final = routing.Failure ?? (text is not null ? "FINAL" : decisions.Length > 0 ? "ABORT" : "EnforcementFailure");
        return new(final, text, decisions, run.Report.Trace);
    }

    private sealed class Routing(GeneratorComparison.Case item, JsonElement inputs, string generation, string regeneration,
        string clause, int budget, IFlowCompletion live) : IFlowCompletion
    {
        internal string? Failure;
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            var detector = request.NodeId.Contains("validator");
            var generate = request.NodeId.EndsWith("-generation");
            var regenerate = request.NodeId.EndsWith("-regeneration");
            if (!detector && !generate && !regenerate) return await GeneratorComparison.Upstream(request, item, token);
            if (request.Provider != "openai" || request.Model != "gpt-5.6-luna" || request.ReasoningEffort != "medium")
                throw new InvalidDataException("Luna medium only; no fallback.");
            if (generate || regenerate)
            {
                var values = inputs;
                if (regenerate)
                {
                    var merged = request.Inputs.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
                    foreach (var p in inputs.EnumerateObject()) merged[p.Name] = p.Value.Clone();
                    values = JsonSerializer.SerializeToElement(merged);
                }
                request = GroundingPromptExperiment.Variant(request with
                {
                    System = generate ? generation : regeneration,
                    Inputs = values,
                    MaxOutputTokens = budget
                }, "B", clause);
            }
            try { return await live.CompleteAsync(request, token); }
            catch { Failure = detector ? "EnforcementFailure" : "GenerationFailure"; throw; }
        }
    }
}
