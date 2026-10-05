using Cortex.Core.Flows.Durable;
using Temporalio.Activities;

namespace Cortex.Temporal.Activities;

public sealed class ExecutionActivities(IFlowExecutionDriver driver)
{
    [Activity]
    public Task<ExecutionDescriptor> InitializeAsync(ExecutionStart start) => ActivityFailureBoundary.RunAsync(() => driver.InitializeAsync(start, ActivityExecutionContext.Current.CancellationToken), "execution-activity-failed");
    [Activity]
    public Task<ExecutionDescriptor> AdvanceAsync(StepCommand command) => ActivityFailureBoundary.RunAsync(() => driver.AdvanceAsync(command, ActivityExecutionContext.Current.CancellationToken), "execution-activity-failed");
    [Activity]
    public Task<ExecutionDescriptor?> RecoverAsync(StepKey key) => ActivityFailureBoundary.RunAsync(() => driver.RecoverAsync(key, ActivityExecutionContext.Current.CancellationToken), "execution-activity-failed");
    [Activity]
    public Task ValidateFeedbackAsync(FeedbackSubmission submission) => ActivityFailureBoundary.RunAsync(() => driver.ValidateFeedbackAsync(submission, ActivityExecutionContext.Current.CancellationToken), "feedback-authorization-failed");
    [Activity]
    public Task<ExecutionDescriptor> ResolveFeedbackAsync(StepCommand command, FeedbackResolution resolution) =>
        ActivityFailureBoundary.RunAsync(() => driver.ResolveFeedbackAsync(command, resolution, ActivityExecutionContext.Current.CancellationToken), "feedback-resolution-failed");
}
