using Cortex.Core.Flows.Durable;
using Cortex.Temporal.Hosting;

namespace Cortex.Temporal.Workflows;

public sealed record ExecutionWorkflowInput(ExecutionStart Start, WorkflowActivityPolicy ActivityPolicy);
