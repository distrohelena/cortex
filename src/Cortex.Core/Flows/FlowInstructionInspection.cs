using System.Text.Json;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Flows;

public sealed record FlowInstructionSnapshot(string ProjectRevision, string StateId, string NodeId,
    string System, JsonElement? ResponseSchema, IReadOnlyList<string> InputNames, JsonElement Configuration);

public static class FlowInstructionInspection
{
    /// <summary>Compile and describe an instruction without admitting a message or constructing
    /// a runtime. Uses the same schema and context composition as actual session execution.</summary>
    public static FlowInstructionSnapshot Describe(SessionFlowProject project, string stateId,
        SessionEntryKind entryKind, string nodeId)
    {
        var compiled = SessionFlowProjectCompiler.Compile(project);
        if (!compiled.States.TryGetValue(stateId, out var state) ||
            !state.Entries.TryGetValue(entryKind, out var flow) ||
            !flow.Nodes.TryGetValue(nodeId, out var node) || node.Source.Kind != "prompt")
            throw new InvalidDataException("Requested instruction is not available in this project state/entry.");
        return new(compiled.ProjectRevision, stateId, nodeId,
            InstructionContext.Compose(node.Source.Config, state.InstructionContext),
            TypedFlowRunner.ResponseSchema(node)?.Clone(),
            Array.AsReadOnly(node.Inputs.Select(port => port.Name).ToArray()), node.Source.Config.Clone());
    }
}
