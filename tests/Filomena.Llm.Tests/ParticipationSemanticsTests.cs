using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class ParticipationSemanticsTests
{
    internal static JsonNode Fixture(string state = Inactive)
    {
        var p = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "typed-flow-slice37.json")))!;
        p["project"]!["initialStateId"] = state;
        return p;
    }

    public static TheoryData<string, string, bool, string, string> Transitions => ParticipationCalibrationTests.Transitions;

    [Theory]
    [MemberData(nameof(Transitions))]
    public async Task SignalsStillUseGraphOwnedTransitions(string state, string action, bool follow, string involvement, string expected)
    {
        var run = await ExecuteAsync(Scenarios[0] with { InitialState = state }, false,
            new Signals(action, follow, involvement), null, "none", TestContext.Current.CancellationToken,
            projectOverride: Fixture(state), projectRevision: "slice37");
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal(expected, run.Report.CurrentState);
        Assert.Equal(action == "responder" ? "WOULD_RESPOND" : "SILENT", run.Report.Signal);
        Assert.Equal(2, run.Report.LlmCalls);
        Assert.DoesNotContain("Interest profile", run.Requests[0].System);
        Assert.DoesNotContain("State participation guidance", run.Requests[0].System);
        Assert.Contains("Interest profile", run.Requests[1].System);
        Assert.Contains("State participation guidance", run.Requests[1].System);
        Assert.All(run.Requests, r => Assert.DoesNotContain("Personality core", r.System));
        Assert.All(run.Requests, r => Assert.False(r.Inputs.TryGetProperty("memorias", out _)));
        var reason = run.Requests[1].ResponseSchema!.Value.GetProperty("properties").GetProperty("motivo");
        Assert.Equal("string", reason.GetProperty("type").GetString());
        Assert.Equal(12, reason.GetProperty("enum").GetArrayLength());
        Assert.False(reason.TryGetProperty("maxWords", out _));
        Assert.DoesNotContain("proximo_estado", run.Requests[1].ResponseSchema!.Value.GetRawText());
        Assert.DoesNotContain("tecnologia, programação", JsonSerializer.Serialize(run.Report.Trace));
    }

    [Theory]
    [InlineData("chamada_direta")]
    [InlineData("interesse_no_assunto")]
    [InlineData("conflito_sem_convite")]
    public async Task ReasonNeverControlsState(string reason)
    {
        var run = await ExecuteAsync(Scenarios[0], false, new Signals("calar", false, "nenhum", reason), null, "none",
            TestContext.Current.CancellationToken, projectOverride: Fixture());
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal(Inactive, run.Report.CurrentState);
        Assert.Equal(reason, run.Report.Presence!.Value.GetProperty("motivo").GetString());
        Assert.DoesNotContain(run.Report.Trace, t => t.EventName == "transition-applied");
    }

    [Theory]
    [InlineData("uma justificativa livre")]
    [InlineData("reason_unknown")]
    public async Task UnknownReasonFailsClosedAfterOneCorrection(string reason)
    {
        var run = await ExecuteAsync(Scenarios[1], false, new Signals(reason: reason), null, "none",
            TestContext.Current.CancellationToken, projectOverride: Fixture());
        Assert.False(run.Report.Success);
        Assert.Null(run.Report.Presence);
        Assert.Equal(3, run.Report.LlmCalls);
        Assert.Equal("SILENT", run.Report.Signal);
        Assert.Equal(Inactive, run.Report.CurrentState);
    }

    [Fact]
    public async Task ProjectRoundtripsAndValidatesThroughRealEditorBackend()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("slice37-roundtrip-");
        using var external = new HttpClient(new RejectExternal());
        try
        {
            await using var app = App.LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            var project = Fixture();
            using var saved = await browser.PostAsJsonAsync("/api/flow/project", project, token);
            saved.EnsureSuccessStatusCode();
            var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", token);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(project), loaded));
            using var validation = await browser.PostAsJsonAsync("/api/flow/typed/project/validate", new { project = loaded }, token);
            validation.EnsureSuccessStatusCode();
            Assert.True((await validation.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("valid").GetBoolean());
            var html = await browser.GetStringAsync("/flow", token);
            Assert.Contains("use-interest-profile", html);
            Assert.Contains("id=\"interest-profile\"", html);
        }
        finally { directory.Delete(true); }
    }

    internal sealed class Signals(string action = "responder", bool follow = true, string involvement = "direto", string reason = "chamada_direta") : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            request.NodeId.EndsWith("situation", StringComparison.Ordinal)
                ? new ParticipationCalibrationTests.Signals(involvement: involvement).CompleteAsync(request, token)
                : Task.FromResult(JsonSerializer.Serialize(new { acao = action, continuar_acompanhando = follow, motivo = reason }));
    }

    private sealed class RejectExternal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("No external requests during roundtrip.");
    }
}
