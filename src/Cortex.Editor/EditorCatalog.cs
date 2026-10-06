using System.Text.Json;
using System.Text.RegularExpressions;
using Cortex.Core.Flows;

namespace Cortex.Editor;

public static class EditorCatalog
{
    public static string Title(string kind)
    {
        var words = Regex.Replace(kind, "([a-z0-9])([A-Z])", "$1 $2");
        return words.Length == 0 ? "Node" : char.ToUpperInvariant(words[0]) + words[1..];
    }
    public static object[] Available(IReadOnlyList<FlowNodeHandler> handlers) => handlers.Select(handler => (object)new
    {
        kind = handler.Definition.Kind, title = handler.Definition.DisplayName ?? Title(handler.Definition.Kind), isEntry = handler.IsEntry,
        fields = handler.Definition.ConfigFields.Select(field => new { name = field.Name, label = field.Label ?? Title(field.Name), required = field.Required, schema = field.Type.ToJsonSchema() }),
        ports = handler.Definition.DataInputs.Select(port => new { id = port.Name, label = port.Name, type = port.Type.Kind, direction = "in" })
            .Concat(handler.Definition.DataOutputs.Select(port => new { id = port.Name, label = port.Name, type = port.Type.Kind, direction = "out" })),
        executionPorts = handler.Definition.ControlInputs.Select(port => new { id = port.Name, label = "Executar", direction = "in" })
            .Concat(handler.Definition.ControlOutputs.Select(port => new { id = port.Name, label = "Próximo", direction = "out" }))
    }).ToArray();

    public static Dictionary<string, object> Descriptions(JsonElement graph, IReadOnlyList<FlowNodeHandler> handlers)
    {
        var available = handlers.Select(handler => handler.Definition.Kind).ToHashSet(StringComparer.Ordinal);
        return graph.GetProperty("nodes").EnumerateArray().ToDictionary(node => node.GetProperty("id").GetString()!, node =>
        {
            var id = node.GetProperty("id").GetString()!;
            var kind = node.GetProperty("kind").GetString()!;
            var known = available.Contains(kind);
            var palette = Available(handlers);
            if (known) return palette[handlers.Select(handler => handler.Definition.Kind).ToList().IndexOf(kind)];
            var edges = graph.GetProperty("edges").EnumerateArray().ToArray();
            var controls = graph.TryGetProperty("controlEdges", out var ce) ? ce.EnumerateArray().ToArray() : [];
            object[] Ports(JsonElement[] connections) => connections.SelectMany(edge => new[] { "source", "target" }
                .Where(end => edge.GetProperty(end).GetString() == id)
                .Select(end => (object)new { id = edge.GetProperty(end + "Port").GetString(), label = edge.GetProperty(end + "Port").GetString(), type = "indisponível", direction = end == "source" ? "out" : "in" }))
                .DistinctBy(port => JsonSerializer.Serialize(port)).ToArray();
            return (object)new { kind, title = Title(kind), className = "unavailable", preview = "Node indisponível · confira os plugins", fields = Array.Empty<object>(), ports = Ports(edges), executionPorts = Ports(controls) };
        });
    }
}
