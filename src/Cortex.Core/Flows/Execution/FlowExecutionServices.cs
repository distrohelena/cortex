using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Outbound;

namespace Cortex.Core.Flows.Execution;

internal sealed record FlowExecutionServices(IFlowCompletion Completion)
{
    internal FlowDiagnosticOptions Diagnostics { get; init; } = new();
    internal TimeProvider Clock { get; init; } = TimeProvider.System;
    internal Action<TypedNodeExecutionTrace>? Observer { get; init; }
    internal Func<TypedNode, JsonElement?, string?, CancellationToken, Task<JsonElement>>? MemoryQuery { get; init; }
    internal InstructionContext? InstructionContext { get; init; }
    internal Func<string, IReadOnlyDictionary<string, TypedValue>, CancellationToken, Task>? InstructionOutput { get; init; }
    internal Func<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>>, CancellationToken, Task>? NodeStarting { get; init; }
    internal BoundaryExecutionRegistry? BoundaryRegistry { get; init; }
    internal OutboundExecution? Outbound { get; init; }
}
