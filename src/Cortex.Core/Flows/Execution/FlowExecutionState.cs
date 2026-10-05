using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Execution;

internal sealed class FlowExecutionState(ExecutionSnapshotV1 snapshot,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>>? runtimeOutputs)
{
    internal ExecutionSnapshotV1 Snapshot { get; } = snapshot;
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>>? RuntimeOutputs { get; } = runtimeOutputs;
}
internal sealed record KernelStepResult(TypedRunResult Result, ExecutionStatus Status, string? NextNodeId,
    StepEffect NextEffect, IReadOnlyList<TypedNodeExecutionTrace> Traces);
