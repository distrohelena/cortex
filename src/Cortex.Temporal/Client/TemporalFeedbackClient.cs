using Cortex.Core.Flows.Durable;
using Cortex.Temporal.Workflows;
using Temporalio.Client;

namespace Cortex.Temporal.Client;

public sealed class TemporalFeedbackClient(ITemporalClient client, Func<ArtifactScope, string> executionWorkflowId)
{
    public Task<FeedbackReceipt> SubmitAsync(FeedbackSubmission submission, CancellationToken token) =>
        client.GetWorkflowHandle<FlowExecutionWorkflowV1>(executionWorkflowId(submission.Scope))
            .ExecuteUpdateAsync(w => w.SubmitFeedbackAsync(submission), new(submission.OperationId) { Rpc = new() { CancellationToken = token } });
}
