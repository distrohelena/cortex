using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Filomena.Core.Tests.Boundaries;

public sealed class PersonalAttributionContinuationLive
{
    [Fact]
    public async Task Resume_missing_observations_without_automatic_retry()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_ATTRIBUTION_CONTINUE") != "1", "Opt-in explicit continuation only.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_SPIKE_ROOT") ?? throw new InvalidOperationException("Root required.");
        var prior = (Environment.GetEnvironmentVariable("FILOMENA_SPIKE_PRIOR") ?? throw new InvalidOperationException("Explicit prior files required.")).Split(';');
        var token = TestContext.Current.CancellationToken;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var corpus = JsonSerializer.Deserialize<PersonalAttributionSpikeLive.Corpus>(
            await File.ReadAllTextAsync(Path.Combine(root, "tests/fixtures/personal-attribution-spike.json"), token), options)!;
        var rows = new List<PersonalAttributionSpikeLive.Row>();
        JsonElement? originalMetadata = null;
        foreach (var file in prior)
            foreach (var line in await File.ReadAllLinesAsync(Path.Combine(root, file), token))
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("Strategy", out _))
                    rows.Add(doc.RootElement.Deserialize<PersonalAttributionSpikeLive.Row>(options)!);
                else if (doc.RootElement.TryGetProperty("contracts", out _)) originalMetadata ??= doc.RootElement.Clone();
            }
        Assert.NotNull(originalMetadata);
        var meta = originalMetadata.Value;
        Assert.Equal(corpus.Source, meta.GetProperty("source").GetString());
        Assert.Equal(corpus.Context, meta.GetProperty("context").GetString());
        Assert.Equal(37, corpus.Cases.Length);
        foreach (var contract in meta.GetProperty("contracts").EnumerateArray())
        {
            var strategy = contract.GetProperty("strategy").GetString()!;
            Assert.Equal(contract.GetProperty("prompt").GetString(), PersonalAttributionSpike.Contract(strategy));
            Assert.True(JsonElement.DeepEquals(contract.GetProperty("schema"), PersonalAttributionSpike.Schema(strategy).ToJsonSchema()));
        }
        foreach (var row in rows)
        {
            var item = Assert.Single(corpus.Cases, c => c.Id == row.Id);
            Assert.Equal(item.Candidate, row.Candidate);
            Assert.Equal(item.Allow, row.ExpectedAllow);
        }
        Assert.All(rows.Where(r => r.Status == "valid").GroupBy(r => (r.Id, r.Strategy, r.Repetition)),
            group => Assert.Single(group));
        var path = Path.Combine(root, "docs", $"personal-attribution-continuation-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        await using var log = new StreamWriter(path) { AutoFlush = true };
        await log.WriteLineAsync(JsonSerializer.Serialize(new
        {
            type = "continuation",
            prior,
            automaticRetries = 0,
            maxTechnicalFailuresPerFixtureStrategy = 3,
            targetValidPerFixtureStrategy = 5,
            originalMetadata = meta
        }));
        TestContext.Current.TestOutputHelper!.WriteLine("CONTINUATION=" + path);
        for (var repetition = 1; repetition <= 5; repetition++)
            foreach (var item in corpus.Cases)
            {
                var pending = PersonalAttributionResume.Pending(rows, item.Id, repetition);
                if (pending.Length == 0) continue;
                var inputs = await PersonalAttributionSpikeLive.Inputs(corpus.Source, corpus.Context, item.Candidate, token);
                var pair = await Task.WhenAll(pending.Select(s => PersonalAttributionSpikeLive.Call(s, item, repetition, inputs, key!, token)));
                foreach (var row in pair)
                {
                    var json = JsonSerializer.SerializeToNode(row)!;
                    json["AttemptNumber"] = rows.Count(r => r.Id == row.Id && r.Strategy == row.Strategy && r.Repetition == row.Repetition) + 1;
                    json["AttemptId"] = Guid.NewGuid().ToString("N");
                    await log.WriteLineAsync(json.ToJsonString());
                    rows.Add(row);
                }
                // Persist results, then return control to the experiment operator. No retry loop.
                if (pair.Any(r => r.Status != "valid"))
                    throw new InvalidOperationException("Non-valid attempt persisted; explicit operator continuation required.");
            }
        Assert.Equal(370, rows.Count(r => r.Status == "valid"));
    }
}
