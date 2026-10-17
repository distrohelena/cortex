using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class TranscriptionTests
{
    [Fact]
    public void WhisperOffsetsAreConvertedFromMillisecondsToSeconds()
    {
        using var raw = JsonDocument.Parse("""
            { "transcription": [
              { "offsets": { "from": 0, "to": 1320 }, "text": " Olá, mundo. " },
              { "offsets": { "from": 1320, "to": 3450 }, "text": " Este é um teste." }
            ] }
            """);
        var result = WhisperCppTranscriber.ParseTranscript(raw.RootElement);
        Assert.Equal("Olá, mundo. Este é um teste.", result.Text);
        Assert.Equal(1.32, result.Segments[0].End);
        Assert.Equal(1.32, result.Segments[1].Start);
        Assert.Equal(3.45, result.Segments[1].End);
        FlowDomain.Check(JsonSerializer.SerializeToElement(result, VideoNodes.JsonOptions), VideoNodes.TranscriptType);
    }

    [Theory]
    [InlineData(-1, 400)]
    [InlineData(500, 400)]
    public void InvalidTimestampsAreRejected(int from, int to)
    {
        var raw = JsonSerializer.SerializeToElement(new
        {
            transcription = new[] { new { offsets = new { from, to }, text = "texto" } }
        });
        Assert.Throws<InvalidDataException>(() => WhisperCppTranscriber.ParseTranscript(raw));
    }

    [Fact]
    public async Task ThreeNodesPassRealFilePathAndPublishTypedTranscript()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + " vídeo.mp4");
        await File.WriteAllTextAsync(path, "fixture path only; no inference in unit tests", TestContext.Current.CancellationToken);
        try
        {
            var transcription = new FixtureTranscriber();
            var flow = TypedFlowCompiler.Compile(Graph(path), "video", VideoNodes.CreateHandlers(transcription));
            var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);
            Assert.True(result.Success);
            Assert.Equal(Path.GetFullPath(path), transcription.Received);
            Assert.Equal(1, transcription.Calls);
            Assert.Equal(new[] { "video", "transcribe", "transcript" }, result.Steps.Select(step => step.NodeId));
            var value = result.Outputs["transcript"]["transcript"];
            FlowDomain.Check(value.Value, VideoNodes.TranscriptType);
            Assert.Equal("Olá, mundo.", value.Value.GetProperty("text").GetString());
            Assert.Equal(1.5, value.Value.GetProperty("segments")[0].GetProperty("end").GetDouble());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingVideoFailsAtFileNodeWithoutInvokingTranscriber()
    {
        var transcriber = new FixtureTranscriber();
        var flow = TypedFlowCompiler.Compile(Graph(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4")),
            "video", VideoNodes.CreateHandlers(transcriber));
        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("video", result.Steps.Single().NodeId);
        Assert.Equal(0, transcriber.Calls);
        Assert.False(result.Outputs.ContainsKey("transcript"));
    }

    [Fact]
    public async Task TranscriptionFailureDoesNotPublishTranscriptOrRetry()
    {
        var path = Path.GetTempFileName();
        try
        {
            var transcriber = new FixtureTranscriber { Fail = true };
            var flow = TypedFlowCompiler.Compile(Graph(path), "video", VideoNodes.CreateHandlers(transcriber));
            var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);
            Assert.False(result.Success);
            Assert.Equal(1, transcriber.Calls);
            Assert.Equal("transcribe", result.Steps.Last().NodeId);
            Assert.False(result.Outputs.ContainsKey("transcript"));
        }
        finally { File.Delete(path); }
    }

    private static JsonElement Graph(string videoPath) => JsonSerializer.SerializeToElement(new
    {
        version = 4,
        nodes = new object[]
        {
            new { id = "video", kind = "videoFile", x = 70, y = 130, settings = new { path = videoPath } },
            new { id = "transcribe", kind = "transcribe", x = 410, y = 130 },
            new { id = "transcript", kind = "transcript", x = 750, y = 130 }
        },
        edges = new[]
        {
            new { source = "video", sourcePort = "video", target = "transcribe", targetPort = "video" },
            new { source = "transcribe", sourcePort = "transcript", target = "transcript", targetPort = "transcript" }
        },
        controlEdges = new[]
        {
            new { source = "video", sourcePort = "then", target = "transcribe", targetPort = "exec" },
            new { source = "transcribe", sourcePort = "then", target = "transcript", targetPort = "exec" }
        }
    }, VideoNodes.JsonOptions);

    private sealed class FixtureTranscriber : IVideoTranscriber
    {
        public string? Received { get; private set; }
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public Task<TranscriptResult> TranscribeAsync(string videoPath, CancellationToken token)
        {
            Received = videoPath;
            Calls++;
            if (Fail) throw new InvalidDataException("fixture transcription failure");
            return Task.FromResult(new TranscriptResult("Olá, mundo.", [new(0, 1.5, "Olá, mundo.")]));
        }
    }
}
