using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Cortex.Core.Tests.Assertions;

// Throwaway remote experiment. No Boundary inputs or runtime registration.
public sealed class AssertionExtractionLive
{
    internal static JsonElement Inputs(JsonElement corpus, JsonElement item) => JsonSerializer.SerializeToElement(new
    {
        candidate = item.GetProperty("candidate").GetString(),
        candidate_length_utf16 = item.GetProperty("candidate").GetString()!.Length,
        recent_context = corpus.GetProperty("contexts").GetProperty(item.GetProperty("context").GetString()!).GetString(),
        subjects = corpus.GetProperty("subjects")
    });

    [Fact]
    public async Task Run_one_fixture_then_require_semantic_review()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_ASSERTION_SPIKE") != "1", "Opt-in isolated synthetic Assertion experiment.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_SPIKE_ROOT") ?? throw new InvalidOperationException("Root required.");
        var id = Environment.GetEnvironmentVariable("FILOMENA_ASSERTION_CASE") ?? throw new InvalidOperationException("One fixture required for manual gate review.");
        var model = Environment.GetEnvironmentVariable("FILOMENA_ASSERTION_MODEL") ?? "gpt-5.6-terra";
        Assert.Contains(model, new[] { "gpt-5.6-terra", "gpt-5.6-luna" });
        var token = TestContext.Current.CancellationToken;
        var source = await File.ReadAllTextAsync(Path.Combine(root, "tests/fixtures/assertion-extraction-spike.json"), token);
        using var document = JsonDocument.Parse(source);
        var corpus = document.RootElement;
        var item = corpus.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        var subjects = corpus.GetProperty("subjects").EnumerateArray().Select(s => s.GetProperty("id").GetString()!).ToArray();
        var inputs = Inputs(corpus, item);
        var schema = AssertionExtraction.Schema(subjects);
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source + AssertionExtraction.Prompt + schema.GetRawText())));
        var previous = new List<JsonElement>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "docs"), "assertion-extraction-attempts-*.jsonl"))
            foreach (var line in await File.ReadAllLinesAsync(file, token))
            {
                using var row = JsonDocument.Parse(line);
                if (row.RootElement.GetProperty("type").GetString() == "attempt"
                    && row.RootElement.GetProperty("id").GetString() == id
                    && row.RootElement.GetProperty("model").GetString() == model)
                {
                    Assert.Equal(fingerprint, row.RootElement.GetProperty("fingerprint").GetString());
                    previous.Add(row.RootElement.Clone());
                }
            }
        var valid = previous.Count(r => r.GetProperty("status").GetString() == "valid");
        var failures = previous.Count - valid;
        Assert.True(failures < 3, "Three failed attempts for this fixture/model: stop, no further requests.");
        Assert.True(valid < 5, "Fixture already has five valid observations; do not rerun it.");
        var path = Path.Combine(root, "docs", $"assertion-extraction-attempts-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        await using var log = new StreamWriter(path) { AutoFlush = true };
        await log.WriteLineAsync(JsonSerializer.Serialize(new
        {
            type = "metadata",
            fingerprint,
            model,
            effort = "medium",
            maxOutputTokens = 4096,
            automaticRetries = 0,
            prompt = AssertionExtraction.Prompt,
            schema,
            id,
            inputs,
            expectedHuman = item.GetProperty("expectedHuman"),
            previousAttempts = previous.Count
        }));
        TestContext.Current.TestOutputHelper!.WriteLine("ASSERTION_RESULTS=" + path);
        for (var repetition = valid + 1; repetition <= 5; repetition++)
        {
            using var usage = new UsageHandler();
            using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
            var completion = new OpenAiFlowCompletion(http, () => key, "medium", 4096);
            var request = new FlowCompletionRequest("assertion-extraction-spike-v1", AssertionExtraction.Prompt, inputs,
                schema, model, "openai", null, ReasoningEffort: "medium", MaxOutputTokens: 4096);
            string status = "valid", error = "";
            string? raw = null;
            JsonElement? output = null;
            var start = Stopwatch.GetTimestamp();
            try
            {
                raw = await completion.CompleteAsync(request, token);
                error = AssertionExtraction.Validate(raw, item.GetProperty("candidate").GetString()!, subjects) ?? "";
                using var parsed = JsonDocument.Parse(raw);
                output = parsed.RootElement.Clone();
                if (error.Length > 0) status = "invalid_output";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (HttpRequestException e)
            {
                status = e.StatusCode is null ? "transport_failure" : "provider_failure";
                error = $"http_{(int?)e.StatusCode}:{e.HttpRequestError}";
            }
            catch (OperationCanceledException e)
            {
                status = e.InnerException is TimeoutException ? "timeout" : "transport_failure";
                error = "transport_cancelled";
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException)
            {
                status = "invalid_output"; error = e.GetType().Name;
            }
            var row = new
            {
                type = "attempt",
                fingerprint,
                attemptId = Guid.NewGuid(),
                id,
                model,
                repetition,
                attemptNumber = previous.Count + repetition - valid,
                status,
                error,
                candidate = item.GetProperty("candidate").GetString(),
                output,
                raw,
                milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                usage = usage.Last
            };
            await log.WriteLineAsync(JsonSerializer.Serialize(row));
            if (status != "valid") throw new InvalidOperationException("Attempt preserved. Explicit resume required; no automatic retry.");
        }
        // Never automatically advance to another fixture. Review five semantic outputs first.
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
