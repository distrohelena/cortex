using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class GenerationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProjectRoundtripAndBackendValidation(bool memory)
    {
        var token = TestContext.Current.CancellationToken;
        using var external = new HttpClient(new RejectExternal());
        var directory = Directory.CreateTempSubdirectory("slice4-roundtrip-");
        try
        {
            await using var app = App.LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            var project = Fixture(memory);
            using var saved = await browser.PostAsJsonAsync("/api/flow/project", project, token);
            saved.EnsureSuccessStatusCode();
            var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", token);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(project), loaded));
            using var validation = await browser.PostAsJsonAsync("/api/flow/typed/project/validate", new { project = loaded }, token);
            validation.EnsureSuccessStatusCode();
            var result = await validation.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.True(result.GetProperty("valid").GetBoolean(), result.GetRawText());
            Assert.Contains("id=\"output-mode\"", await browser.GetStringAsync("/flow", token));
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(Inactive)]
    [InlineData(Attentive)]
    [InlineData(Engaged)]
    public async Task EachStateGeneratesUsingItsCapturedGuidance(string state)
    {
        var (result, run) = await GenerationExperiments.Execute(GenerationExperiments.Cases[1] with { InitialState = state },
            true, new Model(), new GenerationExperiments.Memories(), "fake", TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error);
        Assert.Equal("WOULD_SEND", result.Signal);
        Assert.Equal(Engaged, result.FinalState);
        Assert.Contains(Fixture()["project"]!["states"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == state)!["participationGuidance"]!.GetValue<string>(),
            run.Requests.Single(r => r.NodeId == "group-generation").System);
    }

    [Theory]
    [InlineData("calar")]
    [InlineData("esperar")]
    public async Task SilentBranchesNeverReadMemoryOrGenerate(string action)
    {
        var reader = new BoundaryMemory();
        var (result, run) = await GenerationExperiments.Execute(Scenarios[1], true, new Model(action: action), reader, "fake",
            TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error);
        Assert.Equal("NO_SEND", result.Signal);
        Assert.Empty(reader.Queries);
        Assert.DoesNotContain(run.Requests, r => r.NodeId == "group-generation");
    }

    [Fact]
    public async Task MultiplePersonsRemainAttributedWithoutInternalIdentity()
    {
        var (result, run) = await GenerationExperiments.Execute(GenerationExperiments.Cases[10], true,
            new Model(), new GenerationExperiments.Memories(), "fake", TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error);
        var inputs = run.Requests.Single(r => r.NodeId == "group-generation").Inputs;
        var memories = inputs.GetProperty("memorias").EnumerateArray().ToArray();
        Assert.Equal(2, memories.Length);
        Assert.Contains("Helena", memories[0].GetRawText());
        Assert.Contains("Bia", memories[1].GetRawText());
        Assert.Equal("limite", memories[0].GetProperty("categoria").GetString());
        Assert.DoesNotContain("whatsapp:", inputs.GetRawText());
        Assert.DoesNotContain("participantUuid", inputs.GetRawText());
        Assert.DoesNotContain("fixture-", inputs.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SimulationIsAwaitedBeforeTransition(bool fail)
    {
        var received = new List<SessionInstructionOutput>();
        var run = await ExecuteAsync(Scenarios[1], false, new Model(), null, "none",
            TestContext.Current.CancellationToken, projectOverride: Fixture(false), projectRevision: "slice4",
            instructionOutput: async (output, token) =>
            {
                if (!output.NodeId.EndsWith("-generation", StringComparison.Ordinal)) return;
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                received.Add(output);
                if (fail) throw new InvalidOperationException("simulation failed");
            });
        var sent = Assert.Single(received);
        Assert.Equal(Inactive, sent.StateId);
        Assert.Equal("Uma resposta.", sent.Outputs["texto"].Value.GetString());
        Assert.Equal(!fail, run.Outcome.Success);
        Assert.Equal(fail ? Inactive : Engaged, run.Report.CurrentState);
    }

    internal static JsonNode Fixture(bool memory = true, string state = Inactive)
    {
        var p = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            memory ? "typed-flow-slice4.json" : "typed-flow-slice4-no-memory.json")))!;
        p["project"]!["initialStateId"] = state;
        return p;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitContextAndInjection(bool memory)
    {
        var p = Fixture(memory);
        var core = p["project"]!["personalityCore"]!.GetValue<string>();
        var interest = p["project"]!["interestProfile"]!.GetValue<string>();
        var reader = new BoundaryMemory();
        var run = await ExecuteAsync(Scenarios[1], memory, new Model(), reader, "fake", TestContext.Current.CancellationToken,
            projectOverride: p, projectRevision: "slice4");
        Assert.True(run.Outcome.Success, run.Outcome.Error);
        Assert.Equal(Engaged, run.Report.CurrentState);
        var generate = Assert.Single(run.Requests, r => r.NodeId.EndsWith("-generation", StringComparison.Ordinal));
        Assert.Null(generate.ResponseSchema);
        Assert.Contains(core, generate.System);
        Assert.DoesNotContain(interest, generate.System);
        foreach (var request in run.Requests.Where(r => r != generate)) Assert.DoesNotContain(core, request.System);
        Assert.Equal(JsonValueKind.Array, generate.Inputs.GetProperty("mensagens").ValueKind);
        Assert.Equal(JsonValueKind.Array, generate.Inputs.GetProperty("historico").ValueKind);
        Assert.True(generate.Inputs.TryGetProperty("ambiente", out _));
        Assert.Equal(memory, generate.Inputs.TryGetProperty("memorias", out var memories));
        Assert.Equal(memory ? 1 : 0, reader.Queries.Count);
        if (memory)
        {
            Assert.Single(memories.EnumerateArray());
            Assert.Contains("Helena", memories.GetRawText());
            Assert.DoesNotContain(reader.Queries[0].Persons[0].ParticipantUuid.ToString(), generate.Inputs.GetRawText());
        }
        Assert.Equal("Uma resposta.", run.Outcome.Outputs["group-generation"]["texto"].GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n ")]
    public async Task EmptyGenerationNeverTransitions(string response)
    {
        var run = await ExecuteAsync(Scenarios[1], false, new Model(response), null, "none",
            TestContext.Current.CancellationToken, projectOverride: Fixture(false), projectRevision: "slice4");
        Assert.False(run.Outcome.Success);
        Assert.Equal(Inactive, run.Report.CurrentState);
        Assert.False(run.Outcome.Outputs.ContainsKey("group-generation"));
        Assert.DoesNotContain(run.Report.Trace, t => t.EventName == "transition-applied");
    }

    [Fact]
    public async Task MemoryFailureDoesNotGenerateOrTransition()
    {
        var run = await ExecuteAsync(Scenarios[1], true, new Model(), new BrokenMemory(), "error",
            TestContext.Current.CancellationToken, projectOverride: Fixture(), projectRevision: "slice4");
        Assert.False(run.Outcome.Success);
        Assert.Equal(Inactive, run.Report.CurrentState);
        Assert.DoesNotContain(run.Requests, r => r.NodeId.EndsWith("-generation", StringComparison.Ordinal));
    }

    internal sealed class Model(string response = "Uma resposta.", string action = "responder") : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => Task.FromResult(
            request.NodeId.EndsWith("-generation", StringComparison.Ordinal) ? response :
            request.NodeId.EndsWith("-situation", StringComparison.Ordinal)
                ? """{"ambiente":"casual","envolvimento":"direto","ritmo":"aberto","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena chamou Filomena"}"""
                : JsonSerializer.Serialize(new { acao = action, continuar_acompanhando = false, motivo = "chamada_direta" }));
    }
    private sealed class BrokenMemory : IMemoryReader
    {
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token) =>
            throw new HttpRequestException("fixture unavailable");
    }
    private sealed class RejectExternal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("No external services during backend tests.");
    }
}
