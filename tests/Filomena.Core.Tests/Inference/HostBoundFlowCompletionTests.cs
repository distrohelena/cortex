using System.Collections.Concurrent;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Inference;
using Xunit;
using static Filomena.Core.Tests.Inference.HostInferenceTestFixtures;

namespace Filomena.Core.Tests.Inference;

public sealed class HostBoundFlowCompletionTests
{
    private static IFlowCompletion Bind(Completion adapter, Action<InferenceAttemptObservation>? observe = null)
    {
        var authority = new HostInferenceAuthority(observe: observe);
        return authority.Bind(authority.Grant(Scope, "principal", [Profile(adapter)]), Scope, "p", Correlation);
    }

    [Fact]
    public async Task StructuredDecisionWithoutSessionPersonaOrMemory()
    {
        var observations = new List<InferenceAttemptObservation>();
        var adapter = new Completion((request, _) =>
        {
            Assert.Equal("test-model", request.Model);
            Assert.Equal("private prompt", request.System);
            Assert.Equal("forged", request.Inputs.GetProperty("project_id").GetString());
            return Task.FromResult("{\"choice\":\"accept\"}");
        });
        var bound = Bind(adapter, observations.Add);
        Assert.Equal("{\"choice\":\"accept\"}", await bound.CompleteAsync(Request, TestContext.Current.CancellationToken));
        var observation = Assert.Single(observations);
        Assert.Equal(Scope.ProjectId, observation.ProjectId);
        Assert.Equal(InferenceAttemptStage.Completed, observation.Stage);
        Assert.Null(observation.ObservedModel);
        Assert.Null(observation.Cost);
        Assert.Null(observation.InputTokens);
        Assert.Null(observation.OutputTokens);
        Assert.DoesNotContain("private prompt", JsonSerializer.Serialize(observation));
        Assert.Equal(1, adapter.Calls);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("provider")]
    [InlineData("node")]
    [InlineData("schema")]
    [InlineData("effort")]
    [InlineData("tokens")]
    public async Task ProfileOverrideRejectedBeforeAdapter(string field)
    {
        var adapter = new Completion();
        var request = field switch
        {
            "model" => Request with { Model = "other" },
            "provider" => Request with { Provider = "cloud" },
            "node" => Request with { NodeId = "admin" },
            "schema" => Request with { ResponseSchema = null },
            "effort" => Request with { ReasoningEffort = null },
            _ => Request with { MaxOutputTokens = 4096 }
        };
        await Assert.ThrowsAsync<InferenceDeniedException>(() => Bind(adapter).CompleteAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(0, adapter.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"choice\":\"unauthorized\"}")]
    [InlineData("{\"choice\":\"accept\",\"tool\":\"send\"}")]
    public async Task InvalidOutputIsNotCompleted(string output)
    {
        var events = new List<InferenceAttemptObservation>();
        var adapter = new Completion((_, _) => Task.FromResult(output));
        await Assert.ThrowsAsync<InvalidDataException>(() => Bind(adapter, events.Add).CompleteAsync(Request, TestContext.Current.CancellationToken));
        Assert.Equal(InferenceAttemptStage.InvalidOutput, Assert.Single(events).Stage);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task CancelledBeforeInvocationCallsZero()
    {
        var adapter = new Completion();
        var events = new List<InferenceAttemptObservation>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bind(adapter, events.Add).CompleteAsync(Request, cts.Token));
        Assert.Equal(0, adapter.Calls);
        Assert.Equal(InferenceAttemptStage.CancelledBeforeInvocation, Assert.Single(events).Stage);
    }

    [Fact]
    public async Task CancellationAfterInvocationRemainsUncertain()
    {
        using var cts = new CancellationTokenSource();
        var events = new List<InferenceAttemptObservation>();
        var adapter = new Completion((_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(""); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bind(adapter, events.Add).CompleteAsync(Request, cts.Token));
        Assert.Equal(InferenceAttemptStage.CancelledAfterInvocation, Assert.Single(events).Stage);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task ExceptionDoesNotExposePayloadOrRetry()
    {
        var events = new List<InferenceAttemptObservation>();
        var adapter = new Completion((_, _) => throw new HttpRequestException("secret response private prompt"));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Bind(adapter, events.Add).CompleteAsync(Request, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("secret", error.ToString());
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(events));
        Assert.Equal(InferenceAttemptStage.FailedAfterInvocation, Assert.Single(events).Stage);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task TimeoutKeepsProviderFailureCategoryWithoutPrivateDetails()
    {
        var adapter = new Completion((_, _) => throw new TimeoutException("private endpoint"));
        var error = await Assert.ThrowsAsync<TimeoutException>(() => Bind(adapter).CompleteAsync(Request, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("private", error.ToString());
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task ConcurrentAttemptsAndCorrectionsHaveDistinctIds()
    {
        var events = new ConcurrentBag<InferenceAttemptObservation>();
        var adapter = new Completion();
        var bound = Bind(adapter, events.Add);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => bound.CompleteAsync(Request with { Correction = i == 0 ? null : "format" }, TestContext.Current.CancellationToken)));
        Assert.Equal(8, adapter.Calls);
        Assert.Equal(8, events.Select(e => e.AttemptId).Distinct().Count());
    }

    [Fact]
    public async Task ObservationFailureDoesNotReplayAdapter()
    {
        var adapter = new Completion();
        var bound = Bind(adapter, _ => throw new InvalidOperationException("sink failed"));
        Assert.Contains("accept", await bound.CompleteAsync(Request, TestContext.Current.CancellationToken));
        Assert.True(Assert.IsType<HostBoundFlowCompletion>(bound).ObservationFailed);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task ObservationFailureIsVisibleToSessionHost()
    {
        var adapter = new Completion();
        var authority = new HostInferenceAuthority(observe: _ => throw new InvalidOperationException("sink"));
        var bound = authority.Bind(authority.Grant(Scope, "principal", [Profile(adapter)]), Scope, "p", Correlation);
        await bound.CompleteAsync(Request, TestContext.Current.CancellationToken);
        Assert.True(authority.ObservationFailed);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task NullOutputIsNeverAValidatedResultEvenWithTextValidator()
    {
        var adapter = new Completion((_, _) => Task.FromResult<string>(null!));
        var events = new List<InferenceAttemptObservation>();
        var authority = new HostInferenceAuthority(observe: events.Add);
        var grant = authority.Grant(Scope, "principal", [new HostInferenceProfile("p", "r", adapter, Request, _ => { })]);
        await Assert.ThrowsAsync<InvalidDataException>(() => authority.Bind(grant, Scope, "p", Correlation).CompleteAsync(Request, TestContext.Current.CancellationToken));
        Assert.Equal(InferenceAttemptStage.InvalidOutput, Assert.Single(events).Stage);
    }

    [Fact]
    public async Task RuntimeValidationDoesNotClaimCompleted()
    {
        var events = new List<InferenceAttemptObservation>();
        var adapter = new Completion((_, _) => Task.FromResult("invalid json for runner correction"));
        var authority = new HostInferenceAuthority(observe: events.Add);
        var grant = authority.Grant(Scope, "principal", [HostInferenceProfile.ForRuntime("p", "r", adapter, Request)]);
        Assert.Contains("invalid", await authority.Bind(grant, Scope, "p", Correlation).CompleteAsync(Request, TestContext.Current.CancellationToken));
        Assert.Equal(InferenceAttemptStage.AdapterReturnedPendingValidation, Assert.Single(events).Stage);
    }

    [Fact]
    public async Task ExplicitRuntimeSchemaVariantsDoNotPermitFutureSchemas()
    {
        var adapter = new Completion();
        var variants = new List<JsonElement> { JsonSerializer.Deserialize<JsonElement>("{\"type\":\"boolean\"}") };
        var profile = HostInferenceProfile.ForRuntime("p", "r", adapter, Request, variants);
        variants.Clear();
        var authority = new HostInferenceAuthority();
        var grant = authority.Grant(Scope, "principal", [profile]);
        var bound = authority.Bind(grant, Scope, "p", Correlation);
        await bound.CompleteAsync(Request with { ResponseSchema = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"boolean\"}") }, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InferenceDeniedException>(() => bound.CompleteAsync(Request with
        { ResponseSchema = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"number\"}") }, TestContext.Current.CancellationToken));
        Assert.Equal(1, adapter.Calls);
    }
}
