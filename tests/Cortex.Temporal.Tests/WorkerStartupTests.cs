using Cortex.Core.Flows.Durable;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Hosting;
using Cortex.Temporal.Workflows;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class WorkerStartupTests
{
    [Fact]
    public async Task WorkflowCanReplayAndAnswerQueriesWhileEffectQueueWaitsForRestoreValidation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var environment = await TemporalTestEnvironment.StartAsync();
        var queue = "startup-" + Guid.NewGuid().ToString("N");
        var options = new TemporalRuntimeOptions("installation", "unused:7233", "default", queue, queue + "-effects", Path.Combine(Path.GetTempPath(), "unused"));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var driver = new FeedbackWorkflowTests.FeedbackDriver();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var running = new TemporalRuntimeWorker(environment.Client, options, [typeof(FlowExecutionWorkflowV1)], [new ExecutionActivities(driver)]).RunAsync(stop.Token, ready.Task);
        try
        {
            var input = driver.Input(options.EffectTaskQueue);
            var handle = await environment.Client.StartWorkflowAsync((FlowExecutionWorkflowV1 w) => w.RunAsync(input), new(Guid.NewGuid().ToString("N"), queue));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (!(await handle.FetchHistoryAsync()).Events.Any(e => e.ActivityTaskScheduledEventAttributes is not null)) await Task.Delay(30, timeout.Token);
            Assert.Null(await handle.QueryAsync(w => w.Status()));
            Assert.Equal(0, driver.UpstreamCalls);
            Assert.DoesNotContain((await handle.FetchHistoryAsync()).Events, e => e.ActivityTaskStartedEventAttributes is not null);
            ready.SetResult();
            while ((await handle.QueryAsync(w => w.Status()))?.Status != ExecutionStatus.WaitingForHuman) await Task.Delay(30, timeout.Token);
            Assert.Equal(1, driver.UpstreamCalls);
            await handle.ExecuteUpdateAsync(w => w.CancelAsync("cancel"));
            Assert.Equal(ExecutionStatus.Cancelled, (await handle.GetResultAsync()).Status); Assert.Equal(0, driver.DownstreamCalls);
        }
        finally
        {
            stop.Cancel();
            try { await running; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}
