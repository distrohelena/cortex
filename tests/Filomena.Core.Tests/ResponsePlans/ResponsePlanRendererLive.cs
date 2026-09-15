using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Llm.LmStudio;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Core.Tests.ResponsePlans;

public sealed class ResponsePlanRendererLive
{
    [Fact]
    public async Task Render_one_candidate_then_check_adherence()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_PLAN_RENDER") != "1", "Opt-in synthetic plan renderer only.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_SPIKE_ROOT") ?? throw new InvalidOperationException("Root required.");
        var id = Environment.GetEnvironmentVariable("FILOMENA_PLAN_CASE") ?? throw new InvalidOperationException("Plan required.");
        var repetition = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_RENDER_REPETITION") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(repetition, 1, 10);
        var token = TestContext.Current.CancellationToken;
        var source = await File.ReadAllTextAsync(Path.Combine(root, "tests/fixtures/response-plan-renderer-spike.json"), token);
        using var document = JsonDocument.Parse(source);
        var corpus = document.RootElement;
        var item = corpus.GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
        using var voice = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, corpus.GetProperty("voiceSource").GetString()!), token));
        var project = voice.RootElement.GetProperty("project");
        var core = project.GetProperty("personalityCore").GetString()!;
        var guidance = project.GetProperty("states").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "participation-engaged-v1").GetProperty("participationGuidance").GetString()!;
        var system = ResponsePlanRenderer.SystemPrompt(core, guidance);
        var inputs = ResponsePlanRenderer.Inputs(corpus, item);
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source + system + PlanAdherenceSpike.Prompt + PlanAdherenceSpike.Schema().GetRawText())));
        foreach (var existing in Directory.EnumerateFiles(Path.Combine(root, "docs"), "response-plan-render-*.jsonl"))
            foreach (var line in await File.ReadAllLinesAsync(existing, token))
            {
                using var entry = JsonDocument.Parse(line);
                if (entry.RootElement.GetProperty("type").GetString() == "metadata"
                    && entry.RootElement.GetProperty("id").GetString() == id
                    && entry.RootElement.GetProperty("repetition").GetInt32() == repetition)
                    throw new InvalidOperationException("Render attempt already recorded; do not silently repeat it.");
            }
        using var localHttp = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        var models = await localHttp.GetFromJsonAsync<JsonElement>("/api/v0/models", token);
        var loaded = models.GetProperty("data").EnumerateArray().Where(m => m.GetProperty("state").GetString() == "loaded").ToArray();
        Assert.Single(loaded);
        Assert.Equal("google/gemma-4-e4b", loaded[0].GetProperty("id").GetString());
        var options = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        var renderer = new LmStudioClient(localHttp, options);
        var path = Path.Combine(root, "docs", $"response-plan-render-{id}-{repetition}-{Guid.NewGuid():N}.jsonl");
        await using var log = new StreamWriter(path) { AutoFlush = true };
        await log.WriteLineAsync(JsonSerializer.Serialize(new
        {
            type = "metadata", id, repetition, fingerprint, sourceAttemptId = item.GetProperty("sourceAttemptId"),
            rendererModel = options.Model, rendererTemperature = options.GenerationTemperature, rendererMaxTokens = options.GenerationMaxTokens,
            checkerModel = "gpt-5.6-terra", effort = "medium", maxOutputTokens = 4096,
            system, inputs, checkerPrompt = PlanAdherenceSpike.Prompt, checkerSchema = PlanAdherenceSpike.Schema()
        }));
        var start = Stopwatch.GetTimestamp();
        string candidate;
        try { candidate = await renderer.CompleteResponseAsync(system, inputs.GetRawText(), token); }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or JsonException or OperationCanceledException)
        {
            await log.WriteLineAsync(JsonSerializer.Serialize(new { type = "renderer_failure", id, repetition, error = e.GetType().Name, milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds }));
            throw;
        }
        var rendererMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        await log.WriteLineAsync(JsonSerializer.Serialize(new { type = "candidate", id, repetition, candidate, rendererMs }));
        var checkerInputs = JsonSerializer.SerializeToElement(new { authorized_plan = item.GetProperty("plan"), candidate, targets = corpus.GetProperty("targets") });
        using var usage = new UsageHandler();
        using var remoteHttp = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        var checker = new OpenAiFlowCompletion(remoteHttp, () => key, "medium", 4096);
        var checkerStart = Stopwatch.GetTimestamp();
        string status = "valid", error = "";
        string? raw = null;
        JsonElement? features = null;
        try
        {
            raw = await checker.CompleteAsync(new FlowCompletionRequest("plan-adherence-spike-v1", PlanAdherenceSpike.Prompt, checkerInputs,
                PlanAdherenceSpike.Schema(), "gpt-5.6-terra", "openai", null, ReasoningEffort: "medium", MaxOutputTokens: 4096), token);
            if (PlanAdherenceSpike.Classify(raw) == "invalid_output") { status = "invalid_output"; error = "invalid_schema"; }
            using var parsed = JsonDocument.Parse(raw); features = parsed.RootElement.Clone();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (HttpRequestException e) { status = e.StatusCode is null ? "transport_failure" : "provider_failure"; error = $"http_{(int?)e.StatusCode}"; }
        catch (OperationCanceledException) { status = "timeout"; error = "cancelled"; }
        catch (Exception e) when (e is JsonException or InvalidDataException) { status = "invalid_output"; error = e.GetType().Name; }
        await log.WriteLineAsync(JsonSerializer.Serialize(new
        {
            type = "adherence", id, repetition, status, error, features, raw, usage = usage.Last,
            result = status == "valid" ? PlanAdherenceSpike.Classify(raw!) : "failure",
            checkerMs = Stopwatch.GetElapsedTime(checkerStart).TotalMilliseconds,
            totalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds
        }));
        Assert.Equal("valid", status);
        // Structural success is NOT semantic approval. Manual review occurs outside this test.
    }

    private sealed class UsageHandler() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        internal JsonElement? Last { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (parsed.RootElement.TryGetProperty("usage", out var usage)) Last = usage.Clone();
            }
            return response;
        }
    }
}
