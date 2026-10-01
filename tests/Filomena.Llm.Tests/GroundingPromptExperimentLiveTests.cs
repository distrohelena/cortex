using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GroundingPromptExperimentLiveTests
{
    [Fact]
    public async Task Selected_fixture_A_B_five_valid_without_enforcement()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_GROUNDING_AB") != "1", "Synthetic paid A/B opt-in.");
        var token = TestContext.Current.CancellationToken;
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_GROUNDING_ROOT") ?? throw new InvalidOperationException();
        var selected = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_GROUNDING_CASE") ?? "1");
        var data = await File.ReadAllTextAsync(Path.Combine(root, "docs/grounding-ab-inputs.json"), token);
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data)));
        using var json = JsonDocument.Parse(data);
        var doc = json.RootElement;
        Assert.Equal("gpt-5.6-luna", doc.GetProperty("model").GetString());
        Assert.Equal("medium", doc.GetProperty("effort").GetString());
        var item = doc.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == selected);
        var mode = item.GetProperty("mode").GetString()!;
        var request = new FlowCompletionRequest("group-" + mode,
            doc.GetProperty(mode == "generation" ? "generationPrompt" : "regenerationPrompt").GetString()!,
            item.GetProperty("inputs").Clone(), null, "gpt-5.6-luna", "openai", null, "medium", doc.GetProperty("maxOutputTokens").GetInt32());
        var previous = new List<JsonElement>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "docs"), "grounding-ab-attempts-*.jsonl"))
            foreach (var line in await File.ReadAllLinesAsync(file, token))
            {
                using var d = JsonDocument.Parse(line);
                Assert.Equal(fingerprint, d.RootElement.GetProperty("fingerprint").GetString());
                previous.Add(d.RootElement.Clone());
            }
        var path = Path.Combine(root, "docs", $"grounding-ab-attempts-{selected}-{Guid.NewGuid():N}.jsonl");
        await using var writer = new StreamWriter(path) { AutoFlush = true };
        for (var repetition = 1; repetition <= 5; repetition++)
            foreach (var arm in (repetition + selected) % 2 == 0 ? new[] { "A", "B" } : new[] { "B", "A" })
            {
                var cell = previous.Where(p => p.GetProperty("id").GetInt32() == selected && p.GetProperty("arm").GetString() == arm).ToArray();
                if (cell.Any(p => p.GetProperty("repetition").GetInt32() == repetition && p.GetProperty("status").GetString() == "valid")) continue;
                Assert.True(cell.Count(p => p.GetProperty("status").GetString() != "valid") < 3, "Technical failure ceiling reached.");
                var actual = GroundingPromptExperiment.Variant(request, arm, doc.GetProperty("clause").GetString()!);
                using var usage = new Usage();
                using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
                var completion = new OpenAiFlowCompletion(http, () => key);
                string? output = null;
                var status = "valid";
                string? failure = null;
                var start = Stopwatch.GetTimestamp();
                try { output = await completion.CompleteAsync(actual, token); }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    status = GeneratorComparisonLiveTests.Classify(e);
                    failure = e is HttpRequestException he ? $"HTTP:{(int?)he.StatusCode}" : e.GetType().Name;
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    fingerprint,
                    id = selected,
                    arm,
                    repetition,
                    mode,
                    blindId = Guid.NewGuid().ToString("N")[..12],
                    status,
                    failure,
                    output,
                    milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    usage = usage.Value,
                    model = actual.Model,
                    effort = actual.ReasoningEffort,
                    maxOutputTokens = actual.MaxOutputTokens,
                    system = actual.System,
                    inputs = actual.Inputs
                }));
                Assert.True(status == "valid", "Attempt preserved. Explicit resume required after technical failure.");
            }
        TestContext.Current.TestOutputHelper!.WriteLine("GROUNDING_FILE=" + path);
    }

    private sealed class Usage() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        internal JsonElement? Value;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (doc.RootElement.TryGetProperty("usage", out var usage)) Value = usage.Clone();
            }
            return response;
        }
    }
}
