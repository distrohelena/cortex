using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Filomena.Llm.SystemOne;
using Cortex.Inference.SystemOne;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class SystemOneParticipationTests
{
    internal const string SourceArtifact = "docs/manual-live-project-v3-gpt6-luna.json";
    internal static JsonNode Project(string state, string variant, string provider, string model)
    {
        if (variant is not ("baseline" or "situation" or "both")) throw new InvalidDataException("Unknown experiment variant.");
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root, SourceArtifact)))!;
        var project = DirectParticipationRuleTests.ParticipationProject(state, sourceProject: source);
        if (variant == "baseline") return project;
        foreach (var s in project["project"]!["states"]!.AsArray())
        {
            var graph = s!["graph"]!;
            var nodes = graph["nodes"]!.AsArray();
            foreach (var prefix in new[] { "dm", "group" })
            {
                var situation = nodes.Single(n => n!["id"]!.ToString() == prefix + "-situation")!;
                var fields = situation["responseFields"]!.AsArray();
                fields.Remove(fields.Single(f => f!["id"]!.ToString() == "resumo"));
                var edges = graph["edges"]!.AsArray();
                foreach (var edge in edges.Where(e => e!["source"]!.ToString() == prefix + "-situation" && e["sourcePort"]!.ToString() == "resumo").ToArray())
                {
                    var target = nodes.Single(n => n!["id"]!.ToString() == edge!["target"]!.ToString())!;
                    var inputs = target["inputs"]!.AsArray();
                    inputs.Remove(inputs.Single(i => i!["id"]!.ToString() == edge!["targetPort"]!.ToString()));
                    edges.Remove(edge);
                }
                Configure(situation);
                if (variant == "both") Configure(nodes.Single(n => n!["id"]!.ToString() == prefix + "-presence")!);
            }
        }
        return project;

        void Configure(JsonNode node)
        {
            node["provider"] = provider;
            node["model"] = model;
            node["correctionRetry"] = false;
        }
    }

    [Theory]
    [InlineData(Inactive)]
    [InlineData(Attentive)]
    [InlineData(Engaged)]
    public async Task Typed_direct_decision_enters_real_runtime_once_and_bypasses_presence(string state)
    {
        using var handler = new Decisions("direto", "calar");
        using var http = new HttpClient(handler);
        var run = await ExecuteAsync(new(1, "direct", state, [new("Helena", "Filomena, pode me ajudar?")]), false,
            Completion(http), null, "none", TestContext.Current.CancellationToken, SessionEntryKind.Dm, Project(state, "both", "laya", "multilingual"));
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.True(DirectParticipationRuleTests.Direct(run));
        Assert.Single(run.Requests);
        Assert.Equal(state, run.Report.CurrentState);
        Assert.Single(run.Report.Trace, t => t.EventName == "execution-started");
        Assert.DoesNotContain(run.Report.Trace, t => t.EventName == "node-outbound");
        Assert.False(run.Report.Situation!.Value.TryGetProperty("resumo", out _));
    }

    [Fact]
    public async Task Non_direct_decision_uses_presence_and_existing_migration_transition()
    {
        using var handler = new Decisions("nenhum", "calar");
        using var http = new HttpClient(handler);
        var run = await ExecuteAsync(new(2, "migration", Engaged, [new("Helena", "Bia, mudando de assunto: qual filme?")]), false,
            Completion(http), null, "none", TestContext.Current.CancellationToken, SessionEntryKind.Group, Project(Engaged, "both", "laya", "multilingual"));
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal("calar", run.Report.Action);
        Assert.Equal(Attentive, run.Report.CurrentState);
        Assert.Equal(2, run.Requests.Count);
        Assert.DoesNotContain(run.Report.Trace, t => t.EventName == "node-outbound");
    }

    internal static IFlowCompletion Completion(HttpClient http) => new SystemOneFlowCompletion(
        new(http, new("http://localhost/v1/systemone"), () => null, false), "laya", 0.5);

    // Only the external inference boundary is replaced; compiler, batching, routing and FSM are real.
    internal sealed class Decisions(string involvement, string action, string model = "laya-rl-agent") : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var answers = new JsonObject();
            foreach (var q in body.RootElement.GetProperty("questions").EnumerateObject())
            {
                if (q.Value.GetProperty("type").GetString() == "noul")
                { answers[q.Name] = new JsonObject { ["type"] = "noul", ["noul"] = 0.1 }; continue; }
                var chosen = q.Name switch { "envolvimento" => involvement, "acao" => action, _ => q.Value.GetProperty("criteria").EnumerateObject().First().Name };
                var probabilities = new JsonObject();
                foreach (var option in q.Value.GetProperty("criteria").EnumerateObject()) probabilities[option.Name] = option.Name == chosen ? 1.0 : 0.0;
                answers[q.Name] = new JsonObject { ["type"] = "choice", ["choice"] = chosen, ["probabilities"] = probabilities, ["confidence"] = 1.0 };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["model"] = model, ["answers"] = answers,
                ["usage"] = new JsonObject { ["input_tokens"] = 20, ["output_tokens"] = 0 } }.ToJsonString()) };
        }
    }
}
