using System.Text.Json;
using Cortex.Core.Boundaries;

namespace Cortex.Core.Flows;

/// <summary>Projects the shared typed graph resolver into metadata-only public descriptions.</summary>
public static class FlowNodeDescriptionCatalog
{
    public static IReadOnlyDictionary<string, FlowNodeDescription> Describe(JsonElement graph, IEnumerable<NodeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var available = definitions.ToDictionary(definition => definition.Kind, StringComparer.Ordinal);
        var nodes = graph.GetProperty("nodes").EnumerateArray()
            .Select(raw => new FlowNode(raw.GetProperty("id").GetString()!, raw.GetProperty("kind").GetString()!, 1, raw)).ToDictionary(node => node.Id, StringComparer.Ordinal);
        var edges = graph.GetProperty("edges").EnumerateArray().Select(edge => new DataEdge(Ref(edge, "source"), [], Ref(edge, "target"))).ToArray();
        var controlEdges = graph.TryGetProperty("controlEdges", out var controls)
            ? controls.EnumerateArray().Select(edge => new ControlEdge(Ref(edge, "source"), Ref(edge, "target"))).ToArray() : [];
        var resolved = new Dictionary<string, ResolvedFlowNode>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);
        ResolvedFlowNode Resolve(string id)
        {
            if (resolved.TryGetValue(id, out var known)) return known;
            if (!nodes.TryGetValue(id, out var node)) throw new InvalidDataException("Node inexistente: " + id);
            if (!resolving.Add(id)) throw new InvalidDataException("Ciclo de dados.");
            ResolvedFlowNode contract;
            try
            {
                if (node.Kind == "note")
                    contract = new(node, "Nota", FlowNodeExecutionMode.Pure, FlowEffect.Pure, [], [], [], [], [], []);
                else
                    contract = TypedFlowNodeResolver.Resolve(node, available, portName =>
                    {
                        var links = edges.Where(edge => edge.Target == new PortRef(id, portName)).ToArray();
                        if (links.Length != 1) return null;
                        var upstream = Resolve(links[0].Source.NodeId);
                        return upstream.Outputs.FirstOrDefault(port => port.Id == links[0].Source.Port);
                    }, allowUnresolved: true, edges, controlEdges);
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or FlowSchemaException or BoundaryCompilationException or TypedFlowException)
            {
                var definition = available.GetValueOrDefault(node.Kind);
                contract = new(node, definition?.DisplayName ?? node.Kind, definition?.ExecutionMode ?? FlowNodeExecutionMode.Action,
                    definition?.Effect ?? FlowEffect.Pure,
                    definition?.DataInputs.Select(port => new ResolvedFlowPort(port.Name, port.Name, port.Type, port.Required)).ToArray() ?? [],
                    definition?.DataOutputs.Select(port => new ResolvedFlowPort(port.Name, port.Name, port.Type, port.Required)).ToArray() ?? [],
                    definition?.ControlInputs ?? [], definition?.ControlOutputs ?? [], definition?.ConfigFields ?? [],
                    error is TypedFlowException typed ? typed.Issues : [new(node.Id, "config", error.Message)], definition?.NavigableGraphReference);
            }
            resolving.Remove(id);
            resolved[id] = contract;
            return contract;
        }

        var descriptions = new Dictionary<string, FlowNodeDescription>(StringComparer.Ordinal);
        foreach (var id in nodes.Keys)
        {
            var contract = Resolve(id);
            descriptions.Add(id, new(contract.Source.Id, contract.Source.Kind, contract.DisplayName, contract.ExecutionMode, contract.Effect,
                contract.Inputs.Select(port => new FlowNodePortDescription(port.Id, port.Name, port.Type, port.Required, port.ConstantValue)).ToArray(),
                contract.Outputs.Select(port => new FlowNodePortDescription(port.Id, port.Name, port.Type, port.Required, port.ConstantValue)).ToArray(),
                contract.ControlInputs, contract.ControlOutputs, contract.Configuration, contract.Diagnostics, contract.NavigableGraphReference));
        }
        return descriptions;
    }

    public static IReadOnlyDictionary<string, FlowNodeDescription> Describe(JsonElement graph, IEnumerable<FlowNodeHandler> handlers) =>
        Describe(graph, handlers.Select(handler => handler.Definition));

    private static PortRef Ref(JsonElement edge, string end) => new(edge.GetProperty(end).GetString()!, edge.GetProperty(end + "Port").GetString()!);
}
