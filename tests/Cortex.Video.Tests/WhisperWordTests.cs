using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class WhisperWordTests
{
    [Fact]
    public void FullJsonMergesSubwordTokensAndPunctuationWithoutChangingSegments()
    {
        var raw = JsonSerializer.SerializeToElement(new { transcription = new[] { new {
            offsets = new { from = 0, to = 1200 }, text = " Legalizar é coisa.",
            tokens = new object[] { new { text = "[_BEG_]" }, Token(" Legal",80,200), Token("izar",200,600), Token(" é",600,700), Token(" coisa",700,1100), new { text = "." }, new { text = "[_TT_60]" } }
        } } });
        var result = WhisperCppTranscriber.ParseTranscript(raw);
        Assert.Equal("Legalizar é coisa.", result.Text);
        Assert.Single(result.Segments);
        Assert.Equal(new[] { "Legalizar", "é", "coisa." }, result.Words.Select(word => word.Text));
        Assert.Equal(.08, result.Words[0].Start);
        Assert.Equal(.6, result.Words[0].End);
        Assert.Equal(1.1, result.Words[2].End);
        Assert.Equal("whisper_cpp_token_timestamps", result.WordTimingMethod);
        FlowDomain.Check(JsonSerializer.SerializeToElement(result,VideoNodes.JsonOptions),VideoNodes.TranscriptType);
    }

    [Fact]
    public void UntimedFragmentsNeverReceiveInventedWordOffsets()
    {
        var raw = JsonSerializer.SerializeToElement(new { transcription = new[] { new {
            offsets = new { from = 0, to = 1200 }, text = " Uma explicação.",
            tokens = new object[] { Token(" Uma",80,200), new { text = " explica" }, Token("ção",300,600), new { text = "." } }
        } } });
        var result = WhisperCppTranscriber.ParseTranscript(raw);
        Assert.Equal("Uma", Assert.Single(result.Words).Text);
        Assert.Equal("Uma explicação.", result.Text);
    }

    [Theory]
    [InlineData(-1,200)]
    [InlineData(500,400)]
    [InlineData(0,1300)]
    public void InvalidWordOffsetsAreRejected(int start,int end)
    {
        var raw = JsonSerializer.SerializeToElement(new { transcription = new[] { new {
            offsets = new { from = 0, to = 1200 }, text = " Olá", tokens = new[] { Token(" Olá",start,end) }
        } } });
        Assert.Throws<InvalidDataException>(() => WhisperCppTranscriber.ParseTranscript(raw));
    }

    [Fact]
    public void OlderTranscriptJsonStillAcceptsSegmentOnlyData()
    {
        using var legacy = JsonDocument.Parse("""{"text":"Olá.","segments":[{"start":0,"end":1,"text":"Olá."}]}""");
        FlowDomain.Check(legacy.RootElement,VideoNodes.TranscriptType);
        var result = legacy.RootElement.Deserialize<TranscriptResult>(VideoNodes.JsonOptions)!;
        Assert.Empty(result.Words);
        Assert.Null(result.WordTimingMethod);
    }

    private static object Token(string text,int from,int to) => new { text, offsets = new { from,to } };
}
