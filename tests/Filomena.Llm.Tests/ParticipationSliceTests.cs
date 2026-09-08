using System.Text.Json;
using System.Net.Http.Json;
using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ParticipationSliceTests
{
    [Theory]
    [InlineData("calar", "inativa", ParticipationExperiments.Inactive)]
    [InlineData("esperar", "atenta", ParticipationExperiments.Attentive)]
    [InlineData("responder", "engajada", ParticipationExperiments.Engaged)]
    public async Task ValidatedActionSelectsOnePathAndMapsStableStateIds(string action, string next, string stateId)
    {
        var run = await ParticipationExperiments.ExecuteAsync(ParticipationExperiments.Scenarios[1], false,
            new ParticipationExperiments.FakeModel(action, next), null, "none", TestContext.Current.CancellationToken);
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal(stateId, run.Report.CurrentState);
        Assert.Equal(action == "responder" ? "WOULD_RESPOND" : "SILENT", run.Report.Signal);
        Assert.Equal(2, run.Report.LlmCalls);
        var situation = run.Requests[0]; var presence = run.Requests[1];
        Assert.DoesNotContain("State participation guidance", situation.System);
        Assert.DoesNotContain("Personality core", situation.System);
        Assert.Contains("State participation guidance", presence.System);
        Assert.DoesNotContain("Personality core", presence.System);
        Assert.False(situation.Inputs.TryGetProperty("memorias", out _));
        Assert.False(presence.Inputs.TryGetProperty("memorias", out _));
        Assert.Equal(2, presence.Inputs.GetProperty("mensagens").GetArrayLength());
        Assert.Empty(presence.Inputs.GetProperty("historico").EnumerateArray());
        Assert.Single(run.Report.Trace, e => e.EventName == "node-started" && e.NodeId!.Contains("transition-", StringComparison.Ordinal));
        Assert.DoesNotContain("você acha", JsonSerializer.Serialize(run.Report.Trace));
    }

    [Fact]
    public async Task MemoryIsExactSmallExplicitAndNeverReachesSituation()
    {
        var memory = new ParticipationExperiments.BoundaryMemory();
        var run = await ParticipationExperiments.ExecuteAsync(ParticipationExperiments.Scenarios[7], true,
            new ParticipationExperiments.FakeModel(), memory, "fake", TestContext.Current.CancellationToken);
        Assert.True(run.Report.Success, run.Report.Error);
        var query = Assert.Single(memory.Queries);
        Assert.Equal(Core.Memory.MemoryMode.Exact, query.Mode);
        Assert.Equal(4, query.MaxItems); Assert.Equal(700, query.MaxCharacters);
        Assert.Null(query.Text);
        Assert.Equal("whatsapp-group-5511981042361-1589823314-g-us", query.ConversationNamespace);
        Assert.False(run.Requests[0].Inputs.TryGetProperty("memorias", out _));
        var inputs = run.Requests[1].Inputs;
        var boundary = Assert.Single(inputs.GetProperty("memorias").EnumerateArray());
        Assert.Equal("limite", boundary.GetProperty("categoria").GetString());
        Assert.Contains("trabalho", boundary.GetProperty("conteudo").GetString());
        Assert.DoesNotContain("5511944558218", inputs.GetRawText());
        Assert.Equal(1, run.Report.MemoryItems);
    }

    [Fact]
    public async Task DmPresetBatchesAndRunsSameTypedPipeline()
    {
        var run = await ParticipationExperiments.ExecuteAsync(ParticipationExperiments.Scenarios[1], false,
            new ParticipationExperiments.FakeModel(), null, "none", TestContext.Current.CancellationToken, SessionEntryKind.Dm);
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal("dm", run.Requests[1].Inputs.GetProperty("tipo_conversa").GetString());
        Assert.Equal(SessionBatchCloseReason.QuietPeriod, run.Outcome.CloseReason);
        Assert.All(run.Report.Calls, c => Assert.StartsWith("dm-", c.NodeId));
    }

    [Fact]
    public async Task InvalidPresenceNeverProducesWouldRespondOrTransition()
    {
        var run = await ParticipationExperiments.ExecuteAsync(ParticipationExperiments.Scenarios[1], false,
            new ParticipationExperiments.FakeModel("responder", "unknown-state"), null, "none", TestContext.Current.CancellationToken);
        Assert.False(run.Report.Success);
        Assert.Equal("SILENT", run.Report.Signal);
        Assert.Equal(ParticipationExperiments.Inactive, run.Report.CurrentState);
        Assert.Null(run.Report.Presence);
        Assert.Equal(3, run.Report.LlmCalls); // situation + presence + one corrective retry
        Assert.DoesNotContain(run.Report.Trace, e => e.NodeId == "group-action");
        Assert.DoesNotContain(run.Report.Trace, e => e.EventName == "transition-applied");
    }

    [Fact]
    public async Task AllThreeStateFixturesCompileWithBothMemoryVariants()
    {
        foreach (var state in new[] { ParticipationExperiments.Inactive, ParticipationExperiments.Attentive, ParticipationExperiments.Engaged })
            foreach (var memory in new[] { false, true })
            {
                await using var runtime = new SessionFlowRuntime(TimeProvider.System, new ParticipationExperiments.FakeModel());
                var project = ParticipationExperiments.Project(memory, state);
                Assert.Equal(state, runtime.UpdateProject(new("fixture", 1, "revision", JsonSerializer.SerializeToElement(project))).InitialStateId);
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectBackendPersistsAllThreeGraphsAndPromptReferences(bool memory)
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("participation-roundtrip-");
        using var external = new HttpClient(new NoExternal());
        try
        {
            await using var app = LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            var project = ParticipationExperiments.Project(memory);
            (await browser.PostAsJsonAsync("/api/flow/project", project, token)).EnsureSuccessStatusCode();
            var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", token);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(project), loaded));
            var validation = await browser.PostAsJsonAsync("/api/flow/typed/project/validate", new { project = loaded }, token);
            validation.EnsureSuccessStatusCode();
            Assert.True((await validation.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("valid").GetBoolean());
            var html = await browser.GetStringAsync("/flow", token);
            Assert.Contains("use-personality-core", html);
            Assert.Contains("participation-guidance", html);
        }
        finally { directory.Delete(true); }
    }

    private sealed class NoExternal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Project save/load cannot call models, memory or transport.");
    }
}
