using System.Security.Cryptography;
using System.Text.Json;
using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Execution;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class TrialResultMeasurementTests
{
    [Fact]
    public void Missing_elapsed_is_not_a_zero_latency_measurement()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            profileSha256 = Candidate,
            inputSha256 = Input,
            output = "private output"
        });
        Assert.Throws<InvalidDataException>(() => TrialMeasurementReader.ReadElapsed(bytes, Hash(bytes), Candidate, Input));
    }

    [Fact]
    public void Valid_artifact_returns_only_elapsed_not_output()
    {
        var bytes = Result().ToArtifact(false).Content.ToArray();
        Assert.Equal(TimeSpan.FromMilliseconds(150), TrialMeasurementReader.ReadElapsed(bytes, Hash(bytes), Candidate, Input));
    }

    [Theory]
    [InlineData("artifact")]
    [InlineData("candidate")]
    [InlineData("input")]
    [InlineData("negative")]
    public void Corrupt_or_wrong_revision_result_cannot_supply_latency(string variant)
    {
        var result = Result();
        if (variant == "negative") result = result with { Elapsed = TimeSpan.FromMilliseconds(-1) };
        var bytes = result.ToArtifact(false).Content.ToArray();
        Assert.Throws<InvalidDataException>(() => TrialMeasurementReader.ReadElapsed(bytes,
            variant == "artifact" ? new string('c', 64) : Hash(bytes),
            variant == "candidate" ? new string('c', 64) : Candidate,
            variant == "input" ? new string('c', 64) : Input));
    }

    private const string Candidate = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Input = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static CompletionEvaluationResult Result() => new(Candidate, Input, "private output", TimeSpan.FromMilliseconds(150));
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
