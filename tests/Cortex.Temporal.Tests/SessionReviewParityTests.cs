using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Client;
using Cortex.Temporal.Hosting;
using Cortex.Temporal.Workflows;
using Temporalio.Worker;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class SessionReviewParityTests
{
    internal static SessionFlowProject Project() => new("review-project", 1, "review-revision", JsonSerializer.Deserialize<JsonElement>("""
        {"version":1,"stateScope":"session","initialStateId":"initial","states":[{"id":"initial","graph":
        {"version":3,"nodes":[{"id":"in","kind":"dm"},
        {"id":"decision","kind":"prompt","prompt":"PRIVATE DECISION PROMPT","inputs":[],"outputMode":"structured","responseFields":[{"id":"respond","name":"respond","type":"boolean"}]},
        {"id":"branch","kind":"branch","settings":{"operator":"eq","b":true}},
        {"id":"generate","kind":"prompt","prompt":"PRIVATE GENERATION PROMPT","inputs":[],"outputMode":"text","responseFields":[{"id":"text","name":"text","type":"text"}]}],
        "edges":[{"source":"decision","sourcePort":"respond","target":"branch","targetPort":"a"}],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"decision","targetPort":"exec"},{"source":"decision","sourcePort":"then","target":"branch","targetPort":"exec"},
        {"source":"branch","sourcePort":"true","target":"generate","targetPort":"exec"}]}}]}
        """));

    [Fact]
    public async Task ReviewThenRestartThenPreviewReusesOnlyPrivateValidatedDecision()
    {
        await using var environment = await TemporalTestEnvironment.StartAsync();
        var directory = Path.Combine(Path.GetTempPath(), "cortex-review-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            await using var store = new FileExecutionArtifactStore(directory, "installation");
            var project = Project(); var session = SessionWorkflowTests.Session("review"); var model = new Model(); var assessmentCalls = 0;
            using var host = new ShadowOutboundHost(Path.Combine(directory, "outbound.jsonl"), project.Document, new([], [], new Dictionary<string, string>(), [], null), [session], TransportSafetyCapabilities.Unproven);
            host.Rearm(OutboundMode.Shadow);
            var queue = "review-" + Guid.NewGuid().ToString("N");
            TemporalSessionExecutionBackend? backend = null;
            var registry = new HostBoundExecutionBindings([new("installation", session, project, model, new UnavailableMemoryReader(), host, TimeProvider.System)
            { ShadowOnly = true, AnalyzeReassessmentAsync = (context, _) =>
                { assessmentCalls++; Assert.Contains(context.History, h => h.ProviderMessageId == "later"); return Task.FromResult(new ParticipationReassessment(false, ReassessmentOpportunity.Uncertain, ["original"])); },
                ValidateReviewContextAsync = async (expected, token) =>
                {
                    var current = await backend!.ReadSessionAsync(session, token);
                    if (current is null || !DurableSessionArtifacts.ReviewContextMatches(expected, current)) throw new InvalidOperationException("review-stale");
                } }], allowHarnessCompletions: true);
            var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), registry);
            backend = new(environment.Client, new("installation", "unused:7233", "default", queue, queue + "-effects", directory), store, artifacts, ActivityPolicyTests.Policy() with { EffectTaskQueue = queue });
            await using var backendLifetime = backend;
            var reviews = new TemporalSessionReviews(backend, store, artifacts, environment.Client, "installation");
            var workerOptions = new TemporalWorkerOptions(queue).AddWorkflow<SessionWorkflowV1>().AddWorkflow<FlowExecutionWorkflowV1>().AddWorkflow<SessionReviewWorkflowV1>()
                .AddAllActivities(new ExecutionActivities(new FlowExecutionDriver(store, registry))).AddAllActivities(new SessionActivities(artifacts)).AddAllActivities(new SessionReviewActivities(artifacts, environment.Client));
            ArtifactRef reviewRef;
            using (var worker = new TemporalWorker(environment.Client, workerOptions))
                reviewRef = await worker.ExecuteAsync(async () =>
                {
                    await backend.PublishAsync(project, TestContext.Current.CancellationToken);
                    var message = SessionWorkflowTests.Message(session, "original"); var now = DateTimeOffset.UtcNow;
                    var snapshot = new SessionSnapshot(session, project.ProjectId, project.ProjectVersion, project.ProjectRevision, "initial", 0, now, 1, 1, 0, 0);
                    var history = new SessionHistoryEntry(1, message.ProviderMessageId, message.Timestamp, message.Direction, message.EntryKind, message.ChannelId, message.AccountId, message.NativeConversationId, message.Message);
                    await backend.ImportIdleAsync(new("import", "installation", snapshot, [history], ["original"], "epoch", 1, new string('0', 64)), TestContext.Current.CancellationToken);
                    var definition = new ParticipationReviewDefinition(project.ProjectRevision, ["decision"], [new("branch", "true", ParticipationReviewDecision.Respond), new("branch", "false", ParticipationReviewDecision.Silence)]);
                    var result = await reviews.ReviewAsync(session, "original", definition, TestContext.Current.CancellationToken);
                    Assert.Equal(ParticipationReviewDecision.Respond, result.Decision); Assert.Equal(1, model.DecisionCalls); Assert.Equal(0, model.GenerationCalls);
                    return result.ReviewRef!;
                }, TestContext.Current.CancellationToken);
            using (var worker = new TemporalWorker(environment.Client, workerOptions))
                await worker.ExecuteAsync(async () =>
                {
                    var reconnected = new TemporalSessionReviews(backend, store, artifacts, environment.Client, "installation");
                    var preview = await reconnected.PreviewAsync(session, reviewRef, TestContext.Current.CancellationToken);
                    Assert.Equal(ParticipationReviewStatus.Completed, preview.Status); Assert.Equal("PRIVATE RESPONSE", preview.Outputs["generate"]["text"].GetString());
                    Assert.Equal(1, model.DecisionCalls); Assert.Equal(1, model.GenerationCalls);
                    Assert.Empty((await backend.ReadOutcomesAsync(session, new(), TestContext.Current.CancellationToken)).Items);
                    var live = await reconnected.ContinueAsync(session, reviewRef, TestContext.Current.CancellationToken);
                    Assert.Equal(ParticipationReviewStatus.Rejected, live.Status);
                    Assert.Equal(1, model.GenerationCalls);
                    await backend.ReceiveAsync(session, SessionWorkflowTests.Message(session, "later"), TestContext.Current.CancellationToken);
                    await backend.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
                    var stale = await reconnected.PreviewAsync(session, reviewRef, TestContext.Current.CancellationToken);
                    Assert.Equal(ParticipationReviewStatus.Rejected, stale.Status); Assert.Equal("preview-context-stale", stale.Diagnostic);
                    var snapshot = (await backend.ReadSessionAsync(session, TestContext.Current.CancellationToken))!;
                    var definition = new ParticipationReviewDefinition(project.ProjectRevision, ["decision"], [new("branch", "true", ParticipationReviewDecision.Respond), new("branch", "false", ParticipationReviewDecision.Silence)]);
                    var decisionsBefore = model.DecisionCalls;
                    var reassessed = await reconnected.ReassessAsync(session, "original", definition, snapshot, TestContext.Current.CancellationToken);
                    Assert.Equal(ParticipationReviewStatus.Completed, reassessed.Status); Assert.Null(reassessed.Decision); Assert.False(reassessed.Assessment!.Confident);
                    Assert.Equal(decisionsBefore, model.DecisionCalls); Assert.Equal(1, assessmentCalls);
                    await reconnected.ReassessAsync(session, "original", definition, snapshot, TestContext.Current.CancellationToken); Assert.Equal(1, assessmentCalls);
                }, TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class Model : ISessionFlowCompletionFactory, IFlowCompletion
    {
        public int DecisionCalls; public int GenerationCalls;
        public IFlowCompletion Create(SessionCompletionContext context) => this;
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (request.NodeId == "decision") { Interlocked.Increment(ref DecisionCalls); return Task.FromResult("{\"respond\":true}"); }
            Interlocked.Increment(ref GenerationCalls); return Task.FromResult("PRIVATE RESPONSE");
        }
    }
}
