using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class MultiDetectorTests
{
    [Fact]
    public async Task Empty_or_duplicate_detector_configuration_fails_without_calls()
    {
        using var limit = new SemaphoreSlim(2);
        var count = 0;
        var fake = new Completion((_, _) => { count++; return Task.FromResult("{}"); });
        foreach (var definitions in new DetectorDefinition[][] { [], [Definition("a"), Definition("a")] })
        {
            var result = await MultiDetectorComposition.ExecuteAsync(definitions, JsonSerializer.SerializeToElement(new { }), _ => fake, limit, CancellationToken.None);
            Assert.Equal(DetectionOutcome.EnforcementFailure, result.Verdict.Outcome);
        }
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("{\"extra\":true}")]
    [InlineData("{\"extra\":true,\"extra\":false}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task Invalid_output_never_becomes_semantic_block(string raw)
    {
        using var limit = new SemaphoreSlim(1);
        var fake = new Completion((_, _) => Task.FromResult(raw));
        var result = await MultiDetectorComposition.ExecuteAsync([Definition("one")], JsonSerializer.SerializeToElement(new { }), _ => fake, limit, CancellationToken.None);
        Assert.Equal(DetectionOutcome.EnforcementFailure, result.Verdict.Outcome);
        Assert.Empty(result.Verdict.Feedback);
    }

    [Fact]
    public void Complement_can_rescue_principal_allow_without_majority_vote()
    {
        var result = MultiDetectorComposition.Aggregate([
            new(DetectionOutcome.Allow, "principal", ""),
            new(DetectionOutcome.Block, "complement", "Feedback complementar.")]);
        Assert.Equal(DetectionOutcome.Block, result.Outcome);
        Assert.Equal("Feedback complementar.", result.Feedback);
    }

    [Theory]
    [InlineData("Allow", "Allow", "Allow")]
    [InlineData("Block", "Allow", "Block")]
    [InlineData("Allow", "Block", "Block")]
    [InlineData("Block", "Block", "Block")]
    [InlineData("EnforcementFailure", "Block", "EnforcementFailure")]
    [InlineData("Allow", "EnforcementFailure", "EnforcementFailure")]
    [InlineData("Block", "EnforcementFailure", "EnforcementFailure")]
    [InlineData("EnforcementFailure", "Allow", "EnforcementFailure")]
    [InlineData("EnforcementFailure", "EnforcementFailure", "EnforcementFailure")]
    public void Failure_precedes_semantic_block_and_only_block_has_regeneration_feedback(string a, string b, string expected)
    {
        var result = MultiDetectorComposition.Aggregate([
            new(Enum.Parse<DetectionOutcome>(a), "a", "same feedback"),
            new(Enum.Parse<DetectionOutcome>(b), "b", "same feedback")]);
        Assert.Equal(expected, result.Outcome.ToString());
        Assert.Equal(expected == "Block" ? "same feedback" : "", result.Feedback);
    }

    [Fact]
    public void Unknown_internal_outcome_cannot_become_allow()
    {
        var result = MultiDetectorComposition.Aggregate([new((DetectionOutcome)999, "bad", "")]);
        Assert.Equal(DetectionOutcome.EnforcementFailure, result.Outcome);
    }

    [Fact]
    public async Task Independent_calls_start_before_either_finishes_and_keep_full_context()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var input = JsonSerializer.SerializeToElement(new { resposta = "Whole. Candidate.", limite = new { subject = "synthetic" }, mensagens = Array.Empty<string>() });
        var fake = new Completion(async (request, token) =>
        {
            Assert.Equal(input.GetRawText(), request.Inputs.GetRawText());
            Assert.Equal(new[] { "resposta", "limite", "mensagens" }, request.Inputs.EnumerateObject().Select(x => x.Name));
            if (Interlocked.Increment(ref count) == 2) arrived.SetResult();
            await release.Task.WaitAsync(token);
            return "{}";
        });
        using var limit = new SemaphoreSlim(2);
        var task = MultiDetectorComposition.ExecuteAsync([Definition("principal"), Definition("complement")], input, _ => fake, limit, CancellationToken.None);
        Assert.True(arrived.Task.IsCompleted);
        release.SetResult();
        var result = await task;
        Assert.Equal(DetectionOutcome.Allow, result.Verdict.Outcome);
        Assert.Equal(2, result.Calls.Count);
    }

    [Fact]
    public async Task Provider_and_invalid_output_are_failures_not_semantic_accusations()
    {
        using var limit = new SemaphoreSlim(2);
        foreach (var exception in new Exception[] { new HttpRequestException("private"), new TimeoutException("private"), new InvalidDataException("private"), new JsonException("private") })
        {
            var fake = new Completion((_, _) => Task.FromException<string>(exception));
            var result = await MultiDetectorComposition.ExecuteAsync([Definition("one")], JsonSerializer.SerializeToElement(new { }), _ => fake, limit, CancellationToken.None);
            Assert.Equal(DetectionOutcome.EnforcementFailure, result.Verdict.Outcome);
            Assert.Empty(result.Verdict.Feedback);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(result));
        }
    }

    [Fact]
    public async Task Shared_limit_and_cancellation_are_respected()
    {
        using var limit = new SemaphoreSlim(1);
        using var cancel = new CancellationTokenSource();
        var count = 0;
        var fake = new Completion(async (_, token) => { Interlocked.Increment(ref count); await Task.Delay(Timeout.Infinite, token); return "{}"; });
        var task = MultiDetectorComposition.ExecuteAsync([Definition("a"), Definition("b")], JsonSerializer.SerializeToElement(new { }), _ => fake, limit, cancel.Token);
        Assert.Equal(1, count);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, limit.CurrentCount);
    }

    private static DetectorDefinition Definition(string id) => new(id, "prompt", new ObjectType(new Dictionary<string, FlowField>()),
        _ => new(DetectionOutcome.Allow, "safe", ""), "openai", "gpt-5.6-luna", "medium", 1024);
    private sealed class Completion(Func<FlowCompletionRequest, CancellationToken, Task<string>> action) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }
}
