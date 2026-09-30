using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Inference;
using Xunit;

namespace Filomena.Core.Tests.Inference;

public sealed class HostInferenceAuthorityTests
{
    [Fact]
    public void GrantFromAnotherAuthorityRejected()
    {
        var first = new HostInferenceAuthority();
        var second = new HostInferenceAuthority();
        var grant = first.Grant(HostInferenceTestFixtures.Scope, "host-user", [HostInferenceTestFixtures.Profile(new Stub())]);
        Assert.Throws<InferenceDeniedException>(() => second.Bind(grant, HostInferenceTestFixtures.Scope, "p", HostInferenceTestFixtures.Correlation));
        Assert.Throws<InferenceDeniedException>(() => second.Revoke(grant));
        Assert.NotNull(first.Bind(grant, HostInferenceTestFixtures.Scope, "p", HostInferenceTestFixtures.Correlation));
    }

    [Fact]
    public async Task WrongScopeAndUnknownProfileRejectedBeforeAdapter()
    {
        var adapter = new HostInferenceTestFixtures.Completion();
        var authority = new HostInferenceAuthority();
        var grant = authority.Grant(HostInferenceTestFixtures.Scope, "same-person", [HostInferenceTestFixtures.Profile(adapter)]);
        Assert.Throws<InferenceDeniedException>(() => authority.Bind(grant, new("application-b"), "p", HostInferenceTestFixtures.Correlation));
        Assert.Throws<InferenceDeniedException>(() => authority.Bind(grant, HostInferenceTestFixtures.Scope, "foreign-profile", HostInferenceTestFixtures.Correlation));
        Assert.Equal(0, adapter.Calls);
        var allowed = authority.Bind(grant, HostInferenceTestFixtures.Scope, "p", HostInferenceTestFixtures.Correlation);
        Assert.Contains("accept", await allowed.CompleteAsync(HostInferenceTestFixtures.Request, TestContext.Current.CancellationToken));
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task BoundCompletionCannotSurviveRevocation()
    {
        var adapter = new HostInferenceTestFixtures.Completion();
        var authority = new HostInferenceAuthority();
        var profiles = new List<HostInferenceProfile> { HostInferenceTestFixtures.Profile(adapter) };
        var grant = authority.Grant(HostInferenceTestFixtures.Scope, "principal", profiles);
        profiles.Clear();
        var bound = authority.Bind(grant, HostInferenceTestFixtures.Scope, "p", HostInferenceTestFixtures.Correlation);
        authority.Revoke(grant);
        authority.Revoke(grant);
        await Assert.ThrowsAsync<InferenceDeniedException>(() => bound.CompleteAsync(HostInferenceTestFixtures.Request, TestContext.Current.CancellationToken));
        Assert.Throws<InferenceDeniedException>(() => authority.Bind(grant, HostInferenceTestFixtures.Scope, "p", HostInferenceTestFixtures.Correlation));
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public async Task RevocationDoesNotUndoAdmittedCallButBlocksNextCall()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new HostInferenceTestFixtures.Completion(async (_, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return "{\"choice\":\"accept\"}";
        });
        var authority = new HostInferenceAuthority();
        var grant = authority.Grant(HostInferenceTestFixtures.Scope, "principal", [HostInferenceTestFixtures.Profile(adapter)]);
        var bound = authority.Bind(grant, HostInferenceTestFixtures.Scope, "p", HostInferenceTestFixtures.Correlation);
        var running = bound.CompleteAsync(HostInferenceTestFixtures.Request, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        authority.Revoke(grant);
        release.SetResult();
        Assert.Contains("accept", await running);
        await Assert.ThrowsAsync<InferenceDeniedException>(() => bound.CompleteAsync(HostInferenceTestFixtures.Request, TestContext.Current.CancellationToken));
        Assert.Equal(1, adapter.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyScopeRejected(string value) => Assert.Throws<ArgumentException>(() => new InferenceScope(value));

    [Fact]
    public void ProfileCopiesMutableInputs()
    {
        using var document = JsonDocument.Parse("{\"type\":\"boolean\"}");
        var profile = new HostInferenceProfile("p", "r1", new Stub(),
            new("step", "system", JsonSerializer.SerializeToElement(new { }), document.RootElement, "model", "provider", null), _ => { });
        document.Dispose();
        Assert.Equal("boolean", profile.ExpectedRequest.ResponseSchema!.Value.GetProperty("type").GetString());
    }

    [Fact]
    public void SchemaEqualityDoesNotDependOnPropertyOrder()
    {
        var request = new FlowCompletionRequest("step", "", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.Deserialize<JsonElement>("{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}"), null, null, null);
        var profile = new HostInferenceProfile("p", "r", new Stub(), request, _ => { });
        profile.ValidateRequest(request with { ResponseSchema = JsonSerializer.Deserialize<JsonElement>("{\"enum\":[\"a\",\"b\"],\"type\":\"string\"}") });
        Assert.Throws<InferenceDeniedException>(() => profile.ValidateRequest(request with { Model = "other" }));
    }

    private sealed class Stub : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
