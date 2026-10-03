using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference;
using Xunit;

namespace Cortex.Inference.Tests;

public sealed class ValidatedCompletionTests
{
    static readonly FlowCompletionRequest Request = new("names", "literal names", JsonSerializer.SerializeToElement(new { }), null, null, null, null);
    static string Validate(string text) => text == "valid" ? text : throw new InvalidDataException("Unknown segment ID: s99");

    [Fact]
    public async Task LocalSuccessDoesNotCallRemote()
    {
        var provider = new Fake("valid");
        var result = await new ValidatedCompletion(provider, "gemma", "luna").CompleteAsync(Request, Validate, TestContext.Current.CancellationToken);
        Assert.Equal("valid", result.Value); Assert.Single(result.Attempts); Assert.Equal("lmstudio", provider.Requests[0].Provider);
    }

    [Fact]
    public async Task RepairsOnceWithSpecificValidationErrorThenFallsBack()
    {
        var provider = new Fake("bad", "bad", "valid");
        var result = await new ValidatedCompletion(provider, "gemma", "luna").CompleteAsync(Request, Validate, TestContext.Current.CancellationToken);
        Assert.Equal(3, result.Attempts.Count);
        Assert.Contains("Unknown segment ID", provider.Requests[1].Correction);
        Assert.Equal("openai", provider.Requests[2].Provider); Assert.Equal("luna", provider.Requests[2].Model);
    }

    [Fact]
    public async Task TimeoutFallsBackButCallerCancellationDoesNot()
    {
        var provider = new Fake(new OperationCanceledException(), "valid");
        Assert.Equal(2, (await new ValidatedCompletion(provider, "gemma", "luna").CompleteAsync(Request, Validate, TestContext.Current.CancellationToken)).Attempts.Count);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var cancelled = new Fake("valid");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ValidatedCompletion(cancelled, "gemma", "luna").CompleteAsync(Request, Validate, stop.Token));
        Assert.Empty(cancelled.Requests);
    }

    [Fact]
    public async Task InvalidFallbackFailsAndNeverReturnsPartialOutput()
    {
        var provider = new Fake("bad", "bad", "bad");
        var error = await Assert.ThrowsAsync<ValidatedCompletionException>(() => new ValidatedCompletion(provider, "gemma", "luna").CompleteAsync(Request, Validate, TestContext.Current.CancellationToken));
        Assert.Equal(3, error.Attempts.Count);
    }

    [Fact]
    public async Task SuccessfulLocalRepairDoesNotUseRemote()
    {
        var provider = new Fake("bad", "valid");
        var result = await new ValidatedCompletion(provider, "gemma", "luna").CompleteAsync(Request, Validate, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Attempts.Count); Assert.All(provider.Requests, r => Assert.Equal("lmstudio", r.Provider));
    }

    [Fact]
    public async Task LocalOnlyBenchmarkNeverUsesLuna()
    {
        var provider = new Fake("bad", "bad");
        await Assert.ThrowsAsync<ValidatedCompletionException>(() => new ValidatedCompletion(provider, "gemma", "luna", allowFallback: false).CompleteAsync(Request, Validate, TestContext.Current.CancellationToken));
        Assert.All(provider.Requests, r => Assert.Equal("lmstudio", r.Provider));
    }

    sealed class Fake(params object[] outputs) : IFlowCompletion
    {
        public List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request); var value = outputs[Requests.Count - 1];
            return value is Exception error ? Task.FromException<string>(error) : Task.FromResult((string)value);
        }
    }
}
