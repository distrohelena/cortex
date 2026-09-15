using System.Net.Http.Json;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Memory;
using Filomena.Llm.LmStudio;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class GenerationCalibrationLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_TEST_CALIBRATION41") == "1";
    internal static readonly Scenario[] Cases =
    [
        .. GenerationExperiments.Cases.Take(10),
        new(11, "K erro simples", Engaged, [new("Gabriel", "achei o problema, era cache de DNS.")]),
        new(12, "L erro inequívoco", Engaged, [new("Gabriel", "Não: dois pares de objetos somam quatro, não cinco. Você somou 2 + 2 errado.")],
            [new("Filomena", "2 + 2 é igual a 5.")]),
        new(13, "M contexto insuficiente", Engaged, [new("Helena", "eu fiz aquilo e agora quebrou.")]),
        new(14, "N flerte amigável", Engaged, [new("Helena", "Filomena, você tem um charme perigoso. Aposto um café que consigo te fazer sorrir. Aceita o desafio?")])
    ];

    [Fact(SkipUnless = nameof(Enabled), Skip = "One selected generation, manual review between boundary cases. Never sends.")]
    public async Task SelectedGenerationWithFixedUpstream()
    {
        var token = TestContext.Current.CancellationToken;
        var selected = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_CALIBRATION41_CASE") ?? "8");
        var repetition = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_CALIBRATION41_REPETITION") ?? "1");
        var boundary = Environment.GetEnvironmentVariable("FILOMENA_CALIBRATION41_BOUNDARY") != "off";
        var scenario = Cases.Single(c => c.Id == selected);
        var options = new LmStudioOptions
        {
            Endpoint = new Uri("http://127.0.0.1:1234/v1/"),
            GenerationTemperature = 0.3,
            GenerationMaxTokens = 8192,
            RequestTimeoutSeconds = Timeout.Infinite
        };
        using var usage = new ParticipationSemanticsLiveTests.UsageObserver(new HttpClientHandler());
        using var http = new HttpClient(usage) { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m => m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        var generation = new FlowModelCompletion(new LmStudioClient(http, options), options.Model);
        var model = new FixedUpstream(generation, scenario.Id);
        var (result, run) = await GenerationExperiments.Execute(scenario, true, model, new SelectedMemory(boundary),
            "fake-separated", token, GenerationCalibrationTests.Fixture(true, scenario.InitialState));
        var input = run.Requests.Single(r => r.NodeId == "group-generation").Inputs;
        var generated = new
        {
            repetition,
            boundary,
            upstream = "fixed fake RESPONDER and situation; generation only is real",
            actualLlmCalls = usage.Calls.Count,
            result,
            limits = input.GetProperty("limites"),
            social = input.GetProperty("memorias_sociais"),
            usage = usage.Calls
        };
        TestContext.Current.TestOutputHelper!.WriteLine("CALIBRATION41=" + JsonSerializer.Serialize(generated));
        Assert.True(result.Success, result.Error);
        Assert.Equal("WOULD_SEND", result.Signal);
        Assert.Single(usage.Calls);
        // Boundary compliance is manually reviewed, not inferred from a regex or another LLM.
    }

    private sealed class SelectedMemory(bool boundary) : IMemoryReader
    {
        public async Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token) =>
            (await new GenerationExperiments.Memories().QueryAsync(query, token))
                .Where(m => boundary || m.Category != MemoryCategory.Boundary).ToArray();
    }

    private sealed class FixedUpstream(IFlowCompletion generation, int scenario) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (request.NodeId == "group-generation") return generation.CompleteAsync(request, token);
            if (request.NodeId == "group-situation") return Task.FromResult(JsonSerializer.Serialize(new
            {
                ambiente = scenario is 2 or 3 or 7 or 10 or 11 or 12 ? "tecnico" : scenario is 4 or 8 ? "serio" : scenario == 6 ? "conflito" : "casual",
                envolvimento = "direto",
                ritmo = "aberto",
                necessita_cuidado = scenario is 4 or 6 or 8,
                oportunidade_social = "forte",
                resumo = scenario == 13 ? "Cancelamento quebrou depois de uma alteração." :
                    scenario == 10 ? "Helena quer cancelar globalmente todas as conversas." : "Interlocutor conversa com Filomena."
            }));
            return new GenerationTests.Model().CompleteAsync(request, token);
        }
    }
}
