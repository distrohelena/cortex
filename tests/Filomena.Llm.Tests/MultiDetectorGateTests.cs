using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Boundaries;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class MultiDetectorGateTests
{
    internal static JsonNode Fixture()
    {
        var fixture = BoundaryGateTests.Fixture();
        foreach (var state in fixture["project"]!["states"]!.AsArray())
        {
            var graph = state!["graph"]!;
            foreach (var prefix in new[] { "dm", "group" })
            {
                var source = prefix + "-response-boundaries";
                var compile = prefix + "-compiled-boundaries";
                graph["nodes"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
                {
                    id = compile,
                    kind = "interpretBoundaries",
                    x = 2000,
                    y = prefix == "dm" ? 1250 : 2900,
                    settings = new { taxonomyVersion = "v1", grammarVersion = "v1", templatesVersion = "v1" }
                }));
                graph["edges"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { source, sourcePort = "memories", target = compile, targetPort = "memories" }));
                foreach (var edge in graph["controlEdges"]!.AsArray().Where(e => e!["source"]!.GetValue<string>() == source).ToArray()) edge!["source"] = compile;
                graph["controlEdges"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { source, sourcePort = "then", target = compile, targetPort = "exec" }));
                foreach (var node in graph["nodes"]!.AsArray().Where(n => n!["id"]!.GetValue<string>().StartsWith(prefix + "-validator-")))
                {
                    node!["executionProfile"] = BoundaryEnforcementProfile.Id;
                    node["semanticContractVersion"] = "no_teasing_about_topic:semantic-v2";
                    node["boundaryEvaluatorVersion"] = "no_teasing_about_topic:evaluator-v2";
                    node["boundaryTemplateVersion"] = "no_teasing_about_topic:v1";
                    node["enforcementStrategy"] = "multiple_detectors_v1";
                    node["complementaryContractVersion"] = "no_teasing_about_topic:implicit-depreciation-v1";
                    node["aggregationPolicyVersion"] = "any_block_all_allow:v1";
                    node["correctionRetry"] = false;
                    node["prompt"] = "Boundary enforcement: contratos resolvidos pela template versionada.";
                    node.AsObject().Remove("responseFields");
                    node["inputs"]!.AsArray().Single(i => i!["name"]!.GetValue<string>() == "limites")!["type"] = "boundary[]";
                    var id = node["id"]!.GetValue<string>();
                    foreach (var edge in graph["edges"]!.AsArray())
                    {
                        if (edge!["target"]!.GetValue<string>() == id && edge["targetPort"]!.GetValue<string>() == "boundaries")
                        { edge["source"] = compile; edge["sourcePort"] = "boundaries"; }
                        if (edge["source"]!.GetValue<string>() == id)
                            edge["sourcePort"] = edge["sourcePort"]!.GetValue<string>() == "result" ? "resultado" : "explicacao";
                    }
                }
            }
        }
        return fixture;
    }

    [Theory]
    [InlineData(false, "error", "error", true, 0)]
    [InlineData(true, "allow", "allow", true, 0)]
    [InlineData(true, "block", "allow", true, 1)]
    [InlineData(true, "block", "block", false, 1)]
    [InlineData(true, "error", "allow", false, 0)]
    [InlineData(true, "invalid", "allow", false, 0)]
    public async Task Full_DAG_preserves_sink_transition_and_rejected_draft_isolation(bool boundaries, string first, string second, bool sent, int regenerated)
    {
        var model = new Gate(first, second);
        var sinks = 0;
        await using var validation = new SessionFlowRuntime(TimeProvider.System, model);
        try { validation.UpdateProject(new("fake", 1, "multi", JsonSerializer.SerializeToElement(Fixture()))); }
        catch (SessionFlowProjectException e) { Assert.Fail(JsonSerializer.Serialize(e.Issues.Take(5))); }
        var run = await ExecuteAsync(Scenarios[1], true, model, new BoundaryGateTests.GateMemory(boundaries), "fake", TestContext.Current.CancellationToken,
            projectOverride: Fixture(), nodeStarting: (visit, token) =>
            {
                if (BoundaryGateTests.IsSink(visit.NodeId)) sinks++;
                return Task.CompletedTask;
            });
        Assert.Equal(sent ? 1 : 0, sinks);
        Assert.Equal(regenerated, model.Generation.Regenerations);
        Assert.Equal(sent ? Engaged : Inactive, run.Report.CurrentState);
        Assert.All(run.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        if (!boundaries) Assert.Equal(0, model.Detections);
    }

    private sealed class Gate(string first, string second) : IFlowCompletion
    {
        public BoundaryGateTests.GateModel Generation { get; } = new();
        private int detections;
        public int Detections => detections;
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (!request.NodeId.Contains("validator")) return Generation.CompleteAsync(request, token);
            Interlocked.Increment(ref detections);
            var behavior = request.NodeId.EndsWith("-1") ? first : second;
            if (behavior == "error") throw new HttpRequestException("synthetic");
            if (behavior == "invalid") return Task.FromResult("{}");
            var fields = request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao");
            if (fields.ContainsKey("relevante_ao_topico")) fields["relevante_ao_topico"] = "sim";
            else if (behavior == "block") fields["deprecia_experiencia_protegida"] = "sim";
            return Task.FromResult(JsonSerializer.Serialize(fields));
        }
    }
}
