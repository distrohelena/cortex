using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GenerationLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_TEST_GENERATION") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in loaded Gemma, simulation only.")]
    public async Task PersonalityMatrixWithoutTransport()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new LmStudioOptions
        {
            Endpoint = new Uri(Environment.GetEnvironmentVariable("FILOMENA_EXPERIMENT_LM_ENDPOINT") ?? "http://127.0.0.1:1234/v1/"),
            GenerationTemperature = 0.3,
            GenerationMaxTokens = 8192,
            RequestTimeoutSeconds = Timeout.Infinite
        };
        using var usage = new ParticipationSemanticsLiveTests.UsageObserver(new HttpClientHandler());
        using var http = new HttpClient(usage) { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m => m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        var model = new FlowModelCompletion(new LmStudioClient(http, options), options.Model);
        var failures = new List<string>();
        for (var repetition = 1; repetition <= 2; repetition++)
            foreach (var scenario in GenerationExperiments.Cases)
                foreach (var memory in scenario.Id is 8 or 9 ? new[] { true, false } : new[] { true })
                {
                    var before = usage.Calls.Count;
                    var (result, _) = await GenerationExperiments.Execute(scenario, memory, model,
                        memory ? new GenerationExperiments.Memories() : null, memory ? "fake" : "none", token);
                    TestContext.Current.TestOutputHelper!.WriteLine("GENERATION_CASE=" + JsonSerializer.Serialize(new
                    {
                        repetition,
                        result,
                        usage = usage.Calls.Skip(before).ToArray()
                    }));
                    if (!result.Success) failures.Add($"{scenario.Id}/{repetition}/{memory}: {result.Error}");
                }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in generation-only comparison; upstream signals are explicitly fake.")]
    public async Task GenerationOnlyWithFixedPresenceForBlockedCasesAndMemoryPairs()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new LmStudioOptions
        {
            Endpoint = new Uri(Environment.GetEnvironmentVariable("FILOMENA_EXPERIMENT_LM_ENDPOINT") ?? "http://127.0.0.1:1234/v1/"),
            GenerationTemperature = 0.3,
            GenerationMaxTokens = 8192,
            RequestTimeoutSeconds = Timeout.Infinite
        };
        using var http = new HttpClient { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m => m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        var model = new GenerationOnly(new FlowModelCompletion(new LmStudioClient(http, options), options.Model));
        foreach (var repetition in new[] { 1, 2 })
            foreach (var scenario in GenerationExperiments.Cases.Where(s => s.Id is 1 or 5 or 8 or 9))
                foreach (var memory in scenario.Id is 8 or 9 ? new[] { true, false } : new[] { false })
                {
                    var (result, _) = await GenerationExperiments.Execute(scenario, memory, model,
                        memory ? new GenerationExperiments.Memories() : null, memory ? "fake" : "none", token);
                    TestContext.Current.TestOutputHelper!.WriteLine("GENERATION_FOCUSED=" + JsonSerializer.Serialize(new
                    {
                        repetition,
                        upstream = "fixed fake situation and RESPONDER; not an end-to-end FSM result",
                        actualLlmCalls = 1,
                        result
                    }));
                    Assert.True(result.Success, result.Error);
                    Assert.Equal("WOULD_SEND", result.Signal);
                }
    }

    private sealed class GenerationOnly(IFlowCompletion generation) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            request.NodeId.EndsWith("-generation", StringComparison.Ordinal)
                ? generation.CompleteAsync(request, token)
                : new GenerationTests.Model().CompleteAsync(request, token);
    }
}
