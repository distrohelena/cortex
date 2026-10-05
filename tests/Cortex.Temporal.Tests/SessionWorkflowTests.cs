using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Client;
using Cortex.Temporal.Hosting;
using Cortex.Temporal.Workflows;
using Temporalio.Worker;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class SessionWorkflowTests
{
    internal static SessionFlowProject Project() => new("project", 1, "revision", JsonSerializer.Deserialize<JsonElement>("""
        {"version":1,"stateScope":"session","initialStateId":"initial","states":[{"id":"initial","graph":
        {"version":3,"nodes":[{"id":"in","kind":"dm"},
        {"id":"ai","kind":"prompt","prompt":"Write","outputMode":"text","correctionRetry":false,"inputs":[],"responseFields":[{"id":"text","name":"text","type":"text"}]}],
        "edges":[],"controlEdges":[{"source":"in","sourcePort":"then","target":"ai","targetPort":"exec"}]}}]}
        """));
    internal static SessionKey Session(string conversation) => new("runtime", "character", "channel", "account", conversation);
    internal static SessionMessageEnvelope Message(SessionKey session, string id) => new(id, session.ChannelId, session.AccountId, session.NativeConversationId,
        DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
        JsonSerializer.SerializeToElement(new { texto = "PRIVATE INBOUND " + id, remetente = (object?)null, canal = new { nome = "DM", nativeId = session.NativeConversationId }, reply = (object?)null }));

    [Theory]
    [InlineData("count")] [InlineData("quiet")] [InlineData("max-wait")]
    public async Task BufferClosesOnceUsingCountOrDurableTimer(string mode)
    {
        await using var environment = await TemporalTestEnvironment.StartAsync(timeSkipping: true);
        var directory = Path.Combine(Path.GetTempPath(), "cortex-buffer-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var doc = System.Text.Json.Nodes.JsonNode.Parse(Project().Document.GetRawText())!;
            var graph = doc["states"]![0]!["graph"]!;
            graph["nodes"]!.AsArray().Insert(1, System.Text.Json.Nodes.JsonNode.Parse("""
                {"id":"acc","kind":"accumulate","settings":{"quietSeconds":null,"maxMessages":null,"maxWaitSeconds":null}}
                """));
            graph["nodes"]![1]!["settings"]![mode == "count" ? "maxMessages" : mode == "quiet" ? "quietSeconds" : "maxWaitSeconds"] = mode == "count" ? 2 : 3600;
            graph["edges"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"source":"in","sourcePort":"message","target":"acc","targetPort":"message"}"""));
            graph["controlEdges"]![0]!["target"] = "acc";
            graph["controlEdges"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"source":"acc","sourcePort":"then","target":"ai","targetPort":"exec"}"""));
            var project = Project() with { Document = JsonSerializer.SerializeToElement(doc) };
            await using var store = new FileExecutionArtifactStore(directory, "installation");
            var binding = new Bindings(project); var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), binding);
            var queue = "buffers-" + Guid.NewGuid().ToString("N");
            await using var backend = new TemporalSessionExecutionBackend(environment.Client,
                new("installation", "unused:7233", "default", queue, queue + "-effects", directory) { MaxPendingBatches = 1 }, store, artifacts,
                ActivityPolicyTests.Policy() with { EffectTaskQueue = queue });
            using var worker = new TemporalWorker(environment.Client, new TemporalWorkerOptions(queue).AddWorkflow<SessionWorkflowV1>().AddWorkflow<FlowExecutionWorkflowV1>()
                .AddAllActivities(new ExecutionActivities(new FlowExecutionDriver(store, binding))).AddAllActivities(new SessionActivities(artifacts)));
            await worker.ExecuteAsync(async () =>
            {
                await backend.PublishAsync(project, TestContext.Current.CancellationToken); var session = Session("buffer");
                await backend.ReceiveAsync(session, Message(session, "one"), TestContext.Current.CancellationToken);
                Assert.Equal(1, (await backend.ReadSessionAsync(session, TestContext.Current.CancellationToken))!.OpenBufferCount);
                if (mode == "count") await backend.ReceiveAsync(session, Message(session, "two"), TestContext.Current.CancellationToken);
                else await environment.DelayAsync(TimeSpan.FromHours(2), TestContext.Current.CancellationToken);
                await backend.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
                var outcome = Assert.Single((await backend.ReadOutcomesAsync(session, new(), TestContext.Current.CancellationToken)).Items);
                Assert.True(outcome.Success); Assert.Equal(mode == "count" ? SessionBatchCloseReason.MaxMessages : mode == "quiet" ? SessionBatchCloseReason.QuietPeriod : SessionBatchCloseReason.MaxWait, outcome.CloseReason);
                await environment.DelayAsync(TimeSpan.FromHours(2), TestContext.Current.CancellationToken);
                Assert.Equal(1, binding.FreeCalls);
            }, TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SessionSerializesChildrenWhileAnotherSessionCompletesAndDedupeSurvivesRotation()
    {
        await using var environment = await TemporalTestEnvironment.StartAsync();
        var directory = Path.Combine(Path.GetTempPath(), "cortex-session-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var store = new FileExecutionArtifactStore(directory, "installation");
            var binding = new Bindings(Project()); var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), binding);
            var queue = "session-" + Guid.NewGuid().ToString("N");
            var options = new TemporalRuntimeOptions("installation", "unused:7233", "default", queue, queue + "-effects", directory);
            var policy = ActivityPolicyTests.Policy() with { EffectTaskQueue = queue };
            await using var backend = new TemporalSessionExecutionBackend(environment.Client, options, store, artifacts, policy, rotationEventLimit: 20);
            using var worker = new TemporalWorker(environment.Client, new TemporalWorkerOptions(queue).AddWorkflow<SessionWorkflowV1>().AddWorkflow<FlowExecutionWorkflowV1>()
                .AddAllActivities(new ExecutionActivities(new FlowExecutionDriver(store, binding))).AddAllActivities(new SessionActivities(artifacts)));
            await worker.ExecuteAsync(async () =>
            {
                await backend.PublishAsync(Project(), TestContext.Current.CancellationToken);
                var first = Session("blocked"); var second = Session("free"); var message = Message(first, "one");
                Assert.Equal(SessionAdmissionStatus.Accepted, (await backend.ReceiveAsync(first, message, TestContext.Current.CancellationToken)).Status);
                await binding.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
                Assert.Equal(SessionAdmissionStatus.Accepted, (await backend.ReceiveAsync(first, Message(first, "two"), TestContext.Current.CancellationToken)).Status);
                Assert.Equal(SessionAdmissionStatus.Accepted, (await backend.ReceiveAsync(second, Message(second, "other"), TestContext.Current.CancellationToken)).Status);
                await backend.WaitForIdleAsync(second, TestContext.Current.CancellationToken);
                Assert.Equal(1, binding.FreeCalls); Assert.Equal(1, binding.BlockedCalls);
                binding.Release.TrySetResult();
                await backend.WaitForIdleAsync(first, TestContext.Current.CancellationToken);
                Assert.Equal(2, binding.BlockedCalls);
                var duplicate = await backend.ReceiveAsync(first, message, TestContext.Current.CancellationToken);
                Assert.Equal(SessionAdmissionStatus.Duplicate, duplicate.Status);
                Assert.Equal(2, (await backend.ReadSessionAsync(first, TestContext.Current.CancellationToken))!.AdmissionWatermark);
                var outcomes = await backend.ReadOutcomesAsync(first, new(), TestContext.Current.CancellationToken);
                Assert.Equal(2, outcomes.Items.Count); Assert.All(outcomes.Items, o => Assert.True(o.Success));
                var history = await backend.ReadHistoryAsync(first, new(), TestContext.Current.CancellationToken); Assert.Equal(2, history.Items.Count);
                var handle = environment.Client.GetWorkflowHandle<SessionWorkflowV1>(DurableIdentity.SessionWorkflowId("installation", first));
                var parentHistory = await handle.FetchHistoryAsync();
                var serialized = parentHistory.ToJson();
                Assert.DoesNotContain("PRIVATE INBOUND", serialized);
            }, TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(directory, true); }
    }

    internal sealed class Bindings(SessionFlowProject project) : IExecutionHostBindings, ISessionFlowCompletionFactory
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int BlockedCalls; internal int FreeCalls;
        public ValueTask<ExecutionHostBinding> ResolveAsync(ArtifactScope scope, CancellationToken token) => ValueTask.FromResult(new ExecutionHostBinding(scope, project.Document, this, new UnavailableMemoryReader(), null, TimeProvider.System));
        public IFlowCompletion Create(SessionCompletionContext context) => new Completion(this, context.Session);
        private sealed class Completion(Bindings owner, SessionKey session) : IFlowCompletion
        {
            public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken cancellationToken)
            {
                if (session.NativeConversationId == "blocked") { Interlocked.Increment(ref owner.BlockedCalls); owner.Started.TrySetResult(); await owner.Release.Task.WaitAsync(cancellationToken); }
                else Interlocked.Increment(ref owner.FreeCalls);
                return "PRIVATE OUTPUT";
            }
        }
    }
}
