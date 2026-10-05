using Cortex.Core.Flows.Durable;
using Temporalio.Workflows;

namespace Cortex.Temporal.Workflows;

[Workflow]
public sealed class SessionReviewWorkflowV1
{
    [WorkflowRun]
    public Task<ExecutionDescriptor> RunAsync(ExecutionWorkflowInput input) => Workflow.ExecuteChildWorkflowAsync((FlowExecutionWorkflowV1 w) => w.RunAsync(input),
        new() { Id = DurableIdentity.ExecutionWorkflowId(input.Start.Scope, input.Start.BatchId) });
}
