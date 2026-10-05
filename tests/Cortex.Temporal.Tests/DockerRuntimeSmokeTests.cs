using Cortex.Core.Flows.Durable;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Hosting;
using Cortex.Temporal.Workflows;
using Temporalio.Client;
using Xunit;

namespace Cortex.Temporal.Tests;

[Trait("Category", "LocalSocket")]
public sealed class DockerRuntimeSmokeTests
{
    private static string ReceiptFile => Environment.GetEnvironmentVariable("CORTEX_TEMPORAL_DOCKER_RECEIPT_FILE")
        ?? throw new InvalidOperationException("Set an explicit synthetic smoke receipt file.");
    private static Task<TemporalClient> Connect() => TemporalClient.ConnectAsync(new("127.0.0.1:7233") { Namespace = "cortex-local" });

    [Fact]
    public async Task ServerRunsDurableFeedbackThroughConfiguredQueues()
    {
        var token = TestContext.Current.CancellationToken; var client = await Connect();
        var options = new TemporalRuntimeOptions("cortex-local", "127.0.0.1:7233", "cortex-local", "cortex-local-workflows", "cortex-local-effects", "unused-smoke-store");
        var driver = new FeedbackWorkflowTests.FeedbackDriver();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var running = new TemporalRuntimeWorker(client, options, [typeof(FlowExecutionWorkflowV1)], [new ExecutionActivities(driver)]).RunAsync(stop.Token);
        try
        {
            var id = "cortex-docker-feedback-smoke-" + Guid.NewGuid().ToString("N");
            var input = driver.Input(options.EffectTaskQueue);
            var handle = await client.StartWorkflowAsync((FlowExecutionWorkflowV1 w) => w.RunAsync(input), new(id, options.WorkflowTaskQueue));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while ((await handle.QueryAsync(w => w.Status()))?.Status != ExecutionStatus.WaitingForHuman) await Task.Delay(30, timeout.Token);
            Assert.Equal("answered", (await handle.ExecuteUpdateAsync(w => w.SubmitFeedbackAsync(driver.Submission()))).Status);
            Assert.Equal(ExecutionStatus.Completed, (await handle.GetResultAsync()).Status);
            Assert.Equal(1, driver.UpstreamCalls); Assert.Equal(1, driver.DownstreamCalls);
            PayloadPrivacyTests.AssertPrivateMarkersAbsent((await handle.FetchHistoryAsync()).ToJson());
            await File.WriteAllTextAsync(ReceiptFile, id, token);
        }
        finally { stop.Cancel(); try { await running; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } }
    }

    [Fact]
    public async Task CompletedFeedbackIsStillReadableAfterContainerRestartWithoutWorkers()
    {
        var id = await File.ReadAllTextAsync(ReceiptFile, TestContext.Current.CancellationToken);
        Assert.StartsWith("cortex-docker-feedback-smoke-", id);
        var client = await Connect();
        var result = await client.GetWorkflowHandle<FlowExecutionWorkflowV1>(id).GetResultAsync<ExecutionDescriptor>();
        Assert.Equal(ExecutionStatus.Completed, result.Status);
    }
}
