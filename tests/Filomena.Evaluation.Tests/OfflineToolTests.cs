using System.Text.Json;
using Filomena.Evaluation.Tools;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class OfflineToolTests
{
    [Fact]
    public void Hash_command_returns_canonical_revision_without_storing_input()
    {
        var first = OfflineEvaluationTools.Invoke("""{"contractVersion":1,"operation":"revision.hash","payload":{"contractVersion":1,"kind":"test","payload":{"b":2,"a":1}}}""");
        var second = OfflineEvaluationTools.Invoke("""{"contractVersion":1,"operation":"revision.hash","payload":{"contractVersion":1,"kind":"test","payload":{"a":1,"b":2}}}""");
        Assert.True(first.Success);
        Assert.Equal(first.Result!.Value.GetProperty("sha256").GetString(), second.Result!.Value.GetProperty("sha256").GetString());
        Assert.False(first.Result.Value.TryGetProperty("payload", out _));
    }

    [Theory]
    [InlineData("deploy")]
    [InlineData("promote")]
    [InlineData("experiment.run")]
    [InlineData("exec")]
    public void Offline_surface_cannot_start_execution(string operation)
    {
        var result = OfflineEvaluationTools.Invoke(JsonSerializer.Serialize(new { contractVersion = 1, operation, payload = new { } }));
        Assert.False(result.Success);
        Assert.Equal("unsupported_operation", result.ErrorCode);
    }

    [Theory]
    [InlineData("{\"contractVersion\":1,\"operation\":\"revision.hash\",\"operation\":\"deploy\",\"payload\":{}}")]
    [InlineData("{\"contractVersion\":1,\"operation\":\"revision.hash\",\"payload\":null}")]
    [InlineData("{\"contractVersion\":1,\"operation\":\"revision.hash\",\"role\":\"admin\",\"payload\":{}}")]
    [InlineData("not-json-sensitive-value")]
    public void Malformed_or_ambiguous_input_is_rejected_without_echo(string input)
    {
        var result = OfflineEvaluationTools.Invoke(input);
        Assert.False(result.Success);
        Assert.Null(result.Result);
        Assert.DoesNotContain("sensitive", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Version_mismatch_is_explicit()
    {
        Assert.Equal("unsupported_contract_version", OfflineEvaluationTools.Invoke("""{"contractVersion":2,"operation":"revision.hash","payload":{}}""").ErrorCode);
    }

    [Fact]
    public void Oversized_input_is_rejected()
    {
        Assert.Equal("request_too_large", OfflineEvaluationTools.Invoke(new string('x', 1_048_577)).ErrorCode);
    }

    [Fact]
    public void Comparison_preview_is_explicitly_non_persisted()
    {
        var request = JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            operation = "comparison.preview",
            payload = new
            {
                candidates = new[] { "profile-revision-a" },
                cells = new[] { new { caseRevision = "case-revision-1", repetition = 0 } },
                trials = new[] { new { candidateRevision = "profile-revision-a", cell = new { caseRevision = "case-revision-1", repetition = 0 },
                    physicalAttempt = Guid.NewGuid(), outcome = "pass", criticalFailure = false, milliseconds = 10, cost = 0.001m, currency = "USD" } }
            }
        });
        var response = OfflineEvaluationTools.Invoke(request);
        Assert.True(response.Success);
        Assert.False(response.Persisted);
        Assert.Equal("profile-revision-a", response.Result!.Value.GetProperty("paretoCandidates")[0].GetString());
    }
}
