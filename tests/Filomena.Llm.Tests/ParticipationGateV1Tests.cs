using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class ParticipationGateV1Tests
{
    internal static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    internal static JsonNode Project(string initialState)
    {
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "docs/manual-shadow-project-v2.json")))!;
        project["project"]!["initialStateId"] = initialState;
        foreach (var state in project["project"]!["states"]!.AsArray())
        {
            var edges = state!["graph"]!["controlEdges"]!.AsArray();
            foreach (var edge in edges.Where(e => e!["source"]!.GetValue<string>() is "dm-action" or "group-action"
                && e["sourcePort"]!.GetValue<string>() == "case-responder").ToArray()) edges.Remove(edge);
            // The real compiler rejects orphan operational nodes. Keep only the
            // participation control closure plus its original data dependencies.
            var graph = state["graph"]!;
            var nodes = graph["nodes"]!.AsArray();
            var data = graph["edges"]!.AsArray();
            var keep = nodes.Where(n => n!["kind"]!.GetValue<string>() is "dm" or "group")
                .Select(n => n!["id"]!.GetValue<string>()).ToHashSet();
            var pending = new Queue<string>(keep);
            while (pending.TryDequeue(out var current))
                foreach (var edge in edges.Where(e => e!["source"]!.GetValue<string>() == current))
                    if (keep.Add(edge!["target"]!.GetValue<string>())) pending.Enqueue(edge["target"]!.GetValue<string>());
            pending = new Queue<string>(keep);
            while (pending.TryDequeue(out var target))
                foreach (var edge in data.Where(e => e!["target"]!.GetValue<string>() == target))
                    if (keep.Add(edge!["source"]!.GetValue<string>())) pending.Enqueue(edge["source"]!.GetValue<string>());
            foreach (var node in nodes.Where(n => !keep.Contains(n!["id"]!.GetValue<string>())).ToArray()) nodes.Remove(node);
            foreach (var collection in new[] { edges, data })
                foreach (var edge in collection.Where(e => !keep.Contains(e!["source"]!.GetValue<string>()) || !keep.Contains(e["target"]!.GetValue<string>())).ToArray()) collection.Remove(edge);
        }
        return project;
    }

    [Theory]
    [InlineData(Inactive)]
    [InlineData(Attentive)]
    [InlineData(Engaged)]
    public async Task Responder_stops_before_generation_with_actual_initial_state(string state)
    {
        var scenario = new Scenario(1, "direct", state, [new("Helena", "Filomena, pode ajudar?")]);
        var run = await ExecuteAsync(scenario, false, new Signals("responder", true, "direto"), null, "none",
            TestContext.Current.CancellationToken, SessionEntryKind.Dm, Project(state));
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal(state, run.Outcome.StateId);
        Assert.Equal(state, run.Report.CurrentState);
        Assert.Equal("WOULD_RESPOND", run.Report.Signal);
        Assert.Equal(2, run.Requests.Count);
        Assert.DoesNotContain(run.Report.Trace, e => e.EventName == "transition-applied" || e.EventName == "node-outbound");
    }

    [Fact]
    public async Task Migration_uses_real_fsm_and_self_history_without_extra_execution()
    {
        var scenario = new Scenario(2, "migration", Engaged, [new("Helena", "Bia, vai ao cinema?")], [new("Filomena", "Use um semáforo compartilhado.")]);
        var run = await ExecuteAsync(scenario, false, new Signals("calar", false, "nenhum"), null, "none",
            TestContext.Current.CancellationToken, SessionEntryKind.Group, Project(Engaged));
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal(Engaged, run.Outcome.StateId);
        Assert.Equal(Attentive, run.Report.CurrentState);
        Assert.Equal(1, run.Report.Trace.Count(e => e.EventName == "execution-started"));
        Assert.Equal(SessionMessageDirection.Outbound, run.History[0].Direction);
        Assert.Contains("semáforo", run.Requests[0].Inputs.GetProperty("historico")[0].GetProperty("texto").GetString());
    }

    internal sealed class Signals(string action, bool follow, string involvement) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (!request.NodeId.EndsWith("-situation") && !request.NodeId.EndsWith("-presence"))
                throw new InvalidOperationException("Participation-only gate forbids generation or enforcement.");
            return request.NodeId.EndsWith("-situation")
                ? new ParticipationCalibrationTests.Signals(action, follow, involvement).CompleteAsync(request, token)
                : Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new { acao = action, continuar_acompanhando = follow, motivo = "pergunta_direta" }));
        }
    }
}
