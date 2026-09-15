using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class BoundaryGateTests
{
    [Fact]
    public async Task BackendSaveLoadAndValidatePreservesCompleteGate()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("gate42-roundtrip-");
        try
        {
            using var external = new HttpClient(new RejectExternal());
            await using var app = App.LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            using var save = await browser.PostAsJsonAsync("/api/flow/project", Fixture(), token);
            save.EnsureSuccessStatusCode();
            var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", token);
            Assert.True(JsonElement.DeepEquals(loaded, JsonSerializer.SerializeToElement(Fixture())));
            using var validation = await browser.PostAsJsonAsync("/api/flow/typed/project/validate", new { project = loaded }, token);
            validation.EnsureSuccessStatusCode();
            var result = await validation.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.True(result.GetProperty("valid").GetBoolean(), result.GetRawText());
            Assert.Contains("id=\"add-count\"", await browser.GetStringAsync("/flow", token));
        }
        finally { directory.Delete(true); }
    }
    private sealed class RejectExternal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => throw new InvalidOperationException("No network in fake test");
    }
    internal static JsonNode Fixture() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "typed-flow-slice42.json")))!;

    [Fact]
    public async Task WholeDualEntryProjectIsExecutable()
    {
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new GateModel());
        try { runtime.UpdateProject(new("gate", 1, "slice42", JsonSerializer.SerializeToElement(Fixture()))); }
        catch (SessionFlowProjectException e) { Assert.Fail(JsonSerializer.Serialize(e.Issues)); }
    }

    [Theory]
    [InlineData(false, "permitir", "permitir", 0, 0, true)]
    [InlineData(false, "error", "error", 0, 0, true)]
    [InlineData(false, "invalid", "invalid", 0, 0, true)]
    [InlineData(true, "permitir", "permitir", 1, 0, true)]
    [InlineData(true, "bloquear", "permitir", 2, 1, true)]
    [InlineData(true, "bloquear", "bloquear", 2, 1, false)]
    [InlineData(true, "error", "permitir", 1, 0, false)]
    [InlineData(true, "invalid", "permitir", 2, 0, false)]
    [InlineData(true, "bloquear", "error", 2, 1, false)]
    public async Task OnlyApprovedPathCanReachSink(bool boundaries, string first, string second, int validations, int regenerations, bool sent)
    {
        var model = new GateModel(first, second);
        var sink = new List<string>();
        var run = await ExecuteAsync(Scenarios[1], true, model, new GateMemory(boundaries), "fake", TestContext.Current.CancellationToken,
            projectOverride: Fixture(), nodeStarting: (visit, token) =>
            {
                if (IsSink(visit.NodeId)) sink.Add(visit.Outputs[CandidateNode(visit.NodeId)]["texto"].Value.GetString()!);
                return Task.CompletedTask;
            });
        Assert.Equal(validations, model.Validations); Assert.Equal(regenerations, model.Regenerations);
        Assert.Equal(sent ? 1 : 0, sink.Count);
        Assert.Equal(sent ? Engaged : Inactive, run.Report.CurrentState);
        Assert.All(run.History, h => Assert.DoesNotContain("candidata", h.Message.GetRawText()));
        Assert.All(run.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        if (sent) Assert.Equal(regenerations == 0 ? "primeira candidata" : "nova candidata", sink.Single());
        foreach (var r in run.Requests.Where(r => r.NodeId.Contains("validator")))
        {
            Assert.Equal(new[] { "limites", "mensagens", "resposta" }, r.Inputs.EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.Equal("openai", r.Provider); Assert.Equal("gpt-5.6-luna", r.Model);
            Assert.Equal("Helena pediu para Filomena não brincar nem provocar sobre trabalho.",
                r.Inputs.GetProperty("limites")[0].GetProperty("conteudo").GetString());
            Assert.Equal("medium", r.ReasoningEffort); Assert.Equal(1024, r.MaxOutputTokens);
            Assert.Equal(BoundaryValidatorProbeTests.Prompt, r.System);
            Assert.DoesNotContain("whatsapp:", r.Inputs.GetRawText());
            Assert.DoesNotContain("personality", r.System, StringComparison.OrdinalIgnoreCase);
        }
    }
    [Theory]
    [InlineData("sink")]
    [InlineData("regeneration")]
    public async Task AsyncFailureHasNoTransition(string failure)
    {
        var sent = 0;
        var run = await ExecuteAsync(Scenarios[1], true, new Faulting(failure), new GateMemory(), "fake", TestContext.Current.CancellationToken,
            projectOverride: Fixture(), nodeStarting: async (visit, token) =>
            {
                if (!IsSink(visit.NodeId)) return;
                await Task.Yield(); token.ThrowIfCancellationRequested();
                if (failure == "sink") throw new InvalidOperationException("sink failed");
                sent++;
            });
        Assert.False(run.Outcome.Success); Assert.Equal(0, sent); Assert.Equal(Inactive, run.Report.CurrentState);
        Assert.DoesNotContain(run.Report.Trace, t => t.EventName == "transition-applied");
    }
    private sealed class Faulting(string failure) : IFlowCompletion
    {
        private readonly GateModel model = new("bloquear", "permitir");
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken token) =>
            failure == "regeneration" && r.NodeId.EndsWith("regeneration") ? Task.FromResult("  ") : model.CompleteAsync(r, token);
    }
    internal static bool IsSink(string id) => id.EndsWith("transition-responder") || id.EndsWith("accepted-1") || id.EndsWith("accepted-2");
    internal static string CandidateNode(string id) => (id.StartsWith("dm-") ? "dm-" : "group-") + (id.EndsWith("accepted-2") ? "regeneration" : "generation");
    internal sealed class GateMemory(bool enabled = true) : IMemoryReader
    {
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token) => Task.FromResult<IReadOnlyList<MemoryItem>>(
            enabled ? [new("synthetic-boundary", query.ConversationNamespace, "Helena pediu para Filomena não brincar nem provocar sobre trabalho.", MemoryCategory.Boundary, query.Persons.Where(p => p.ActorStableId == "whatsapp:5511944558218@s.whatsapp.net").ToArray(), "fake")] : []);
    }
    internal sealed class GateModel(string first = "permitir", string second = "permitir") : IFlowCompletion
    {
        public int Validations { get; private set; }
        public int Regenerations { get; private set; }
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken token)
        {
            if (r.NodeId.Contains("validator"))
            {
                Validations++; var result = r.NodeId.EndsWith("-1") ? first : second;
                if (result == "error") throw new HttpRequestException("provider unavailable");
                return Task.FromResult(result == "invalid" ? "{}" : JsonSerializer.Serialize(new { resultado = result, explicacao = "fixture" }));
            }
            if (r.NodeId.EndsWith("regeneration")) { Regenerations++; return Task.FromResult("nova candidata"); }
            return new GenerationTests.Model("primeira candidata").CompleteAsync(r, token);
        }
    }
}
