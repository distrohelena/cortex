using System.Net.Http.Json;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Llm.LmStudio;
using Filomena.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ParticipationLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_TEST_PARTICIPATION") == "1";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in observational experiment; requires already loaded Gemma. No sends or memory writes.")]
    public async Task EightScenariosAndExplicitMemoryComparison()
    {
        var options = new LmStudioOptions
        {
            Endpoint = new Uri(Environment.GetEnvironmentVariable("FILOMENA_EXPERIMENT_LM_ENDPOINT") ?? "http://127.0.0.1:1234/v1/"),
            GenerationTemperature = 0.1,
            GenerationMaxTokens = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_EXPERIMENT_MAX_TOKENS") ?? "4096", System.Globalization.CultureInfo.InvariantCulture),
            RequestTimeoutSeconds = Timeout.Infinite
        };
        using var http = new HttpClient { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        // Read-only preflight prevents the completion call from implicitly loading a different model.
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), Token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m =>
            m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        IFlowCompletion completion = new FlowModelCompletion(new LmStudioClient(http, options), options.Model);
        var reports = new List<ParticipationExperiments.Report>();
        var output = TestContext.Current.TestOutputHelper!;
        foreach (var scenario in ParticipationExperiments.Scenarios)
        {
            output.WriteLine($"Starting scenario {scenario.Id}: no memory, two instructions; no sending.");
            var run = await ParticipationExperiments.ExecuteAsync(scenario, false, completion, null, "none", Token);
            Record(run.Report);
        }
        var boundary = await ParticipationExperiments.ExecuteAsync(ParticipationExperiments.Scenarios[7], true,
            completion, new ParticipationExperiments.BoundaryMemory(), "synthetic-boundary", Token);
        Record(boundary.Report);

        if (Environment.GetEnvironmentVariable("FILOMENA_TEST_GRAPHITI") == "1")
        {
            await using var client = new GraphitiReadClient(new Uri(Environment.GetEnvironmentVariable("FILOMENA_GRAPHITI_MCP_ENDPOINT") ?? "http://127.0.0.1:8000/mcp"), TimeSpan.FromSeconds(30));
            // These two scenarios contain only the existing canonical Helena participant.
            // Other named actors in the synthetic set deliberately have no seeded graph identities.
            foreach (var scenario in new[] { ParticipationExperiments.Scenarios[1], ParticipationExperiments.Scenarios[3] })
            {
                var run = await ParticipationExperiments.ExecuteAsync(scenario, true, completion, new GraphitiMemoryReader(client), "graphiti-exact", Token);
                Record(run.Report);
            }
        }
        output.WriteLine("EXPERIMENT_REPORT=" + JsonSerializer.Serialize(new
        {
            model = options.Model,
            temperature = options.GenerationTemperature,
            maxTokens = options.GenerationMaxTokens,
            noSending = true,
            reports
        }));
        // Structural success only. Never assert the model's social choice against a golden personality.
        Assert.All(reports, report => Assert.True(report.Success, $"Scenario {report.Scenario}/{report.MemorySource}: {report.Error}"));

        void Record(ParticipationExperiments.Report report)
        {
            reports.Add(report);
            output.WriteLine("SCENARIO=" + JsonSerializer.Serialize(report));
        }
    }
}
