using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Cortex.Core.Shadow;
using Cortex.Core.Outbound;
using Filomena.App;
using System.Net.Http.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class DirectParticipationRuleTests
{
    internal const string DirectExit = "direct_involvement_rule";
    internal const string ProjectFile = "docs/manual-shadow-project-v3.json";
    internal const string Revision = "47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E";
    internal static JsonNode Project() => JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root, ProjectFile)))!;

    // Test-only participation cut; production routing is loaded from the artifact.
    internal static JsonNode ParticipationProject(string initialState, bool baseline = false, JsonNode? sourceProject = null)
    {
        var p = sourceProject?.DeepClone() ?? (baseline ? JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root, "docs/manual-shadow-project-v2.json")))! : Project());
        p["project"]!["initialStateId"] = initialState;
        foreach (var s in p["project"]!["states"]!.AsArray())
        {
            var g = s!["graph"]!;
            var control = g["controlEdges"]!.AsArray();
            foreach (var e in control.Where(e => e!["target"]!.ToString().EndsWith("-response-boundaries", StringComparison.Ordinal)).ToArray()) control.Remove(e);
            var nodes = g["nodes"]!.AsArray(); var data = g["edges"]!.AsArray();
            var keep = nodes.Where(n => n!["kind"]!.ToString() is "dm" or "group").Select(n => n!["id"]!.ToString()).ToHashSet();
            var pending = new Queue<string>(keep);
            while (pending.TryDequeue(out var source))
                foreach (var e in control.Where(e => e!["source"]!.ToString() == source))
                    if (keep.Add(e!["target"]!.ToString())) pending.Enqueue(e["target"]!.ToString());
            pending = new Queue<string>(keep);
            while (pending.TryDequeue(out var target))
                foreach (var e in data.Where(e => e!["target"]!.ToString() == target))
                    if (keep.Add(e!["source"]!.ToString())) pending.Enqueue(e["source"]!.ToString());
            foreach (var n in nodes.Where(n => !keep.Contains(n!["id"]!.ToString())).ToArray()) nodes.Remove(n);
            foreach (var list in new[] { control, data })
                foreach (var e in list.Where(e => !keep.Contains(e!["source"]!.ToString()) || !keep.Contains(e["target"]!.ToString())).ToArray()) list.Remove(e);
        }
        return p;
    }
    internal static bool Direct(Run run) => run.Report.Trace.Any(e => e.EventName == "node-completed" && e.Exit == DirectExit);
    internal static object? Decision(Run run) => Direct(run)
        ? new { participationDecisionSource = DirectExit, acao = "responder", continuar_acompanhando = true, motivo = "chamada_direta" }
        : null;

    [Fact]
    public async Task Full_project_roundtrips_editor_and_changes_only_direct_routing()
    {
        var p = Project(); var restored = p.DeepClone();
        foreach (var s in restored["project"]!["states"]!.AsArray())
        {
            var g = s!["graph"]!;
            foreach (var prefix in new[] { "dm", "group" })
            {
                var id = prefix + "-direct-involvement";
                var nodes = g["nodes"]!.AsArray();
                nodes.Remove(nodes.Single(n => n!["id"]!.ToString() == id));
                var data = g["edges"]!.AsArray();
                data.Remove(data.Single(e => e!["target"]!.ToString() == id));
                var edges = g["controlEdges"]!.AsArray();
                foreach (var e in edges.Where(e => e!["source"]!.ToString() == id).ToArray()) edges.Remove(e);
                edges.Single(e => e!["target"]!.ToString() == id)!["target"] = prefix + "-presence";
            }
        }
        var baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root, "docs/manual-shadow-project-v2.json")));
        Assert.True(JsonNode.DeepEquals(baseline, restored), "Prompts, models, settings and non-direct FSM must remain unchanged.");
        var bytes = File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root, ProjectFile));
        Assert.Equal(Revision, Convert.ToHexString(SHA256.HashData(bytes)));
        _ = PinnedShadowProject.FromBytes(bytes, "manual-shadow-direct-rule-v3", 3, Revision);
        var directory = Directory.CreateTempSubdirectory("direct-rule-editor-");
        using var noExternal = new HttpClient(new RejectExternal());
        await using var app = LocalPromptPlayground.Build(new(), noExternal, 0, projectDirectory: directory.FullName);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
        (await browser.PostAsJsonAsync("/api/flow/project", p, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var read = await browser.GetFromJsonAsync<JsonNode>("/api/flow/project", TestContext.Current.CancellationToken);
        Assert.True(JsonNode.DeepEquals(p, read));
    }
    private sealed class RejectExternal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("No external calls in editor test.");
    }

    [Theory]
    [InlineData(Inactive)]
    [InlineData(Attentive)]
    [InlineData(Engaged)]
    public async Task Direct_full_pipeline_retains_boundary_enforcement_and_shadow_only(string state)
    {
        var p = Project(); p["project"]!["initialStateId"] = state;
        var bytes = System.Text.Encoding.UTF8.GetBytes(p.ToJsonString());
        var pin = PinnedShadowProject.FromBytes(bytes, "direct-rule-local", 3, Convert.ToHexString(SHA256.HashData(bytes)));
        var directory = Directory.CreateTempSubdirectory("direct-rule-shadow-").FullName;
        var model = new FullSignals();
        await using var run = new ManualShadowRun(pin, BoundaryCoverageV1Tests.Binding, BoundaryCoverageV1Tests.Peer, "Helena", directory,
            BoundaryCoverageV1Tests.Roles, model, new BoundaryCoverageV1Tests.Memory());
        var r = await run.InjectAsync("Filomena, me ajuda a corrigir o relatório do trabalho?", TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(r.Outcomes).Success);
        Assert.Contains(r.Trace, e => e.EventName == "node-completed" && e.Exit == DirectExit);
        Assert.DoesNotContain(model.Calls, c => c.NodeId.EndsWith("-presence"));
        Assert.Equal(2, model.Calls.Count(c => c.NodeId.Contains("validator")));
        Assert.Contains(r.Trace, e => e.EventName == "node-outbound" && e.MemoryMetadata!["result"] == "shadowed");
        Assert.Contains(r.Trace, e => e.EventName == "node-hypothetical-transition");
        Assert.False(r.Outcomes[0].TransitionApplied);
        Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
        Assert.All(run.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
    }
    private sealed class FullSignals : IFlowCompletion
    {
        public System.Collections.Concurrent.ConcurrentQueue<FlowCompletionRequest> Calls { get; } = new();
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken token)
        {
            Calls.Enqueue(r);
            if (r.NodeId.EndsWith("-situation")) return new Signals("direto", "calar").CompleteAsync(r, token);
            if (r.NodeId.EndsWith("-presence")) throw new InvalidOperationException("Direct path must bypass presence.");
            if (!r.NodeId.Contains("validator")) return Task.FromResult("Vamos corrigir a versão do relatório.");
            var fields = r.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(x => x.Name, _ => "nao");
            if (fields.ContainsKey("relevante_ao_topico")) fields["relevante_ao_topico"] = "sim";
            return Task.FromResult(JsonSerializer.Serialize(fields));
        }
    }
    [Fact]
    public async Task Direct_routing_does_not_bypass_failed_boundary_recall()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root, ProjectFile));
        var pin = PinnedShadowProject.FromBytes(bytes, "direct-rule-local", 3, Revision);
        var directory = Directory.CreateTempSubdirectory("direct-rule-outage-").FullName;
        var model = new FullSignals();
        await using var run = new ManualShadowRun(pin, BoundaryCoverageV1Tests.Binding, BoundaryCoverageV1Tests.Peer, "Helena", directory,
            BoundaryCoverageV1Tests.Roles, model, new UnavailableMemory());
        var r = await run.InjectAsync("Filomena, me ajuda?", TestContext.Current.CancellationToken);
        Assert.False(Assert.Single(r.Outcomes).Success);
        Assert.Contains(r.Trace, e => e.EventName == "node-completed" && e.Exit == DirectExit);
        Assert.DoesNotContain(r.Trace, e => e.EventName == "node-outbound");
        Assert.DoesNotContain(model.Calls, c => c.NodeId.EndsWith("-generation"));
    }
    private sealed class UnavailableMemory : Cortex.Core.Memory.IMemoryReader
    {
        public Task<IReadOnlyList<Cortex.Core.Memory.MemoryItem>> QueryAsync(Cortex.Core.Memory.MemoryQuery query, CancellationToken token)
            => throw new HttpRequestException("Controlled recall outage, never valid empty.");
    }

    public static TheoryData<string, SessionEntryKind, string> DirectCases
    {
        get
        {
            var data = new TheoryData<string, SessionEntryKind, string>();
            foreach (var state in new[] { Inactive, Attentive, Engaged })
                foreach (var kind in new[] { SessionEntryKind.Dm, SessionEntryKind.Group })
                    foreach (var text in new[] { "Filomena, me ajuda com isso?", "Filomena, o que você acha?", "E você, Filomena?",
                        "Filomena, qual a diferença entre X e Y?", "Filomena, hoje foi horrível. Pode falar comigo?", "Filomena, você faria isso mesmo?",
                        "Filomena, me ajuda a organizar o dia? Tenho uma entrega de manhã e uma reunião à tarde.",
                        "Filomena, só te avisando; não precisa responder." }) data.Add(state, kind, text);
            return data;
        }
    }
    [Theory]
    [MemberData(nameof(DirectCases))]
    public async Task Direct_reaches_response_path_without_presence_even_if_presence_would_silence(string state, SessionEntryKind kind, string text)
    {
        var r = await ExecuteAsync(new(1, "direct regression", state, [new("Helena", text)]), false,
            new Signals("direto", "calar"), null, "none", TestContext.Current.CancellationToken, kind, ParticipationProject(state));
        Assert.True(r.Report.Success, r.Report.Error);
        Assert.True(Direct(r), "Direct involvement must select the deterministic response exit.");
        Assert.Null(r.Report.Presence);
        Assert.Single(r.Requests);
        Assert.EndsWith("-situation", r.Requests[0].NodeId);
        Assert.Equal(state, r.Outcome.StateId);
        Assert.Equal(state, r.Report.CurrentState);
        Assert.Equal(1, r.Report.Trace.Count(e => e.EventName == "execution-started"));
    }
    [Theory]
    [InlineData("indireto", "responder")]
    [InlineData("indireto", "esperar")]
    [InlineData("nenhum", "calar")]
    public async Task Non_direct_still_uses_presence_and_original_fsm(string involvement, string action)
    {
        var r = await ExecuteAsync(new(2, "contrast", Engaged, [new("Bia", "Gabriel, vamos discutir C++?")]), false,
            new Signals(involvement, action), null, "none", TestContext.Current.CancellationToken,
            SessionEntryKind.Group, ParticipationProject(Engaged));
        Assert.True(r.Report.Success, r.Report.Error);
        Assert.False(Direct(r));
        Assert.Equal(action, r.Report.Action);
        Assert.Equal(2, r.Requests.Count);
        Assert.Equal(involvement == "nenhum" ? Attentive : Engaged, r.Report.CurrentState);
    }
    [Theory]
    [InlineData("nenhum")]
    [InlineData("indireto")]
    public async Task Dm_identity_alone_does_not_force_response(string involvement)
    {
        var r = await ExecuteAsync(new(6, "casual DM contrast", Inactive, [new("Helena", "Hoje eu só queria tomar um café sem reunião.")]), false,
            new Signals(involvement, "calar"), null, "none", TestContext.Current.CancellationToken,
            SessionEntryKind.Dm, ParticipationProject(Inactive));
        Assert.True(r.Report.Success, r.Report.Error);
        Assert.False(Direct(r));
        Assert.Equal("calar", r.Report.Action);
        Assert.Equal(2, r.Requests.Count);
        Assert.NotNull(r.Report.Presence);
    }
    internal sealed class Signals(string involvement, string action) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => request.NodeId.EndsWith("-situation")
            ? Task.FromResult(JsonSerializer.Serialize(new { ambiente = "casual", envolvimento = involvement, ritmo = "aberto", necessita_cuidado = false, oportunidade_social = "fraca", resumo = "Helena conversa com Filomena." }))
            : request.NodeId.EndsWith("-presence")
                ? Task.FromResult(JsonSerializer.Serialize(new { acao = action, continuar_acompanhando = true, motivo = "chamada_direta" }))
                : throw new InvalidOperationException("Participation gate forbids generation/recall/enforcement.");
    }

    [Fact]
    public async Task Non_direct_presence_responder_has_same_behavior_before_and_after_rule()
    {
        var scenario = new Scenario(5, "migration decision replay", Engaged,
            [new("Helena", "O assunto de código acabou. Bia, qual filme você escolheu?"), new("Bia", "Uma comédia.")]);
        var before = await ExecuteAsync(scenario, false, new Signals("nenhum", "responder"), null, "none",
            TestContext.Current.CancellationToken, SessionEntryKind.Group, ParticipationProject(Engaged, baseline: true));
        var after = await ExecuteAsync(scenario, false, new Signals("nenhum", "responder"), null, "none",
            TestContext.Current.CancellationToken, SessionEntryKind.Group, ParticipationProject(Engaged));
        Assert.True(before.Report.Success && after.Report.Success);
        Assert.False(Direct(after));
        Assert.Equal("WOULD_RESPOND", before.Report.Signal);
        Assert.Equal("WOULD_RESPOND", after.Report.Signal);
        Assert.Equal(Engaged, before.Report.CurrentState);
        Assert.Equal(before.Report.CurrentState, after.Report.CurrentState);
        Assert.Equal(2, after.Requests.Count);
        for (var i = 0; i < before.Requests.Count; i++)
        {
            Assert.Equal(before.Requests[i].System, after.Requests[i].System);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before.Requests[i].Inputs.GetRawText()), JsonNode.Parse(after.Requests[i].Inputs.GetRawText())));
        }
    }
}
