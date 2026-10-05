using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Client;
using Cortex.Temporal.Hosting;
using Cortex.Temporal.Workflows;
using Temporalio.Client;
using Temporalio.Worker;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class TemporalAcceptanceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static SessionFlowProject Project() => new("acceptance", 1, "acceptance-v1", JsonSerializer.Deserialize<JsonElement>("""
        {"version":1,"stateScope":"session","initialStateId":"initial","states":[{"id":"initial","graph":{
        "version":3,"nodes":[{"id":"in","kind":"dm"},
        {"id":"upstream","kind":"prompt","prompt":"SECRET-UPSTREAM-PROMPT","inputs":[],"outputMode":"text","responseFields":[{"id":"text","name":"text","type":"text"}]},
        {"id":"human","kind":"humanFeedback","settings":{"question":"SECRET-HUMAN-QUESTION"}},
        {"id":"downstream","kind":"prompt","prompt":"SECRET-DOWNSTREAM-PROMPT","inputs":[{"id":"feedback","name":"feedback","type":"text","required":true}],"outputMode":"text","responseFields":[{"id":"text","name":"text","type":"text"}]},
        {"id":"exit","kind":"exit","settings":{"mode":"switch","targetStateId":"done"}}],
        "edges":[{"source":"upstream","sourcePort":"text","target":"human","targetPort":"context"},{"source":"human","sourcePort":"feedback","target":"downstream","targetPort":"feedback"}],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"upstream","targetPort":"exec"},{"source":"upstream","sourcePort":"then","target":"human","targetPort":"exec"},{"source":"human","sourcePort":"then","target":"downstream","targetPort":"exec"},{"source":"downstream","sourcePort":"then","target":"exit","targetPort":"exec"}]}},
        {"id":"done","graph":{"version":3,"nodes":[{"id":"in","kind":"dm"}],"edges":[],"controlEdges":[]}}]}
        """));

    [Fact]
    public async Task PauseStopWorkerReopenStoreAnswerCompleteAndReplayWithoutPrivatePayload()
    {
        await using var environment = await TemporalTestEnvironment.StartAsync();
        var directory = Path.Combine(Path.GetTempPath(), "cortex-acceptance-" + Guid.NewGuid().ToString("N"));
        var project = Project(); var session = SessionWorkflowTests.Session("acceptance"); var model = new Model();
        var queue = "acceptance-" + Guid.NewGuid().ToString("N");
        var options = new TemporalRuntimeOptions("installation", "unused:7233", "default", queue, queue + "-effects", directory);
        var policy = ActivityPolicyTests.Policy() with { EffectTaskQueue = queue };
        var registry = new HostBoundExecutionBindings([new("installation", session, project, model, new UnavailableMemoryReader(), null, TimeProvider.System)
            { AuthorizeFeedbackAsync = (answer, _) => answer.AuthenticatedActorRef == "trusted-operator" ? Task.CompletedTask : throw new UnauthorizedAccessException() }], allowHarnessCompletions: true);
        TemporalWorkerOptions Worker(FileExecutionArtifactStore store, DurableSessionArtifacts artifacts) => new TemporalWorkerOptions(queue)
            .AddWorkflow<SessionWorkflowV1>().AddWorkflow<FlowExecutionWorkflowV1>()
            .AddAllActivities(new ExecutionActivities(new FlowExecutionDriver(store, registry))).AddAllActivities(new SessionActivities(artifacts));
        FeedbackDescriptor pending; ArtifactScope scope; string childId;
        try
        {
            await using (var store = new FileExecutionArtifactStore(directory, "installation"))
            {
                var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), registry);
                await using var backend = new TemporalSessionExecutionBackend(environment.Client, options, store, artifacts, policy);
                using var worker = new TemporalWorker(environment.Client, Worker(store, artifacts));
                (pending, scope, childId) = await worker.ExecuteAsync(async () =>
                {
                    await backend.PublishAsync(project, Token);
                    var message = SessionWorkflowTests.Message(session, "original") with { Message = JsonSerializer.Deserialize<JsonElement>("""{"texto":"SECRET-CONVERSATION","remetente":null,"canal":{"nome":"DM","nativeId":"acceptance"},"reply":null}""") };
                    await backend.ReceiveAsync(session, message, Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    ArtifactPage<FeedbackDescriptor> requests;
                    do { requests = await backend.ReadFeedbackAsync(session, new(), timeout.Token); if (requests.Items.Count == 0) await Task.Delay(30, timeout.Token); } while (requests.Items.Count == 0);
                    var request = Assert.Single(requests.Items); var active = (await backend.ReadActiveExecutionAsync(session, Token))!;
                    Assert.Equal(1, model.Upstream); Assert.Equal(0, model.Downstream);
                    return (request, active.Scope, active.WorkflowId);
                }, Token);
            }
            await using (var store = new FileExecutionArtifactStore(directory, "installation"))
            {
                var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), registry);
                await using var backend = new TemporalSessionExecutionBackend(environment.Client, options, store, artifacts, policy);
                using var worker = new TemporalWorker(environment.Client, Worker(store, artifacts));
                await worker.ExecuteAsync(async () =>
                {
                    Assert.Equal(pending, Assert.Single((await backend.ReadFeedbackAsync(session, new(), Token)).Items));
                    var response = new FeedbackResponseV1("answer", pending.RequestId, pending.RequestDigest, "SECRET-HUMAN-ANSWER", "trusted-operator");
                    var reference = await store.PutOnceAsync(scope, "answer", JsonSerializer.SerializeToElement(response), Token);
                    var submission = new FeedbackSubmission("answer", scope, pending.RequestId, pending.RequestDigest, reference, "trusted-operator");
                    Assert.Equal("answered", (await backend.SubmitFeedbackAsync(submission, Token)).Status);
                    await backend.WaitForIdleAsync(session, Token);
                    var result = Assert.Single((await backend.ReadOutcomesAsync(session, new(), Token)).Items);
                    Assert.True(result.Success, result.Error); Assert.True(result.TransitionApplied); Assert.Equal(scope.ExecutionId, result.ExecutionId);
                    var snapshot = (await backend.ReadSessionAsync(session, Token))!;
                    Assert.Equal("done", snapshot.CurrentStateId); Assert.Equal(1, snapshot.StateVersion);
                    Assert.Equal(1, model.Upstream); Assert.Equal(1, model.Downstream);
                    var child = environment.Client.GetWorkflowHandle<FlowExecutionWorkflowV1>(childId);
                    var history = await child.FetchHistoryAsync();
                    var parentHistory = await environment.Client.GetWorkflowHandle<SessionWorkflowV1>(DurableIdentity.SessionWorkflowId("installation", session)).FetchHistoryAsync();
                    PayloadPrivacyTests.AssertPrivateMarkersAbsent(history.ToJson()); PayloadPrivacyTests.AssertPrivateMarkersAbsent(parentHistory.ToJson());
                    var replayer = new WorkflowReplayer(new WorkflowReplayerOptions().AddWorkflow<FlowExecutionWorkflowV1>().AddWorkflow<SessionWorkflowV1>());
                    await replayer.ReplayWorkflowAsync(history, cancellationToken: Token);
                    await replayer.ReplayWorkflowAsync(parentHistory, cancellationToken: Token);
                    if (Environment.GetEnvironmentVariable("CORTEX_REPLAY_EXPORT") is { } export)
                        await File.WriteAllTextAsync(export, history.ToJson(), Token);
                }, Token);
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Model : ISessionFlowCompletionFactory, IFlowCompletion
    {
        public int Upstream; public int Downstream;
        public IFlowCompletion Create(SessionCompletionContext context) => this;
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (request.NodeId == "upstream") { Interlocked.Increment(ref Upstream); return Task.FromResult("SECRET-UPSTREAM-CONTEXT"); }
            Assert.Equal("SECRET-HUMAN-ANSWER", request.Inputs.GetProperty("feedback").GetString());
            Interlocked.Increment(ref Downstream); return Task.FromResult("SECRET-DOWNSTREAM-OUTPUT");
        }
    }
}
