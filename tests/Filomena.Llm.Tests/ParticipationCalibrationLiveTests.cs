using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.LmStudio;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class ParticipationCalibrationLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_TEST_CALIBRATION") == "1";
    internal static readonly Scenario[] Cases = [.. Scenarios,
        new(9, "interesse legítimo", Inactive, [new("Gabriel", "comecei um projeto de engine em C++"), new("Bia", "sério?"), new("Gabriel", "sim mas ainda não sei se vou continuar")]),
        new(10, "conversa banal", Inactive, [new("Bia", "já chegou?"), new("Helena", "sim"), new("Bia", "beleza")])];

    internal sealed record Result(int Repetition, string Variant, int Scenario, string Name, string InitialState,
        int BatchSize, JsonElement? Situation, JsonElement? Decision, string FinalState, string Signal,
        bool Success, string? Error, int LlmCalls, int CorrectiveCalls, double SituationMs, double DecisionMs,
        double TotalLlmMs, int MemoryItems, object[] Path);

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in loaded Gemma calibration, no sending, no services started.")]
    public async Task PairedOneVersusTwoCallsWithVisibleFsm()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new LmStudioOptions
        {
            Endpoint = new Uri(Environment.GetEnvironmentVariable("FILOMENA_EXPERIMENT_LM_ENDPOINT") ?? "http://127.0.0.1:1234/v1/"),
            GenerationTemperature = 0.1,
            GenerationMaxTokens = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_EXPERIMENT_MAX_TOKENS") ?? "4096", System.Globalization.CultureInfo.InvariantCulture),
            RequestTimeoutSeconds = Timeout.Infinite
        };
        using var http = new HttpClient { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m => m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        IFlowCompletion model = new FlowModelCompletion(new LmStudioClient(http, options), options.Model);
        var results = new List<Result>();
        var output = TestContext.Current.TestOutputHelper!;
        var repetitions = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_CALIBRATION_REPETITIONS") ?? "2", System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(repetitions, 1, 10);
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            foreach (var scenario in Cases)
            {
                // Counterbalance pair order, fixed model/options. No personality goldens.
                var order = (repetition + scenario.Id) % 2 == 0 ? new[] { "two", "single" } : new[] { "single", "two" };
                foreach (var variant in order)
                    await Record(scenario, variant, repetition);
            }
            // Separate exploratory style-boundary experiment; excluded from A/B latency estimates.
            await Record(Scenarios[7], "memory", repetition);
        }
        output.WriteLine("CALIBRATION_REPORT=" + JsonSerializer.Serialize(new
        {
            model = options.Model,
            temperature = options.GenerationTemperature,
            maxTokens = options.GenerationMaxTokens,
            repetitions,
            noSending = true,
            noGraphitiWrites = true,
            results
        }));
        Assert.All(results, r => Assert.True(r.Success, $"{r.Scenario}/{r.Variant}/{r.Repetition}: {r.Error}"));

        async Task Record(Scenario scenario, string variant, int repetition)
        {
            output.WriteLine($"START repetition={repetition} scenario={scenario.Id} variant={variant}");
            var memory = variant == "memory";
            var run = await ExecuteAsync(scenario, memory, model, memory ? new BoundaryMemory() : null,
                memory ? "synthetic-boundary" : "none", token,
                projectOverride: ParticipationCalibrationTests.Project(scenario.InitialState, variant));
            var r = run.Report;
            JsonElement? Select(JsonElement? source, params string[] names) => source is { } json
                ? JsonSerializer.SerializeToElement(json.EnumerateObject().Where(p => names.Contains(p.Name)).ToDictionary(p => p.Name, p => p.Value)) : null;
            var result = new Result(repetition, variant, scenario.Id, scenario.Name, r.InitialState, r.BatchSize,
                Select(variant == "single" ? r.Presence : r.Situation, "ambiente", "envolvimento", "ritmo", "necessita_cuidado", "oportunidade_social", "resumo"),
                Select(r.Presence, "acao", "continuar_acompanhando", "motivo"), r.CurrentState, r.Signal, r.Success, r.Error,
                r.LlmCalls, r.Calls.Count(c => c.Correction), r.SituationMilliseconds, r.PresenceMilliseconds,
                r.Calls.Sum(c => c.Milliseconds), r.MemoryItems,
                r.Trace.Where(t => t.EventName is "node-completed" or "node-failed" or "transition-applied")
                    .Select(t => (object)new { t.EventName, t.NodeId, t.Exit, t.BatchStateId, t.BatchStateVersion, t.CurrentSessionStateId, t.CurrentSessionStateVersion }).ToArray());
            results.Add(result);
            output.WriteLine("CALIBRATION_CASE=" + JsonSerializer.Serialize(result));
        }
    }
}
