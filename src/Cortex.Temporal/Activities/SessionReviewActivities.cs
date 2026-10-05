using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Temporal.Workflows;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Cortex.Temporal.Activities;

public sealed class SessionReviewActivities(ISessionReviewArtifactServices artifacts, ITemporalClient client)
{
    [Activity]
    public Task<PreparedSessionReview> PrepareReviewAsync(ReviewPreparationCommand command) => ActivityFailureBoundary.RunAsync(() => artifacts.PrepareReviewAsync(command, ActivityExecutionContext.Current.CancellationToken), "review-preparation-failed");
    [Activity]
    public Task<CommittedSessionReview> CommitReviewAsync(ReviewCommitCommand command) => ActivityFailureBoundary.RunAsync(() => artifacts.CommitReviewAsync(command, ActivityExecutionContext.Current.CancellationToken), "review-commit-failed");
    [Activity]
    public Task<ExecutionDescriptor?> ReadCompletedReviewAsync(string workflowId) => ActivityFailureBoundary.RunAsync(async () =>
    {
        var handle = client.GetWorkflowHandle<SessionReviewWorkflowV1, ExecutionDescriptor>(workflowId);
        var rpc = new RpcOptions { CancellationToken = ActivityExecutionContext.Current.CancellationToken };
        try
        {
            var description = await handle.DescribeAsync(new() { Rpc = rpc }).ConfigureAwait(false);
            return description.Status == Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Completed ? await handle.GetResultAsync(rpcOptions: rpc).ConfigureAwait(false) : null;
        }
        catch (RpcException error) when (error.Code == RpcException.StatusCode.NotFound) { return null; }
    }, "review-recovery-failed");
}
