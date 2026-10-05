using Cortex.Core.Flows.Durable;
using Temporalio.Activities;

namespace Cortex.Temporal.Activities;

public sealed class ExecutionActivities(IFlowExecutionDriver driver)
{
    [Activity]
    public Task<ExecutionDescriptor> InitializeAsync(ExecutionStart start) => driver.InitializeAsync(start, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task<ExecutionDescriptor> AdvanceAsync(StepCommand command) => driver.AdvanceAsync(command, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task<ExecutionDescriptor?> RecoverAsync(StepKey key) => driver.RecoverAsync(key, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task ValidateFeedbackAsync(FeedbackSubmission submission) => driver.ValidateFeedbackAsync(submission, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task<ExecutionDescriptor> ResolveFeedbackAsync(StepCommand command, FeedbackResolution resolution) =>
        driver.ResolveFeedbackAsync(command, resolution, ActivityExecutionContext.Current.CancellationToken);
}
