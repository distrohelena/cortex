using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Temporal.Hosting;

namespace Cortex.Temporal.Workflows;

public sealed record SessionWorkflowInput(string InstallationId, SessionKey Session, ArtifactRef ProjectRef, string WorkflowTaskQueue,
    WorkflowActivityPolicy ActivityPolicy, int MaxPendingBatches = 256, int RotationEventLimit = 10000, DurableSessionState? CarriedState = null);
public sealed record ActiveExecutionDescriptor(ArtifactScope Scope, string BatchId, string WorkflowId);
