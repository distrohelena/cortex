using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Inference.SystemOne;
using Cortex.Video.Editing;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class JevClipMatcherTests
{
    [Theory]
    [InlineData(MatchMode.Semantic)]
    [InlineData(MatchMode.Intent)]
    public async Task UsesExistingNativeDecisionContractWithoutSendingTimecodes(MatchMode mode)
    {
        using var handler = new Decisions(); using var http = new HttpClient(handler);
        var matcher = new JevClipMatcher(new SystemOneClient(http, new("https://fixture.invalid"), () => "fixture", true, SystemOneProbabilityPolicy.Jev113TwoDecimals));
        var candidates = TakeCandidates.Build(ClipResolutionTests.Transcript());
        var result = await matcher.MatchAsync(new() { Speaker = "narrator", Match = mode, Meaning = "Save settings." }, candidates, TestContext.Current.CancellationToken);
        Assert.Equal(ClipMatchOutcome.Match, result[0].Outcome);
        Assert.Equal(ClipMatchOutcome.NoMatch, result[1].Outcome);
        Assert.Equal(1, result[0].Score);
        Assert.Equal(candidates[0].Id, result[0].CandidateId);
        Assert.Equal("jev-1.13.0", handler.Request.GetProperty("model").GetString());
        Assert.Equal(mode.ToString().ToLowerInvariant(), handler.Request.GetProperty("state").GetProperty("request").GetProperty("mode").GetString());
        Assert.False(handler.Request.GetProperty("state").GetProperty("candidates")[0].TryGetProperty("source", out _));
        Assert.Equal(4, handler.Request.GetProperty("questions").EnumerateObject().Count());
    }

    [Theory]
    [InlineData(.79, false)]
    [InlineData(1, true)]
    public async Task LowConfidenceOrContradictoryResponsesRemainInconclusive(double confidence, bool contradictory)
    {
        using var handler = new Decisions { Confidence = confidence, Contradictory = contradictory }; using var http = new HttpClient(handler);
        var matcher = new JevClipMatcher(new SystemOneClient(http, new("https://fixture.invalid"), () => "fixture", true, SystemOneProbabilityPolicy.Jev113TwoDecimals));
        var result = await matcher.MatchAsync(new() { Speaker = "narrator", Match = MatchMode.Semantic, Meaning = "Save settings." }, TakeCandidates.Build(ClipResolutionTests.Transcript()), TestContext.Current.CancellationToken);
        Assert.All(result, a => Assert.Equal(ClipMatchOutcome.Uncertain, a.Outcome));
    }

    [Fact]
    public async Task MissingCredentialsFailInsteadOfGeneratingMissingRecordingTasks()
    {
        var matcher = new ConfiguredJevClipMatcher("https://fixture.invalid", "CORTEX_MISSING_" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => matcher.MatchAsync(new() { Speaker = "narrator", Match = MatchMode.Semantic, Meaning = "Save settings." }, TakeCandidates.Build(ClipResolutionTests.Transcript()), TestContext.Current.CancellationToken));
    }

    private sealed class Decisions : HttpMessageHandler
    {
        public JsonElement Request { get; private set; }
        public double Confidence { get; init; } = 1;
        public bool Contradictory { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)); Request = document.RootElement.Clone();
            var answers = new JsonObject();
            foreach (var question in Request.GetProperty("questions").EnumerateObject())
            {
                var first = question.Name.EndsWith("0001", StringComparison.Ordinal);
                if (question.Value.GetProperty("type").GetString() == "choice")
                    answers[question.Name] = JsonSerializer.SerializeToNode(new { type = "choice", choice = first ? "match" : "no_match", confidence = Confidence, probabilities = new { match = first ? 1 : 0, no_match = first ? 0 : 1, uncertain = 0 }, source_in = 999 });
                else
                {
                    var probabilities = new JsonObject(); var legend = new JsonObject(); var level = first != Contradictory ? 3 : 0; var i = 0;
                    foreach (var item in question.Value.GetProperty("criteria").EnumerateArray()) { probabilities[i.ToString()] = i == level ? 1 : 0; legend[i.ToString()] = JsonNode.Parse(item.GetRawText()); i++; }
                    answers[question.Name] = new JsonObject { ["type"] = "score", ["score"] = level, ["confidence"] = Confidence, ["probabilities"] = probabilities, ["legend"] = legend };
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["model"] = "jev-1.13.0", ["answers"] = answers, ["usage"] = new JsonObject { ["input_tokens"] = 12, ["output_tokens"] = 9 } }.ToJsonString()) };
        }
    }
}
