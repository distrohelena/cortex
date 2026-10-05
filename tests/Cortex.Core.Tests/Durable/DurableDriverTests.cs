using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class DurableDriverTests
{
    internal static SessionFlowProject Project(JsonElement? graph = null)
    {
        var flow = IncrementalKernelTests.Flow();
        var rawGraph = graph ?? JsonSerializer.SerializeToElement(new { version = 3, nodes = flow.Nodes.Values.Select(n => n.Source.Config),
            edges = flow.Graph.DataEdges.Select(e => new { source = e.Source.NodeId, sourcePort = e.Source.Port, target = e.Target.NodeId, targetPort = e.Target.Port }),
            controlEdges = flow.Graph.ControlEdges.Select(e => new { source = e.Source.NodeId, sourcePort = e.Source.Port, target = e.Target.NodeId, targetPort = e.Target.Port }) });
        return new("project", 1, "revision", JsonSerializer.SerializeToElement(new { version = 1, stateScope = "session", initialStateId = "initial",
            states = new[] { new { id = "initial", graph = rawGraph } } }));
    }
    internal static async Task<ExecutionStart> PrepareAsync(IExecutionArtifactStore store, SessionFlowProject project)
    {
        var scope = DurableIdentityTests.Scope;
        var projectRef = await store.PutOnceAsync(scope, "project", JsonSerializer.SerializeToElement(project), TestContext.Current.CancellationToken);
        var message = System.Text.Json.Nodes.JsonNode.Parse(IncrementalKernelTests.Message().GetRawText())!;
        message["canal"]!["nativeId"] = scope.Session.NativeConversationId;
        var batch = new ExecutionBatchV1("in", JsonSerializer.SerializeToElement(message), new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(), ["input"], [], false);
        var batchRef = await store.PutOnceAsync(scope, "batch", JsonSerializer.SerializeToElement(batch), TestContext.Current.CancellationToken);
        return new(scope, "batch", projectRef, batchRef, "initial", 0, SessionEntryKind.Dm);
    }
    internal sealed class Bindings(SessionFlowProject project, IFlowCompletion model) : IExecutionHostBindings, ISessionFlowCompletionFactory
    {
        internal int Resolutions { get; private set; }
        internal bool Revoked { get; set; }
        internal Func<FeedbackSubmission, CancellationToken, Task>? FeedbackAuthorization { get; set; }
        public ValueTask<ExecutionHostBinding> ResolveAsync(ArtifactScope scope, CancellationToken token)
        {
            Resolutions++;
            if (Revoked) throw new UnauthorizedAccessException("revoked");
            return ValueTask.FromResult(new ExecutionHostBinding(scope, project.Document, this, new UnavailableMemoryReader(), null, TimeProvider.System) { AuthorizeFeedbackAsync = FeedbackAuthorization });
        }
        public IFlowCompletion Create(SessionCompletionContext context) => model;
    }
    internal static StepCommand Command(ExecutionDescriptor descriptor) => new(new(descriptor.Scope, descriptor.NodeId!, descriptor.Ordinal), descriptor.SnapshotRef);
    [Fact]
    public async Task FeedbackRequiresHostAuthorizationBeforePublishing()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = Project(JsonSerializer.SerializeToElement(HumanFeedbackNodeTests.Graph()));
        var model = new IncrementalKernelTests.ScriptedModel("done"); var bindings = new Bindings(project, model);
        var driver = new FlowExecutionDriver(store, bindings);
        var state = await driver.InitializeAsync(await PrepareAsync(store, project), TestContext.Current.CancellationToken);
        state = await driver.AdvanceAsync(Command(state), TestContext.Current.CancellationToken);
        state = await driver.AdvanceAsync(Command(state), TestContext.Current.CancellationToken);
        var request = state.Feedback!;
        var response = new FeedbackResponseV1("op", request.RequestId, request.RequestDigest, "human answer", "actor");
        var reference = await store.PutOnceAsync(state.Scope, "response", JsonSerializer.SerializeToElement(response), TestContext.Current.CancellationToken);
        var resolution = new FeedbackResolution(request, "answered", reference);
        var command = Command(state);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => driver.ResolveFeedbackAsync(command, resolution, TestContext.Current.CancellationToken));
        Assert.Empty(model.Requests);
        bindings.FeedbackAuthorization = (_, _) => Task.CompletedTask;
        var answered = await driver.ResolveFeedbackAsync(command, resolution, TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.Ready, answered.Status);
        var completed = await driver.AdvanceAsync(Command(answered), TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.Completed, completed.Status);
        Assert.Equal("human answer", Assert.Single(model.Requests).Inputs.GetProperty("feedback").GetString());
        Assert.Equal(answered, await driver.ResolveFeedbackAsync(command, resolution, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task WrongPrivateBatchConversationIsRejected()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = Project(); var start = await PrepareAsync(store, project);
        var foreign = new ExecutionBatchV1("in", IncrementalKernelTests.Message(), new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(), ["input"], [], false);
        var foreignRef = await store.PutOnceAsync(start.Scope, "foreign-batch", JsonSerializer.SerializeToElement(foreign), TestContext.Current.CancellationToken);
        var driver = new FlowExecutionDriver(store, new Bindings(project, new IncrementalKernelTests.Model()));
        await Assert.ThrowsAsync<InvalidDataException>(() => driver.InitializeAsync(start with { BatchRef = foreignRef }, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task CommittedStepRetryReturnsSameSnapshotWithoutCompletionCall()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = Project(); var model = new IncrementalKernelTests.Model(); var bindings = new Bindings(project, model);
        var driver = new FlowExecutionDriver(store, bindings);
        var initialized = await driver.InitializeAsync(await PrepareAsync(store, project), TestContext.Current.CancellationToken);
        var ready = await driver.AdvanceAsync(Command(initialized), TestContext.Current.CancellationToken);
        var command = Command(ready);
        var completed = await driver.AdvanceAsync(command, TestContext.Current.CancellationToken);
        bindings.Revoked = true;
        Assert.Equal(completed, await new FlowExecutionDriver(store, bindings).AdvanceAsync(command, TestContext.Current.CancellationToken));
        Assert.Single(model.Requests);
    }
    [Fact]
    public async Task WrongCursorOrdinalAndScopeAreRejected()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = Project(); var driver = new FlowExecutionDriver(store, new Bindings(project, new IncrementalKernelTests.Model()));
        var initialized = await driver.InitializeAsync(await PrepareAsync(store, project), TestContext.Current.CancellationToken);
        var command = Command(initialized);
        foreach (var wrong in new[] { command with { Key = command.Key with { NodeId = "second" } }, command with { Key = command.Key with { Ordinal = 99 } },
            command with { Key = command.Key with { Scope = command.Key.Scope with { ExecutionId = "foreign" } } } })
            await Assert.ThrowsAsync<InvalidDataException>(() => driver.AdvanceAsync(wrong, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task HostBindingRecheckedForEveryNewStep()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = Project(); var model = new IncrementalKernelTests.Model(); var bindings = new Bindings(project, model); var driver = new FlowExecutionDriver(store, bindings);
        var ready = await driver.InitializeAsync(await PrepareAsync(store, project), TestContext.Current.CancellationToken);
        ready = await driver.AdvanceAsync(Command(ready), TestContext.Current.CancellationToken);
        bindings.Revoked = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => driver.AdvanceAsync(Command(ready), TestContext.Current.CancellationToken));
        Assert.Empty(model.Requests);
    }
    [Fact]
    public async Task ResponseCommitLostRecoversDescriptor()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = Project(); var model = new IncrementalKernelTests.Model(); var driver = new FlowExecutionDriver(store, new Bindings(project, model));
        var ready = await driver.InitializeAsync(await PrepareAsync(store, project), TestContext.Current.CancellationToken);
        var command = Command(ready);
        var result = await driver.AdvanceAsync(command, TestContext.Current.CancellationToken);
        Assert.Equal(result, await driver.RecoverAsync(command.Key, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task PrivatePayloadsAreAbsentFromDescriptors()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = Project(JsonSerializer.SerializeToElement(HumanFeedbackNodeTests.Graph()));
        var driver = new FlowExecutionDriver(store, new Bindings(project, new IncrementalKernelTests.ScriptedModel()));
        var state = await driver.InitializeAsync(await PrepareAsync(store, project), TestContext.Current.CancellationToken);
        state = await driver.AdvanceAsync(Command(state), TestContext.Current.CancellationToken);
        state = await driver.AdvanceAsync(Command(state), TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.WaitingForHuman, state.Status);
        var wire = JsonSerializer.Serialize(state);
        Assert.DoesNotContain("Como devo continuar", wire);
        Assert.DoesNotContain("fixture", wire);
        Assert.NotNull(state.Feedback);
    }
}
