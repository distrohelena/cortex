using System.Text.Json;
using System.Text.RegularExpressions;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public static class EditorCatalog
{
    private static readonly object Note = new
    {
        kind = "note", title = "Nota", isEntry = false, className = "graph-note",
        fields = new[] { new { name = "text", label = "Texto", required = false, multiline = true, schema = new { type = "string" } } },
        ports = Array.Empty<object>(), executionPorts = Array.Empty<object>()
    };
    public static string Title(string kind)
    {
        var words = Regex.Replace(kind, "([a-z0-9])([A-Z])", "$1 $2");
        return words.Length == 0 ? "Node" : char.ToUpperInvariant(words[0]) + words[1..];
    }
    public static object[] Available(IReadOnlyList<FlowNodeHandler> handlers, IReadOnlyDictionary<string, NodeLibrary>? libraries = null) => handlers.Select(handler => (object)new
    {
        kind = handler.Definition.Kind, title = handler.Definition.DisplayName ?? Title(handler.Definition.Kind), isEntry = handler.IsEntry,
        library = libraries?.GetValueOrDefault(handler.Definition.Kind),
        fields = handler.Definition.ConfigFields.Select(field => new { name = field.Name, label = field.Label ?? Title(field.Name), required = field.Required, schema = field.Type.ToJsonSchema() }),
        ports = handler.Definition.DataInputs.Select(port => new { id = port.Name, label = port.Name, type = port.Type.Kind, direction = "in" })
            .Concat(handler.Definition.DataOutputs.Select(port => new { id = port.Name, label = port.Name, type = port.Type.Kind, direction = "out" })),
        executionPorts = handler.Definition.ControlInputs.Select(port => new { id = port.Name, label = "Run", direction = "in" })
            .Concat(handler.Definition.ControlOutputs.Select(port => new { id = port.Name, label = "Next", direction = "out" }))
    }).Append(Note).ToArray();

    public static object[] AvailableDefinitions(IReadOnlyList<NodeDefinition> definitions, IReadOnlyDictionary<string, NodeLibrary>? libraries = null)
        => AvailableDefinitions(definitions, libraries, _ => false);

    public static object[] AvailableDefinitions(IReadOnlyList<NodeDefinition> definitions, IReadOnlyDictionary<string, NodeLibrary>? libraries, Func<string, bool> isRegisteredReference)
    {
        var builtinDescriptions = BuiltinPalette(definitions);
        var result = new List<object>();
        foreach (var description in builtinDescriptions.Values) result.Add(Project(description, libraries?.GetValueOrDefault(description.Kind)));
        foreach (var definition in definitions)
        {
            var settings = JsonSerializer.SerializeToElement(new { });
            NodeDefinitionResolution resolved;
            try { resolved = definition.Resolve(settings); }
            catch { resolved = new(definition.DataInputs, definition.DataOutputs, definition.ControlInputs, definition.ControlOutputs, []); }
            result.Add(new { kind = definition.Kind, title = definition.DisplayName ?? Title(definition.Kind), isEntry = definition.ExecutionMode == FlowNodeExecutionMode.Entry,
                semantic = Semantic(definition.ExecutionMode, definition.Kind), executionMode = definition.ExecutionMode.ToString(), effect = definition.Effect.ToString(), navigation = definition.NavigableGraphReference is { } reference && isRegisteredReference(reference) ? reference : null,
                library = libraries?.GetValueOrDefault(definition.Kind), fields = definition.ConfigFields.Select(field => new { name = field.Name, label = field.Label ?? Title(field.Name), required = field.Required, schema = field.Type.ToJsonSchema() }),
                ports = resolved.DataInputs.Select(port => new { id = port.Name, label = port.Name, type = port.Type.Kind, schema = port.Type.ToJsonSchema(), required = port.Required, direction = "in" })
                    .Concat(resolved.DataOutputs.Select(port => new { id = port.Name, label = port.Name, type = port.Type.Kind, schema = port.Type.ToJsonSchema(), required = port.Required, direction = "out" })),
                executionPorts = resolved.ControlInputs.Select(port => new { id = port.Name, label = "Run", direction = "in" }).Concat(resolved.ControlOutputs.Select(port => new { id = port.Name, label = "Next", direction = "out" })), diagnostics = resolved.Issues });
        }
        result.Add(Note); return result.ToArray();
    }

    public static Dictionary<string, object> DescriptionsDefinitions(JsonElement graph, IReadOnlyList<NodeDefinition> definitions, IReadOnlyDictionary<string, NodeLibrary>? libraries = null)
        => DescriptionsDefinitions(graph, definitions, libraries, _ => false);

    public static Dictionary<string, object> DescriptionsDefinitions(JsonElement graph, IReadOnlyList<NodeDefinition> definitions, IReadOnlyDictionary<string, NodeLibrary>? libraries, Func<string, bool> isRegisteredReference)
    {
        var contracts = TypedFlowCompiler.Describe(graph, definitions);
        var edges = graph.GetProperty("edges").EnumerateArray().ToArray();
        var controls = graph.TryGetProperty("controlEdges", out var ce) ? ce.EnumerateArray().ToArray() : [];
        var wiresByNode = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            var sourceId = edge.GetProperty("source").GetString()!; var targetId = edge.GetProperty("target").GetString()!;
            var sourcePort = edge.GetProperty("sourcePort").GetString()!; var targetPort = edge.GetProperty("targetPort").GetString()!;
            var valid = contracts.TryGetValue(sourceId, out var source) && contracts.TryGetValue(targetId, out var target)
                && source.Outputs.FirstOrDefault(port => port.Id == sourcePort)?.Type is { } outputType
                && target.Inputs.FirstOrDefault(port => port.Id == targetPort)?.Type is { } inputType
                && outputType.IsCompatibleWith(inputType);
            if (valid) continue;
            var reason = "Saved wire has incompatible or unavailable port types.";
            void Add(string nodeId, string portId)
            {
                if (!wiresByNode.TryGetValue(nodeId, out var list)) wiresByNode[nodeId] = list = [];
                list.Add(new { source = sourceId, sourcePort, target = targetId, targetPort, port = portId, reason });
            }
            Add(sourceId, sourcePort); Add(targetId, targetPort);
        }
        return graph.GetProperty("nodes").EnumerateArray().ToDictionary(node => node.GetProperty("id").GetString()!, node =>
        {
            var id = node.GetProperty("id").GetString()!; var kind = node.GetProperty("kind").GetString()!;
            if (kind == "note") return Note;
            if (!contracts.TryGetValue(id, out var description)) return new { kind, title = Title(kind), diagnostics = new[] { "Node metadata unavailable." } };
            if (description.Diagnostics.Any(issue => issue.Port == "kind"))
            {
                object[] Saved(JsonElement[] wires) => wires.SelectMany(edge => new[] { "source", "target" }.Where(end => edge.GetProperty(end).GetString() == id)
                    .Select(end => (object)new { id = edge.GetProperty(end + "Port").GetString(), label = edge.GetProperty(end + "Port").GetString(), type = "unavailable", direction = end == "source" ? "out" : "in" })).DistinctBy(port => JsonSerializer.Serialize(port)).ToArray();
                return new { kind, title = Title(kind), className = "unavailable", preview = "Node unavailable · check plugins", fields = Array.Empty<object>(), ports = Saved(edges), executionPorts = Saved(controls), diagnostics = description.Diagnostics };
            }
            return Project(description, libraries?.GetValueOrDefault(kind),
                description.NavigableGraphReference is { } reference && isRegisteredReference(reference) ? reference : null,
                wiresByNode.GetValueOrDefault(id) ?? [],
                definitions.Any(definition => definition.Kind == kind));
        }, StringComparer.Ordinal);
    }

    private static object Project(FlowNodeDescription description, NodeLibrary? library, string? navigation = null, IReadOnlyList<object>? wireDiagnostics = null, bool supportsInputDefaults = false) => new
    {
        kind = description.Kind, title = description.DisplayName, isEntry = description.ExecutionMode == FlowNodeExecutionMode.Entry,
        semantic = Semantic(description.ExecutionMode, description.Kind), executionMode = description.ExecutionMode.ToString(), effect = description.Effect.ToString(),
        library, navigation, diagnostics = description.Diagnostics, wireDiagnostics = wireDiagnostics ?? [],
        fields = description.Configuration.Select(field => new { name = field.Name, label = field.Label ?? Title(field.Name), required = field.Required, schema = field.Type.ToJsonSchema() }),
        ports = description.Inputs.Select(port => new { id = port.Id, label = port.Name, type = port.Type?.Kind ?? "unresolved", schema = port.Type?.ToJsonSchema(), unresolved = port.Type is null, required = port.Required, hasDefault = port.ConstantValue.HasValue, value = port.ConstantValue, editableDefault = supportsInputDefaults && SupportsEditableDefault(port.Type), direction = "in" })
            .Concat(description.Outputs.Select(port => new { id = port.Id, label = port.Name, type = port.Type?.Kind ?? "unresolved", schema = port.Type?.ToJsonSchema(), unresolved = port.Type is null, required = port.Required, hasDefault = false, value = (JsonElement?)null, editableDefault = false, direction = "out" })),
        executionPorts = description.ControlInputs.Select(port => new { id = port.Name, label = "Run", direction = "in" }).Concat(description.ControlOutputs.Select(port => new { id = port.Name, label = "Next", direction = "out" }) )
    };

    private static bool SupportsEditableDefault(FlowType? type) => type is StringType or NumberType or BooleanType or IntegerType or DictionaryType or JsonType
        or NullableType { InnerType: DictionaryType or JsonType };

    private static string Semantic(FlowNodeExecutionMode mode, string kind) => kind == "note" ? "note" : mode switch
    { FlowNodeExecutionMode.Entry => "event", FlowNodeExecutionMode.Pure => "pure", _ => "operation" };

    private static IReadOnlyDictionary<string, FlowNodeDescription> BuiltinPalette(IReadOnlyList<NodeDefinition> definitions)
    {
        var nodes = TypedFlowCompiler.AvailableBuiltinKinds.Select((kind, index) => (object)new Dictionary<string, object?>
        {
            ["id"] = "palette-" + index, ["kind"] = kind, ["settings"] = kind switch
            {
                "accumulate" => new { quietSeconds = 1, maxMessages = 5, maxWaitSeconds = 60 },
                "memory" => new { mode = "hybrid", scope = "person", categories = new[] { "fato" }, maxItems = 5, maxCharacters = 1000 },
                "exit" => new { mode = "switch", targetStateId = "palette" }, "property" => new { path = new[] { "nome" } },
                "branch" => new { @operator = "equal", b = JsonSerializer.SerializeToElement("palette") }, "switch" => new { cases = Array.Empty<object>() },
                _ => new { }
            },
            ["prompt"] = kind == "prompt" ? "palette" : null, ["question"] = kind == "humanFeedback" ? "palette" : null
        }).ToArray();
        var graph = JsonSerializer.SerializeToElement(new { version = 4, nodes, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
        return TypedFlowCompiler.Describe(graph, definitions);
    }

    public static Dictionary<string, object> Descriptions(JsonElement graph, IReadOnlyList<FlowNodeHandler> handlers, IReadOnlyDictionary<string, NodeLibrary>? libraries = null)
    {
        var available = handlers.Select(handler => handler.Definition.Kind).ToHashSet(StringComparer.Ordinal);
        return graph.GetProperty("nodes").EnumerateArray().ToDictionary(node => node.GetProperty("id").GetString()!, node =>
        {
            var id = node.GetProperty("id").GetString()!;
            var kind = node.GetProperty("kind").GetString()!;
            if (kind == "note") return Note;
            var known = available.Contains(kind);
            var palette = Available(handlers, libraries);
            if (known) return palette[handlers.Select(handler => handler.Definition.Kind).ToList().IndexOf(kind)];
            var edges = graph.GetProperty("edges").EnumerateArray().ToArray();
            var controls = graph.TryGetProperty("controlEdges", out var ce) ? ce.EnumerateArray().ToArray() : [];
            object[] Ports(JsonElement[] connections) => connections.SelectMany(edge => new[] { "source", "target" }
                .Where(end => edge.GetProperty(end).GetString() == id)
                .Select(end => (object)new { id = edge.GetProperty(end + "Port").GetString(), label = edge.GetProperty(end + "Port").GetString(), type = "unavailable", direction = end == "source" ? "out" : "in" }))
                .DistinctBy(port => JsonSerializer.Serialize(port)).ToArray();
            return (object)new { kind, title = Title(kind), className = "unavailable", preview = "Node unavailable · check plugins", fields = Array.Empty<object>(), ports = Ports(edges), executionPorts = Ports(controls) };
        });
    }
}
