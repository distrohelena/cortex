using Temporalio.Common;
using Temporalio.Worker;
using Cortex.Temporal.Workflows;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class ReplayCompatibilityTests
{
    [Fact]
    public async Task ReplayGoldenV1FeedbackHistory()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flow-feedback-v1.json"), TestContext.Current.CancellationToken);
        PayloadPrivacyTests.AssertPrivateMarkersAbsent(json);
        await new WorkflowReplayer(new WorkflowReplayerOptions().AddWorkflow<FlowExecutionWorkflowV1>())
            .ReplayWorkflowAsync(WorkflowHistory.FromJson("synthetic-feedback-v1", json), cancellationToken: TestContext.Current.CancellationToken);
    }
}
