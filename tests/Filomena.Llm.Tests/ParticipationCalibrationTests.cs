using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class ParticipationCalibrationTests
{
    internal static JsonNode Project(string state, string variant = "two")
    {
        var suffix = variant == "two" ? "" : "-" + variant;
        var p = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, $"typed-flow-slice36{suffix}.json")))!;
        p["project"]!["initialStateId"] = state;
        return p;
    }

    public static TheoryData<string, string, bool, string, string> Transitions
    {
        get
        {
            var rows = new TheoryData<string, string, bool, string, string>();
            foreach (var state in new[] { Inactive, Attentive, Engaged })
                foreach (var action in new[] { "calar", "esperar", "responder" })
                    foreach (var follow in new[] { false, true })
                        foreach (var involvement in new[] { "nenhum", "indireto", "direto" })
                        {
                            var expected = action == "responder" ? Engaged
                                : state == Engaged ? involvement == "nenhum" ? Attentive : Engaged
                                : action == "esperar" || follow ? Attentive : Inactive;
                            rows.Add(state, action, follow, involvement, expected);
                        }
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public async Task GraphOwnsEveryStateTransition(string state, string action, bool follow, string involvement, string expected)
    {
        foreach (var variant in new[] { "two", "single" })
        {
            var scenario = Scenarios[0] with { InitialState = state };
            var run = await ExecuteAsync(scenario, false, new Signals(action, follow, involvement), null, "none",
                TestContext.Current.CancellationToken, projectOverride: Project(state, variant));
            Assert.True(run.Report.Success, run.Report.Error);
            Assert.Equal(expected, run.Report.CurrentState);
            Assert.Equal(action == "responder" ? "WOULD_RESPOND" : "SILENT", run.Report.Signal);
            Assert.Equal(variant == "two" ? 2 : 1, run.Report.LlmCalls);
            Assert.All(run.Requests, r => Assert.DoesNotContain("proximo_estado", r.ResponseSchema!.Value.GetRawText()));
            Assert.All(run.Requests, r => Assert.False(r.Inputs.TryGetProperty("memorias", out _)));
            if (state != Engaged && action == "calar")
                Assert.Contains(run.Report.Trace, e => e.NodeId == "group-follow" && e.EventName == "node-completed" && e.Exit == (follow ? "true" : "false"));
            if (state == expected && action != "responder")
                Assert.DoesNotContain(run.Report.Trace, e => e.EventName == "transition-applied");
        }
    }

    [Theory]
    [InlineData("two")]
    [InlineData("single")]
    [InlineData("memory")]
    public async Task EveryStateGraphValidatesAndBackendRoundtrips(string variant)
    {
        var p = Project(Inactive, variant);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new Signals());
        var compiled = runtime.UpdateProject(new("fixture", 1, "slice36", JsonSerializer.SerializeToElement(p)));
        Assert.Equal(3, compiled.StateIds.Count);
        var directory = Directory.CreateTempSubdirectory("slice36-roundtrip-");
        using var http = new HttpClient(new RejectExternal());
        try
        {
            await using var app = App.LocalPromptPlayground.Build(new(), http, 0, projectDirectory: directory.FullName);
            await app.StartAsync(TestContext.Current.CancellationToken);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(browser, "/api/flow/project", p, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            var loaded = await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<JsonElement>(browser, "/api/flow/project", TestContext.Current.CancellationToken);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(p), loaded));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task MemoryVariantIsExplicitAndCannotChooseState()
    {
        var memory = new BoundaryMemory();
        var run = await ExecuteAsync(Scenarios[7], true, new Signals(), memory, "synthetic", TestContext.Current.CancellationToken,
            projectOverride: Project(Inactive, "memory"));
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal(1, run.Report.MemoryItems);
        Assert.False(run.Requests[0].Inputs.TryGetProperty("memorias", out _));
        Assert.Equal(Engaged, run.Report.CurrentState);
    }

    [Theory]
    [InlineData("{\"acao\":\"responder\",\"continuar_acompanhando\":\"false\",\"motivo\":\"fixture\"}")]
    [InlineData("{\"acao\":\"responder\",\"continuar_acompanhando\":false,\"motivo\":\"um dois três quatro cinco seis sete oito nove dez onze doze treze\"}")]
    [InlineData("{\"acao\":\"responder\",\"continuar_acompanhando\":false,\"motivo\":\"fixture\",\"proximo_estado\":\"atenta\"}")]
    public async Task InvalidSignalCannotTransitionOrWouldRespond(string invalid)
    {
        var run = await ExecuteAsync(Scenarios[1], false, new InvalidSignal(invalid), null, "none", TestContext.Current.CancellationToken,
            projectOverride: Project(Inactive));
        Assert.False(run.Report.Success); Assert.Equal(Inactive, run.Report.CurrentState);
        Assert.Equal("SILENT", run.Report.Signal); Assert.Equal(3, run.Report.LlmCalls);
        Assert.DoesNotContain(run.Report.Trace, e => e.EventName == "transition-applied");
    }

    [Fact]
    public async Task SameCharacterKeepsDriftPlexAndDmStatesIndependent()
    {
        var token = TestContext.Current.CancellationToken;
        var project = Project(Inactive);
        foreach (var state in project["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray().Where(n => n!["kind"]!.GetValue<string>() == "accumulate"))
                node!["settings"] = JsonSerializer.SerializeToNode(new { quietSeconds = (double?)null, maxMessages = 1, maxWaitSeconds = (double?)null });
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new RoutedSignals());
        runtime.UpdateProject(new("fixture", 1, "slice36", JsonSerializer.SerializeToElement(project)));
        var drift = new SessionKey("runtime", "character", "whatsapp", "default", "5511981042361-1589823314@g.us");
        var plex = drift with { NativeConversationId = "120363318973251043@g.us" };
        var dm = drift with { NativeConversationId = "5511944558218@s.whatsapp.net" };
        await Task.WhenAll(Receive(drift, "responder", SessionEntryKind.Group), Receive(plex, "calar", SessionEntryKind.Group), Receive(dm, "esperar", SessionEntryKind.Dm));
        Assert.Equal(Engaged, runtime.ReadSession(drift)!.CurrentStateId);
        Assert.Equal(Inactive, runtime.ReadSession(plex)!.CurrentStateId);
        Assert.Equal(Attentive, runtime.ReadSession(dm)!.CurrentStateId);
        await Receive(drift, "calar", SessionEntryKind.Group);
        Assert.Equal(Engaged, runtime.ReadOutcomes(drift).Last().StateId);
        Assert.Equal(Attentive, runtime.ReadSession(drift)!.CurrentStateId);
        Assert.Equal(Inactive, runtime.ReadSession(plex)!.CurrentStateId);
        Assert.Equal(Attentive, runtime.ReadSession(dm)!.CurrentStateId);

        async Task Receive(SessionKey session, string action, SessionEntryKind kind)
        {
            var message = JsonSerializer.SerializeToElement(new
            {
                texto = action,
                remetente = (object?)null,
                canal = new { nome = "fixture", nativeId = session.NativeConversationId },
                reply = (object?)null
            });
            var admission = await runtime.ReceiveAsync(session, new(action, session.ChannelId, session.AccountId, session.NativeConversationId,
                DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, kind, message), token);
            Assert.Equal(SessionAdmissionStatus.Accepted, admission.Status);
            await runtime.WaitForIdleAsync(session, token);
        }
    }

    internal sealed class Signals(string action = "responder", bool follow = false, string involvement = "direto") : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            var properties = request.ResponseSchema!.Value.GetProperty("properties");
            var values = new Dictionary<string, object>();
            if (properties.TryGetProperty("ambiente", out _))
            {
                values["ambiente"] = "casual"; values["envolvimento"] = involvement; values["ritmo"] = "aberto";
                values["necessita_cuidado"] = false; values["oportunidade_social"] = "fraca"; values["resumo"] = "Conversa de teste";
            }
            if (properties.TryGetProperty("acao", out _))
            { values["acao"] = action; values["continuar_acompanhando"] = follow; values["motivo"] = "fixture"; }
            return Task.FromResult(JsonSerializer.Serialize(values));
        }
    }

    private sealed class InvalidSignal(string invalid) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            request.NodeId.EndsWith("situation", StringComparison.Ordinal) ? new Signals().CompleteAsync(request, token)
            : Task.FromResult(invalid);
    }

    private sealed class RoutedSignals : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            new Signals(request.Inputs.GetProperty("mensagens")[0].GetProperty("texto").GetString()!, false, "nenhum").CompleteAsync(request, token);
    }

    private sealed class RejectExternal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("No external requests in project persistence.");
    }
}
