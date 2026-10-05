using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Workflows;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Worker;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class FeedbackWorkflowTests
{
    [Fact]
    public async Task WaitingSurvivesWorkerRestartThenAuthenticatedFeedbackResumesOnce()
    {
        await using var environment = await TemporalTestEnvironment.StartAsync();
        var driver = new FeedbackDriver();
        var queue = "feedback-" + Guid.NewGuid().ToString("N");
        var input = driver.Input(queue);
        var id = DurableIdentity.ExecutionWorkflowId(input.Start.Scope, input.Start.BatchId);
        var handle = await environment.Client.StartWorkflowAsync((FlowExecutionWorkflowV1 w) => w.RunAsync(input), new(id, queue));
        var options = new TemporalWorkerOptions(queue).AddWorkflow<FlowExecutionWorkflowV1>().AddAllActivities(new ExecutionActivities(driver));
        using (var worker = new TemporalWorker(environment.Client, options))
            await worker.ExecuteAsync(async () =>
            {
                await WaitPendingAsync(handle);
                Assert.Equal(0, driver.DownstreamCalls);
                var history = await handle.FetchHistoryAsync();
                Assert.DoesNotContain(history.Events, e => e.ActivityTaskScheduledEventAttributes?.ActivityType.Name == "ResolveFeedback");
            }, TestContext.Current.CancellationToken);
        using (var worker = new TemporalWorker(environment.Client, options))
            await worker.ExecuteAsync(async () =>
            {
                var before = await handle.QueryAsync(w => w.Status());
                Assert.Equal(input.Start.Scope.ExecutionId, before!.Scope.ExecutionId);
                var submission = driver.Submission();
                driver.Authorized = false;
                await Assert.ThrowsAsync<WorkflowUpdateFailedException>(() => handle.ExecuteUpdateAsync(w => w.SubmitFeedbackAsync(submission), new("unauthorized")));
                Assert.Equal(ExecutionStatus.WaitingForHuman, (await handle.QueryAsync(w => w.Status()))!.Status);
                driver.Authorized = true;
                var receipt = await handle.ExecuteUpdateAsync(w => w.SubmitFeedbackAsync(submission), new(submission.OperationId));
                Assert.Equal("answered", receipt.Status);
                var repeat = await handle.ExecuteUpdateAsync(w => w.SubmitFeedbackAsync(submission), new(submission.OperationId));
                Assert.Equal(receipt, repeat);
                Assert.Equal(ExecutionStatus.Completed, (await handle.GetResultAsync()).Status);
                Assert.Equal(1, driver.UpstreamCalls); Assert.Equal(1, driver.DownstreamCalls); Assert.Equal(1, driver.Resolutions);
            }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("scope")] [InlineData("digest")] [InlineData("late")]
    public async Task InvalidFeedbackDoesNotResolve(string invalid)
    {
        await using var environment = await TemporalTestEnvironment.StartAsync();
        var driver = new FeedbackDriver(); var queue = "invalid-" + Guid.NewGuid().ToString("N");
        using var worker = new TemporalWorker(environment.Client, new TemporalWorkerOptions(queue).AddWorkflow<FlowExecutionWorkflowV1>().AddAllActivities(new ExecutionActivities(driver)));
        await worker.ExecuteAsync(async () =>
        {
            var input = driver.Input(queue);
            var handle = await environment.Client.StartWorkflowAsync((FlowExecutionWorkflowV1 w) => w.RunAsync(input), new(Guid.NewGuid().ToString(), queue));
            await WaitPendingAsync(handle);
            var submission = driver.Submission();
            submission = invalid switch { "scope" => submission with { Scope = submission.Scope with { ExecutionId = "foreign" } }, "digest" => submission with { RequestDigest = new string('1', 64) }, _ => submission with { RequestId = "old-request" } };
            await Assert.ThrowsAsync<WorkflowUpdateFailedException>(() => handle.ExecuteUpdateAsync(w => w.SubmitFeedbackAsync(submission)));
            Assert.Equal(0, driver.Resolutions);
            await handle.ExecuteUpdateAsync(w => w.CancelAsync("cancel"));
            Assert.Equal(ExecutionStatus.Cancelled, (await handle.GetResultAsync()).Status);
            Assert.Equal(0, driver.DownstreamCalls);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TimeoutResolvesOnceWithoutThen()
    {
        await using var environment = await TemporalTestEnvironment.StartAsync(timeSkipping: true);
        var driver = new FeedbackDriver { TimeoutSeconds = 3600 }; var queue = "timeout-" + Guid.NewGuid().ToString("N");
        using var worker = new TemporalWorker(environment.Client, new TemporalWorkerOptions(queue).AddWorkflow<FlowExecutionWorkflowV1>().AddAllActivities(new ExecutionActivities(driver)));
        await worker.ExecuteAsync(async () =>
        {
            var input = driver.Input(queue);
            var result = await environment.Client.ExecuteWorkflowAsync((FlowExecutionWorkflowV1 w) => w.RunAsync(input), new(Guid.NewGuid().ToString(), queue));
            Assert.Equal(ExecutionStatus.Completed, result.Status); Assert.Equal("timeout", driver.ResolutionStatus);
            Assert.Equal(1, driver.Resolutions); Assert.Equal(0, driver.DownstreamCalls);
        }, TestContext.Current.CancellationToken);
    }

    private static async Task WaitPendingAsync(WorkflowHandle<FlowExecutionWorkflowV1, ExecutionDescriptor> handle)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while ((await handle.QueryAsync(w => w.Status()))?.Status != ExecutionStatus.WaitingForHuman)
            await Task.Delay(30, timeout.Token);
    }

    internal sealed class FeedbackDriver : IFlowExecutionDriver
    {
        private readonly ArtifactScope scope = new("installation", new("runtime", "character", "channel", "account", "conversation"), "execution", "revision");
        public bool Authorized { get; set; } = true;
        public int? TimeoutSeconds { get; init; }
        public int UpstreamCalls { get; private set; } public int DownstreamCalls { get; private set; } public int Resolutions { get; private set; }
        public string? ResolutionStatus { get; private set; }
        private ArtifactRef Ref(string key) => new(1, scope, key, new string('0', 64));
        private FeedbackDescriptor Feedback => new("request", Ref("request"), new string('0', 64), "feedback", 1, TimeoutSeconds);
        public ExecutionWorkflowInput Input(string queue) => new(new(scope, "batch", Ref("project"), Ref("batch"), "state", 1, SessionEntryKind.Dm), ActivityPolicyTests.Policy() with { EffectTaskQueue = queue });
        public FeedbackSubmission Submission() => new("answer", scope, "request", new string('0', 64), Ref("response"), "operator");
        public Task<ExecutionDescriptor> InitializeAsync(ExecutionStart start, CancellationToken token) => Task.FromResult(new ExecutionDescriptor(scope, Ref("initial"), "upstream", 0, ExecutionStatus.Ready, StepEffect.Inference, null, null, null));
        public Task<ExecutionDescriptor> AdvanceAsync(StepCommand command, CancellationToken token)
        {
            if (command.Key.NodeId == "upstream") { UpstreamCalls++; return Task.FromResult(new ExecutionDescriptor(scope, Ref("waiting"), "feedback", 1, ExecutionStatus.WaitingForHuman, StepEffect.Feedback, Feedback, null, null)); }
            DownstreamCalls++; return Task.FromResult(new ExecutionDescriptor(scope, Ref("done"), null, 3, ExecutionStatus.Completed, StepEffect.Pure, null, Ref("done"), null));
        }
        public Task<ExecutionDescriptor?> RecoverAsync(StepKey key, CancellationToken token) => Task.FromResult<ExecutionDescriptor?>(null);
        public Task ValidateFeedbackAsync(FeedbackSubmission submission, CancellationToken token) => Authorized ? Task.CompletedTask : throw new ApplicationFailureException("feedback-unauthorized", nonRetryable: true);
        public Task<ExecutionDescriptor> ResolveFeedbackAsync(StepCommand command, FeedbackResolution resolution, CancellationToken token)
        {
            Resolutions++; ResolutionStatus = resolution.Status;
            return Task.FromResult(new ExecutionDescriptor(scope, Ref("resolved"), resolution.Status == "answered" ? "downstream" : null, 2,
                resolution.Status == "answered" ? ExecutionStatus.Ready : resolution.Status == "cancelled" ? ExecutionStatus.Cancelled : ExecutionStatus.Completed,
                StepEffect.Inference, null, null, null));
        }
    }
}
