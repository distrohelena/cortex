using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Inference.LmStudio;
using Cortex.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GenerationGraphitiLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_TEST_GENERATION_GRAPHITI") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in existing Graphiti + loaded Gemma, read-only simulation.")]
    public async Task ExistingMemoryOnResponderPath()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new LmStudioOptions
        {
            Endpoint = new Uri(Environment.GetEnvironmentVariable("FILOMENA_EXPERIMENT_LM_ENDPOINT") ?? "http://127.0.0.1:1234/v1/"),
            GenerationMaxTokens = 8192,
            GenerationTemperature = 0.3,
            RequestTimeoutSeconds = Timeout.Infinite
        };
        using var http = new HttpClient { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m => m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        await using var graphiti = new GraphitiReadClient(new Uri(
            Environment.GetEnvironmentVariable("FILOMENA_GRAPHITI_MCP_ENDPOINT") ?? "http://127.0.0.1:8000/mcp"), TimeSpan.FromSeconds(30));
        var raw = await graphiti.SearchFactsAsync("whatsapp-group-5511981042361-1589823314-g-us", "DRIFT42", 1, token);
        Assert.False(raw.TryGetProperty("error", out _));
        var model = new FlowModelCompletion(new LmStudioClient(http, options), options.Model);
        foreach (var scenario in new[] { GenerationExperiments.Cases[0], GenerationExperiments.Cases[7] })
        {
            var (result, _) = await GenerationExperiments.Execute(scenario, true, model,
                new GraphitiMemoryReader(graphiti), "real-read-only", token);
            // Deliberately omit real memory content: the report records counts and generated speech.
            TestContext.Current.TestOutputHelper!.WriteLine("GENERATION_REAL_MEMORY=" + JsonSerializer.Serialize(result));
            Assert.True(result.Success, result.Error);
            Assert.Equal("WOULD_SEND", result.Signal);
            Assert.InRange(result.MemoryItems, 0, 6);
        }
    }
}
