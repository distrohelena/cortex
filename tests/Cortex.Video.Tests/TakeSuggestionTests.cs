using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Inference.SystemOne;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class TakeSuggestionTests
{
    [Fact]
    public async Task RepeatedSpeechAndContentAreSeparateDecisionsWithOriginalTimes()
    {
        using var handler = new Decisions();
        using var http = new HttpClient(handler);
        var selector = new JevTakeSuggester(new SystemOneClient(http, new("https://fixture.invalid/systemone"), () => "fixture-key", true, SystemOneProbabilityPolicy.Jev113TwoDecimals));
        var plan = await selector.SuggestAsync(Source(), "Explicar como cortar um vídeo.", TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("draft", plan.Status);
        Assert.Equal(3, plan.Candidates.Count);
        Assert.Equal(plan.Candidates[0].GroupId, plan.Candidates[1].GroupId);
        Assert.NotEqual(plan.Candidates[1].GroupId, plan.Candidates[2].GroupId);
        Assert.False(plan.Candidates[0].Keep);
        Assert.True(plan.Candidates[1].Keep);
        Assert.False(plan.Candidates[2].Keep);
        var clip = Assert.Single(plan.ProposedTimeline);
        Assert.Equal(3, clip.SourceStart);
        Assert.Equal(5, clip.SourceEnd);
        Assert.Equal(0, clip.Start);
        Assert.Equal(2, clip.End);
        Assert.Equal("transcriptOnly", plan.AssessmentBasis);
        Assert.Equal(64, plan.SourceTranscriptHash.Length);
        FlowDomain.Check(JsonSerializer.SerializeToElement(plan, VideoNodes.JsonOptions), TakeSuggestionNode.EditPlanType);
    }

    [Fact]
    public async Task InvalidOrUnorderedSourceFailsBeforeAnyModelCall()
    {
        using var handler = new Decisions(); using var http = new HttpClient(handler);
        var selector = new JevTakeSuggester(new SystemOneClient(http, new("https://fixture.invalid"), () => "fixture", true));
        await Assert.ThrowsAsync<InvalidDataException>(() => selector.SuggestAsync(new("x", [new(3, 4, "Primeira."), new(0, 2, "Segunda.")]), "Objetivo", TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task MusicOnlyProducesEmptyDraftWithoutModelCall()
    {
        using var handler = new Decisions(); using var http = new HttpClient(handler);
        var selector = new JevTakeSuggester(new SystemOneClient(http, new("https://fixture.invalid"), () => "fixture", true));
        var plan = await selector.SuggestAsync(new("[Música]", [new(0, 3, "[Música]")]), "Objetivo", TestContext.Current.CancellationToken);
        Assert.Empty(plan.Candidates); Assert.Empty(plan.ProposedTimeline); Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task WeakRepeatEvidenceDoesNotMergeTheTakes()
    {
        using var handler = new Decisions { SameProbability = .55 }; using var http = new HttpClient(handler);
        var selector = new JevTakeSuggester(new SystemOneClient(http, new("https://fixture.invalid"), () => "fixture", true, SystemOneProbabilityPolicy.Jev113TwoDecimals));
        var plan = await selector.SuggestAsync(Source(), "Objetivo", TestContext.Current.CancellationToken);
        Assert.NotEqual(plan.Candidates[0].GroupId, plan.Candidates[1].GroupId);
    }

    [Fact]
    public async Task RepeatGroupsRequireEvidenceForEveryPair()
    {
        using var handler = new Decisions(); using var http = new HttpClient(handler);
        var selector = new JevTakeSuggester(new SystemOneClient(http, new("https://fixture.invalid"), () => "fixture", true, SystemOneProbabilityPolicy.Jev113TwoDecimals));
        var transcript = new TranscriptResult("", [
            new(0, 1, "Como cortar um vídeo no editor com precisão."),
            new(2, 3, "Como cortar um vídeo no editor."),
            new(4, 5, "Como cortar um vídeo no editor sem perdas.")
        ]);
        var plan = await selector.SuggestAsync(transcript, "Objetivo", TestContext.Current.CancellationToken);
        Assert.Equal(2, plan.RepeatEvidence.Count);
        Assert.Equal(plan.Candidates[0].GroupId, plan.Candidates[1].GroupId);
        Assert.NotEqual(plan.Candidates[0].GroupId, plan.Candidates[2].GroupId);
    }

    [Fact]
    public async Task ProviderFailureIsNotRetriedOrConvertedIntoAnEmptyPlan()
    {
        using var handler = new Decisions { Fail = true }; using var http = new HttpClient(handler);
        var selector = new JevTakeSuggester(new SystemOneClient(http, new("https://fixture.invalid"), () => "fixture", true));
        await Assert.ThrowsAsync<HttpRequestException>(() => selector.SuggestAsync(Source(), "Objetivo", TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void PhraseCandidatesJoinWhisperChunksWithoutInventingWordTimes()
    {
        var source = new TranscriptResult("", [new(0, 1, "Vou explicar"), new(1, 2, "os cortes."), new(3, 4, "Outra frase.")]);
        var candidates = TakeCandidates.Build(source);
        Assert.Equal(2, candidates.Count);
        Assert.Equal("Vou explicar os cortes.", candidates[0].Text);
        Assert.Equal(0, candidates[0].Source.Start);
        Assert.Equal(2, candidates[0].Source.End);
        Assert.Equal(new[] { 0, 1 }, candidates[0].Source.SegmentIds);
    }

    [Fact]
    public async Task FourNodeGraphPublishesTypedEditableDraftThroughExistingRunner()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var handler = new Decisions(); using var http = new HttpClient(handler);
            var suggester = new JevTakeSuggester(new SystemOneClient(http, new("https://fixture.invalid"), () => "fixture", true, SystemOneProbabilityPolicy.Jev113TwoDecimals));
            var graph = JsonSerializer.SerializeToElement(new
            {
                version = 4,
                nodes = new object[] { new { id = "video", kind = "videoFile", settings = new { path } }, new { id = "transcribe", kind = "transcribe" }, new { id = "transcript", kind = "transcript" }, new { id = "takes", kind = "suggestTakes", settings = new { goal = "Explicar cortes de vídeo" } } },
                edges = new[] { new { source = "video", sourcePort = "video", target = "transcribe", targetPort = "video" }, new { source = "transcribe", sourcePort = "transcript", target = "transcript", targetPort = "transcript" }, new { source = "transcript", sourcePort = "transcript", target = "takes", targetPort = "transcript" } },
                controlEdges = new[] { new { source = "video", sourcePort = "then", target = "transcribe", targetPort = "exec" }, new { source = "transcribe", sourcePort = "then", target = "transcript", targetPort = "exec" }, new { source = "transcript", sourcePort = "then", target = "takes", targetPort = "exec" } }
            });
            var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(graph, "video", VideoNodes.CreateHandlers(new TranscriptFixture(), suggester)), TestContext.Current.CancellationToken);
            Assert.True(result.Success);
            Assert.Equal(new[] { "video", "transcribe", "transcript", "takes" }, result.Steps.Select(step => step.NodeId));
            var draft = result.Outputs["takes"]["editPlan"];
            FlowDomain.Check(draft.Value, TakeSuggestionNode.EditPlanType);
            Assert.Equal("draft", draft.Value.GetProperty("status").GetString());
            var editable = JsonNode.Parse(draft.Value.GetRawText())!;
            editable["candidates"]![0]!["keep"] = true;
            Assert.True(editable["candidates"]![0]!["keep"]!.GetValue<bool>());
            Assert.Equal(0, draft.Value.GetProperty("candidates")[0].GetProperty("source").GetProperty("start").GetDouble());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExcessiveCandidatesFailWithoutTruncatingTheRecording()
    {
        var source = new TranscriptResult("", Enumerable.Range(0, 33).Select(i => new TranscriptSegment(i * 2, i * 2 + 1, "Uma fala completa.")).ToArray());
        Assert.Throws<InvalidDataException>(() => TakeCandidates.Build(source));
    }

    private sealed class TranscriptFixture : IVideoTranscriber
    {
        public Task<TranscriptResult> TranscribeAsync(string videoPath, CancellationToken token) => Task.FromResult(Source());
    }

    internal static TranscriptResult Source() => new("", [
        new(0, 2, "Hoje eu vou, ah, explicar como cortar um vídeo."),
        new(3, 5, "Hoje eu vou explicar como cortar um vídeo."),
        new(6, 7.5, "O gato dormiu no sofá.")
    ]);

    private sealed class Decisions : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public double SameProbability { get; init; } = .94;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (Fail) return new(HttpStatusCode.ServiceUnavailable);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var answers = new JsonObject();
            foreach (var question in document.RootElement.GetProperty("questions").EnumerateObject())
            {
                if (question.Value.GetProperty("type").GetString() == "choice")
                    answers[question.Name] = JsonSerializer.SerializeToNode(new { type = "choice", choice = "same", confidence = SameProbability, probabilities = new { same = SameProbability, different = Math.Round(1 - SameProbability, 2) }, start = 999 });
                else
                {
                    var level = question.Name.StartsWith("content_") && question.Name.EndsWith("0003") ? 0 : question.Name.StartsWith("delivery_") && question.Name.EndsWith("0001") ? 1 : 3;
                    var probabilities = new JsonObject(); var legend = new JsonObject(); var index = 0;
                    foreach (var item in question.Value.GetProperty("criteria").EnumerateArray())
                    { probabilities[index.ToString()] = index == level ? 1 : 0; legend[index.ToString()] = JsonNode.Parse(item.GetRawText()); index++; }
                    answers[question.Name] = new JsonObject { ["type"] = "score", ["score"] = level, ["confidence"] = 1, ["probabilities"] = probabilities, ["legend"] = legend, ["start"] = 999 };
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["model"] = "jev-1.13.0", ["answers"] = answers, ["usage"] = new JsonObject { ["input_tokens"] = 12, ["output_tokens"] = 9 } }.ToJsonString()) };
        }
    }
}
