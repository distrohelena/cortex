using System.Collections.Concurrent;
using System.Text.Json;
using Filomena.Core.Boundaries;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Inference;
using Filomena.Core.Tests.Flows;
using Xunit;

namespace Filomena.Core.Tests.Inference;

public sealed class HostBoundSessionCompletionTests
{
    [Theory]
    [InlineData("account")]
    [InlineData("dm")]
    [InlineData("character")]
    [InlineData("runtime")]
    [InlineData("channel")]
    [InlineData("revision")]
    [InlineData("version")]
    [InlineData("project")]
    public void ExactBindingRejectsEveryMismatch(string fault)
    {
        var key = SessionSliceAcceptanceSupport.Key("exact");
        var adapter = new HostInferenceTestFixtures.Completion();
        var (factory, _, _) = BoundFactory(Project("r1", 1), key, adapter);
        var context = new SessionCompletionContext(key, "execution", "batch", "slice2", 1, "r1");
        context = fault switch
        {
            "account" => context with { Session = key with { AccountId = "other" } },
            "dm" => context with { Session = key with { NativeConversationId = "other" } },
            "character" => context with { Session = key with { CharacterId = "other" } },
            "runtime" => context with { Session = key with { RuntimeId = "other" } },
            "channel" => context with { Session = key with { ChannelId = "other" } },
            "revision" => context with { ProjectRevision = "r2" },
            "version" => context with { ProjectVersion = 2 },
            _ => context with { ProjectId = "other" }
        };
        Assert.Throws<InferenceDeniedException>(() => factory.Create(context));
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public async Task FutureNodeAndMutatedBindingNeverAcquireCoverage()
    {
        var key = SessionSliceAcceptanceSupport.Key("fixed");
        var authority = new HostInferenceAuthority();
        var adapter = new HostInferenceTestFixtures.Completion();
        var grant = authority.Grant(HostInferenceTestFixtures.Scope, "host", [HostInferenceTestFixtures.Profile(adapter)]);
        var nodes = new Dictionary<string, string> { ["step"] = "p" };
        var binding = new HostSessionInferenceBinding(key, "project", 1, "r1", authority, grant, HostInferenceTestFixtures.Scope, nodes);
        var factory = new HostBoundSessionCompletionFactory([binding]);
        nodes.Clear();
        nodes.Add("future", "p");
        Assert.Throws<InferenceDeniedException>(() => new HostBoundSessionCompletionFactory([binding, binding]));
        var bound = factory.Create(new(key, "run", "batch", "project", 1, "r1"));
        await bound.CompleteAsync(HostInferenceTestFixtures.Request, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InferenceDeniedException>(async () => await bound.CompleteAsync(HostInferenceTestFixtures.Request with { NodeId = "future" }, TestContext.Current.CancellationToken));
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task TwoConcurrentSessionsDoNotExchangeCorrelation()
    {
        var project = Project("r1", 1);
        var first = SessionSliceAcceptanceSupport.Key("first");
        var second = SessionSliceAcceptanceSupport.Key("second");
        var observations = new ConcurrentQueue<InferenceAttemptObservation>();
        var adapter = new HostInferenceTestFixtures.Completion((_, _) => Task.FromResult(SessionSliceAcceptanceSupport.Response("inativa", "calar")));
        var (one, _, _) = BoundFactory(project, first, adapter, observations.Enqueue);
        var (two, _, _) = BoundFactory(project, second, adapter, observations.Enqueue);
        await using var runtime = new SessionFlowRuntime(new FakeTimeProvider(), new SelectFactory(context => context.Session == first ? one.Create(context) : two.Create(context)));
        runtime.UpdateProject(project);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, first, "one", SessionEntryKind.Group);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, second, "two", SessionEntryKind.Group);
        await Task.WhenAll(runtime.WaitForIdleAsync(first, TestContext.Current.CancellationToken), runtime.WaitForIdleAsync(second, TestContext.Current.CancellationToken));
        Assert.Equal(2, observations.Select(o => o.RunId).Distinct().Count());
        Assert.Contains(observations, o => o.RunId == Assert.Single(runtime.ReadOutcomes(first)).ExecutionId);
        Assert.Contains(observations, o => o.RunId == Assert.Single(runtime.ReadOutcomes(second)).ExecutionId);
    }

    [Fact]
    public async Task FactoryReceivesCapturedBatchRevision()
    {
        var key = SessionSliceAcceptanceSupport.Key("pin");
        var adapter = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar"), SessionSliceAcceptanceSupport.Response("inativa", "calar"), SessionSliceAcceptanceSupport.Response("inativa", "calar")], 1);
        var contexts = new ConcurrentQueue<SessionCompletionContext>();
        await using var runtime = new SessionFlowRuntime(new FakeTimeProvider(), new Factory(contexts, adapter));
        runtime.UpdateProject(Project("r1", 1));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "first", SessionEntryKind.Group);
        await adapter.BlockStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "queued-r1", SessionEntryKind.Group);
        runtime.UpdateProject(Project("r2", 2));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "queued-r2", SessionEntryKind.Group);
        adapter.Release();
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "r1", "r1", "r2" }, contexts.Select(c => c.ProjectRevision));
        Assert.Equal(new long[] { 1, 1, 2 }, contexts.Select(c => c.ProjectVersion));
        Assert.Equal(runtime.ReadOutcomes(key).Select(o => o.ExecutionId), contexts.Select(c => c.ExecutionId));
        Assert.Equal(3, contexts.Select(c => c.BatchId).Distinct().Count());
    }

    [Fact]
    public async Task SessionBindingMismatchRejectsBeforeCompletion()
    {
        var adapter = new HostInferenceTestFixtures.Completion();
        var key = SessionSliceAcceptanceSupport.Key("allowed");
        var project = Project("r1", 1);
        var (factory, _, _) = BoundFactory(project, key, adapter);
        await using var runtime = new SessionFlowRuntime(new FakeTimeProvider(), factory);
        runtime.UpdateProject(project);
        var wrong = key with { AccountId = "foreign" };
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, wrong, "one", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(wrong, TestContext.Current.CancellationToken);
        Assert.False(Assert.Single(runtime.ReadOutcomes(wrong)).Success);
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public async Task GrantRevokedWhileBatchQueuedPreventsInference()
    {
        var key = SessionSliceAcceptanceSupport.Key("queued");
        var project = Project("r1", 1);
        var adapter = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")], 1);
        var (factory, authority, grant) = BoundFactory(project, key, adapter);
        await using var runtime = new SessionFlowRuntime(new FakeTimeProvider(), factory);
        runtime.UpdateProject(project);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "first", SessionEntryKind.Group);
        await adapter.BlockStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "queued", SessionEntryKind.Group);
        authority.Revoke(grant);
        adapter.Release();
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Single(adapter.Requests);
        Assert.True(runtime.ReadOutcomes(key)[0].Success);
        Assert.False(runtime.ReadOutcomes(key)[1].Success);
    }

    [Fact]
    public async Task DuplicateInboundDoesNotDuplicateInferenceAndNeedsNoEvaluationDatabase()
    {
        var key = SessionSliceAcceptanceSupport.Key("once");
        var project = Project("r1", 1);
        var adapter = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]);
        var (factory, _, _) = BoundFactory(project, key, adapter);
        await using var runtime = new SessionFlowRuntime(new FakeTimeProvider(), factory);
        runtime.UpdateProject(project);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "one", SessionEntryKind.Group);
        Assert.Equal(SessionAdmissionStatus.Duplicate, (await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "one", SessionEntryKind.Group)).Status);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(runtime.ReadOutcomes(key)).Success);
        Assert.Single(adapter.Requests);
    }

    [Fact]
    public async Task CorrectionPreservesRunnerValidationAndUsesNewAttempt()
    {
        var key = SessionSliceAcceptanceSupport.Key("correct");
        var project = Project("r1", 1);
        var adapter = new RecordingCompletion(["invalid", SessionSliceAcceptanceSupport.Response("inativa", "calar")]);
        var events = new List<InferenceAttemptObservation>();
        var (factory, _, _) = BoundFactory(project, key, adapter, events.Add);
        await using var runtime = new SessionFlowRuntime(new FakeTimeProvider(), factory);
        runtime.UpdateProject(project);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "one", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(runtime.ReadOutcomes(key)).Success);
        Assert.Equal(2, adapter.Requests.Count);
        Assert.NotNull(adapter.Requests.Last().Correction);
        Assert.Equal(2, events.Select(e => e.AttemptId).Distinct().Count());
        Assert.All(events, e => Assert.Equal(InferenceAttemptStage.AdapterReturnedPendingValidation, e.Stage));
    }

    internal static SessionFlowProject Project(string revision, long version) =>
        SessionSliceAcceptanceSupport.Project(revision, root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null), version);

    internal static (HostBoundSessionCompletionFactory Factory, HostInferenceAuthority Authority, HostInferenceGrant Grant) BoundFactory(
        SessionFlowProject project, SessionKey key, IFlowCompletion adapter, Action<InferenceAttemptObservation>? observe = null)
    {
        var authority = new HostInferenceAuthority(observe: observe);
        var root = project.Document.TryGetProperty("project", out var nested) ? nested : project.Document;
        var profiles = new Dictionary<string, HostInferenceProfile>();
        foreach (var state in root.GetProperty("states").EnumerateArray())
        {
            var graph = state.GetProperty("graph");
            var entry = graph.GetProperty("nodes").EnumerateArray().First(n => n.GetProperty("kind").GetString() is "dm" or "group").GetProperty("id").GetString()!;
            var flow = TypedFlowCompiler.Compile(graph, entry);
            foreach (var node in flow.Nodes.Values.Where(n => n.Source.Kind == "prompt"))
            {
                var config = node.Source.Config;
                string? Text(string name) => config.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                var expected = new FlowCompletionRequest(node.Source.Id, "", JsonSerializer.SerializeToElement(new { }), TypedFlowRunner.ResponseSchema(node),
                    Text("model"), Text("provider"), null, Text("reasoningEffort"), config.TryGetProperty("maxOutputTokens", out var budget) ? budget.GetInt32() : null);
                if (profiles.TryGetValue(node.Source.Id, out var prior)) prior.ValidateRequest(expected);
                else profiles.Add(node.Source.Id, HostInferenceProfile.ForRuntime(node.Source.Id, project.ProjectRevision, adapter, expected,
                    config.TryGetProperty("enforcementStrategy", out var strategy) && strategy.GetString() == MultiDetectorComposition.Strategy
                        ? [NoTeasingTopicMultiDetectorV1.ComplementSchema.ToJsonSchema()] : null));
            }
        }
        var grant = authority.Grant(HostInferenceTestFixtures.Scope, "host", profiles.Values.ToArray());
        var factory = new HostBoundSessionCompletionFactory([new(key, project.ProjectId, project.ProjectVersion, project.ProjectRevision,
            authority, grant, HostInferenceTestFixtures.Scope, profiles.Keys.ToDictionary(id => id, id => id))]);
        return (factory, authority, grant);
    }

    private sealed class Factory(ConcurrentQueue<SessionCompletionContext> contexts, IFlowCompletion adapter) : ISessionFlowCompletionFactory
    {
        public IFlowCompletion Create(SessionCompletionContext context) { contexts.Enqueue(context); return adapter; }
    }

    private sealed class SelectFactory(Func<SessionCompletionContext, IFlowCompletion> select) : ISessionFlowCompletionFactory
    {
        public IFlowCompletion Create(SessionCompletionContext context) => select(context);
    }
}
