using System.Text.Json;

namespace Filomena.Core.Flows;

public static class FlowValidator
{
    private static readonly HashSet<string> InputKinds = ["dm-input", "group-input", "manual-input"];
    private static readonly HashSet<FlowEffect> IrreversibleEffects = [FlowEffect.Send, FlowEffect.WriteMemory, FlowEffect.WriteState];

    public static IReadOnlyList<FlowIssue> Validate(GraphDocument graph, NodeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(catalog);

        var issues = new List<FlowIssue>();
        var nodeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, FlowNode>(StringComparer.Ordinal);
        var states = new Dictionary<string, NodeState>(StringComparer.Ordinal);

        IndexNodes(graph, catalog, issues, nodeIndexes, nodes, states);
        ValidateEntries(graph, nodes, issues, out var entries);
        ValidateDataEdges(graph, issues, nodes, states, nodeIndexes);
        ValidateControlEdges(graph, issues, nodes, states, nodeIndexes);
        ValidateRequiredPorts(graph, issues, states, nodeIndexes);
        ValidateCycles(graph, issues, nodes, nodeIndexes);

        var reachable = Reachable(entries, graph, nodes);
        foreach (var node in graph.Nodes.Select((value, index) => (value, index)))
        {
            if (nodes.ContainsKey(node.value.Id) && !reachable.Contains(node.value.Id))
            {
                Add(issues, "graph.unreachable_node", NodePath(node.index), "Node is not reachable from a declared or implicit entry.");
            }
        }

        ValidateDataAvailability(graph, issues, nodes, states, reachable, entries);
        ValidateFirstValidWarnings(graph, issues, nodes, states, nodeIndexes);

        return issues
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Severity)
            .ToArray();
    }

    private static void IndexNodes(
        GraphDocument graph,
        NodeCatalog catalog,
        ICollection<FlowIssue> issues,
        IDictionary<string, int> nodeIndexes,
        IDictionary<string, FlowNode> nodes,
        IDictionary<string, NodeState> states)
    {
        foreach (var (node, index) in graph.Nodes.Select((value, index) => (value, index)))
        {
            var path = NodePath(index);
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                Add(issues, "node.id_invalid", path + "/id", "Node identifier must be non-empty.");
                continue;
            }

            if (!nodeIndexes.TryAdd(node.Id, index))
            {
                Add(issues, "node.id_duplicate", path + "/id", "Node identifiers must be unique.");
                continue;
            }

            nodes[node.Id] = node;
            if (node.Config.ValueKind != JsonValueKind.Object)
            {
                Add(issues, "node.config_invalid", path + "/config", "Node config must be an object.");
            }

            if (!catalog.TryGet(node.Kind, node.BlockVersion, out var definition))
            {
                var code = catalog.Definitions.Any(candidate => string.Equals(candidate.Kind, node.Kind, StringComparison.Ordinal))
                    ? "node.block_version_unsupported"
                    : "node.kind_unsupported";
                Add(issues, code, path + (code == "node.kind_unsupported" ? "/kind" : "/blockVersion"), "Node kind or block version is not supported by the trusted catalog.");
                continue;
            }

            var resolution = definition.Resolve(node.Config);
            foreach (var issue in resolution.Issues)
            {
                Add(issues, issue.Code, path + NormalizeConfigPath(issue.Path), issue.Message, issue.Severity);
            }

            states[node.Id] = new NodeState(node, definition, resolution);
        }
    }

    private static void ValidateEntries(GraphDocument graph, IReadOnlyDictionary<string, FlowNode> nodes, ICollection<FlowIssue> issues, out IReadOnlyList<string> entries)
    {
        if (graph.EntryNodeIds.Count > 0)
        {
            var declared = new List<string>();
            foreach (var (id, index) in graph.EntryNodeIds.Select((value, index) => (value, index)))
            {
                if (!nodes.ContainsKey(id))
                {
                    Add(issues, "graph.entry_node_missing", "/entryNodeIds/" + index, "Declared entry node does not exist.");
                }
                else if (!declared.Contains(id, StringComparer.Ordinal))
                {
                    declared.Add(id);
                }
                else
                {
                    Add(issues, "graph.entry_duplicate", "/entryNodeIds/" + index, "Entry nodes must be unique.");
                }
            }

            if (declared.Count > 1 && !declared.All(id => InputKinds.Contains(nodes[id].Kind)))
            {
                Add(issues, "graph.entry_multiple_unsupported", "/entryNodeIds", "Multiple entries are only supported for the declared DM/group input set.");
            }

            if (declared.Count > 1 && declared.Any(id => nodes[id].Kind == "manual-input"))
            {
                Add(issues, "graph.entry_multiple_unsupported", "/entryNodeIds", "Manual input cannot be combined with the DM/group input set.");
            }

            entries = declared;
            return;
        }

        var incoming = graph.ControlEdges
            .Where(edge => nodes.ContainsKey(edge.Target.NodeId))
            .Select(edge => edge.Target.NodeId)
            .ToHashSet(StringComparer.Ordinal);
        var implicitEntries = nodes.Values
            .Where(node => InputKinds.Contains(node.Kind) && !incoming.Contains(node.Id))
            .Select(node => node.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (implicitEntries.Length == 0)
        {
            Add(issues, "graph.entry_missing", "/entryNodeIds", "Graph must declare an entry or contain one implicit input start.");
        }
        else if (implicitEntries.Length > 1)
        {
            Add(issues, "graph.entry_ambiguous", "/entryNodeIds", "Graph has multiple implicit input starts; declare an input-set entry explicitly.");
        }

        entries = implicitEntries;
    }

    private static void ValidateDataEdges(
        GraphDocument graph,
        ICollection<FlowIssue> issues,
        IReadOnlyDictionary<string, FlowNode> nodes,
        IReadOnlyDictionary<string, NodeState> states,
        IReadOnlyDictionary<string, int> nodeIndexes)
    {
        var producers = new Dictionary<(string NodeId, string Port), DataEdge>();
        foreach (var (edge, index) in graph.DataEdges.Select((value, index) => (value, index)))
        {
            var path = "/dataEdges/" + index;
            if (!nodes.TryGetValue(edge.Source.NodeId, out var sourceNode))
            {
                Add(issues, "edge.source_node_missing", path + "/source/nodeId", "Data edge source node does not exist.");
                continue;
            }

            if (!nodes.TryGetValue(edge.Target.NodeId, out var targetNode))
            {
                Add(issues, "edge.target_node_missing", path + "/target/nodeId", "Data edge target node does not exist.");
                continue;
            }

            if (!states.TryGetValue(sourceNode.Id, out var sourceState) || !states.TryGetValue(targetNode.Id, out var targetState))
            {
                continue;
            }

            if (sourceState.Resolution.DataOutputs.FirstOrDefault(port => string.Equals(port.Name, edge.Source.Port, StringComparison.Ordinal)) is not { } sourcePort)
            {
                Add(issues, "edge.source_port_missing", path + "/source/port", "Data edge source port does not exist.");
                continue;
            }

            if (targetState.Resolution.DataInputs.FirstOrDefault(port => string.Equals(port.Name, edge.Target.Port, StringComparison.Ordinal)) is not { } targetPort)
            {
                Add(issues, "edge.target_port_missing", path + "/target/port", "Data edge target port does not exist.");
                continue;
            }

            if (producers.TryGetValue((edge.Target.NodeId, edge.Target.Port), out var previousProducer))
            {
                if (!IsDeclaredInputAlternative(graph, nodes, previousProducer, edge))
                {
                    Add(issues, "edge.target_producer_duplicate", path + "/target/port", "A data input may have only one producer.");
                }
            }
            else
            {
                producers[(edge.Target.NodeId, edge.Target.Port)] = edge;
            }

            var sourceType = SelectField(sourcePort.Type, edge.FieldPath, issues, path + "/fieldPath");
            if (sourceType is not null && !targetPort.AcceptsAny && !sourceType.IsCompatibleWith(targetPort.Type))
            {
                Add(issues, "edge.type_incompatible", path, "Data edge source type is not assignable to the target port type.");
            }

            if (targetNode.Kind == "condition" && edge.Target.Port == "value" && sourceType is not null)
            {
                ValidateConditionLiteral(targetNode.Config, sourceType, issues, nodeIndexes[targetNode.Id]);
            }
        }
    }

    private static FlowType? SelectField(FlowType sourceType, IReadOnlyList<string>? fieldPath, ICollection<FlowIssue> issues, string path)
    {
        var current = sourceType;
        if (fieldPath is null)
        {
            Add(issues, "edge.field_path_invalid", path, "Data edge field path is missing.");
            return null;
        }

        foreach (var field in fieldPath)
        {
            if (current is not ObjectType objectType || !objectType.Fields.TryGetValue(field, out var selected))
            {
                Add(issues, "edge.field_path_invalid", path, "Data edge field path must select declared object fields.");
                return null;
            }

            current = selected.Type;
        }

        return current;
    }

    private static void ValidateConditionLiteral(JsonElement config, FlowType sourceType, ICollection<FlowIssue> issues, int nodeIndex)
    {
        if (!config.TryGetProperty("value", out var literal))
        {
            return;
        }

        var valid = sourceType switch
        {
            StringType or EnumType => literal.ValueKind == JsonValueKind.String,
            IntegerType => literal.ValueKind == JsonValueKind.Number && literal.TryGetInt32(out _),
            NumberType => literal.ValueKind == JsonValueKind.Number,
            BooleanType => literal.ValueKind is JsonValueKind.True or JsonValueKind.False,
            NullableType nullable => literal.ValueKind == JsonValueKind.Null || IsLiteralCompatible(literal, nullable.InnerType),
            _ => literal.ValueKind == JsonValueKind.Object || literal.ValueKind == JsonValueKind.Array,
        };
        if (!valid)
        {
            Add(issues, "node.condition_literal_type_invalid", NodePath(nodeIndex) + "/config/value", "Condition literal does not match the selected field type.");
        }
    }

    private static bool IsLiteralCompatible(JsonElement literal, FlowType type) => type switch
    {
        StringType or EnumType => literal.ValueKind == JsonValueKind.String,
        IntegerType => literal.ValueKind == JsonValueKind.Number && literal.TryGetInt32(out _),
        NumberType => literal.ValueKind == JsonValueKind.Number,
        BooleanType => literal.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => true,
    };

    private static void ValidateControlEdges(
        GraphDocument graph,
        ICollection<FlowIssue> issues,
        IReadOnlyDictionary<string, FlowNode> nodes,
        IReadOnlyDictionary<string, NodeState> states,
        IReadOnlyDictionary<string, int> nodeIndexes)
    {
        var producers = new Dictionary<(string NodeId, string Port), ControlEdge>();
        foreach (var (edge, index) in graph.ControlEdges.Select((value, index) => (value, index)))
        {
            var path = "/controlEdges/" + index;
            if (!nodes.TryGetValue(edge.Source.NodeId, out var sourceNode))
            {
                Add(issues, "edge.source_node_missing", path + "/source/nodeId", "Control edge source node does not exist.");
                continue;
            }

            if (!nodes.TryGetValue(edge.Target.NodeId, out var targetNode))
            {
                Add(issues, "edge.target_node_missing", path + "/target/nodeId", "Control edge target node does not exist.");
                continue;
            }

            if (!states.TryGetValue(sourceNode.Id, out var sourceState) || !states.TryGetValue(targetNode.Id, out var targetState))
            {
                continue;
            }

            if (!sourceState.Resolution.ControlOutputs.Any(port => string.Equals(port.Name, edge.Source.Port, StringComparison.Ordinal)))
            {
                Add(issues, "edge.source_port_missing", path + "/source/port", "Control edge source port does not exist.");
            }

            if (!targetState.Resolution.ControlInputs.Any(port => string.Equals(port.Name, edge.Target.Port, StringComparison.Ordinal)))
            {
                Add(issues, "edge.target_port_missing", path + "/target/port", "Control edge target port does not exist.");
            }

            if (producers.TryGetValue((edge.Target.NodeId, edge.Target.Port), out var previousProducer) &&
                targetNode.Kind != "join" && !IsDeclaredInputAlternative(graph, nodes, previousProducer, edge))
            {
                Add(issues, "edge.target_producer_duplicate", path + "/target/port", "A control input may have only one producer except named join branches.");
            }
            else
            {
                producers[(edge.Target.NodeId, edge.Target.Port)] = edge;
            }
        }
    }

    private static void ValidateRequiredPorts(
        GraphDocument graph,
        ICollection<FlowIssue> issues,
        IReadOnlyDictionary<string, NodeState> states,
        IReadOnlyDictionary<string, int> nodeIndexes)
    {
        foreach (var (nodeId, state) in states)
        {
            var nodePath = NodePath(nodeIndexes[nodeId]);
            var dataTargets = graph.DataEdges.Where(edge => edge.Target.NodeId == nodeId).Select(edge => edge.Target.Port).ToHashSet(StringComparer.Ordinal);
            foreach (var port in state.Resolution.DataInputs.Where(port => port.Required))
            {
                if (!dataTargets.Contains(port.Name))
                {
                    Add(issues, "node.input_missing", nodePath + "/dataInputs/" + Escape(port.Name), "Required data input is not connected.");
                }
            }

            var controlSources = graph.ControlEdges.Where(edge => edge.Source.NodeId == nodeId).Select(edge => edge.Source.Port).ToHashSet(StringComparer.Ordinal);
            foreach (var port in state.Resolution.ControlOutputs.Where(port => port.Required))
            {
                if (!controlSources.Contains(port.Name))
                {
                    var code = state.Definition.Kind switch
                    {
                        "condition" => "node.decision_exit_missing",
                        "await" => "node.await_contract_invalid",
                        _ => "node.control_exit_missing",
                    };
                    Add(issues, code, nodePath + "/controlOutputs/" + Escape(port.Name), "Required control exit is not connected.");
                }
            }

            var controlTargets = graph.ControlEdges.Where(edge => edge.Target.NodeId == nodeId).Select(edge => edge.Target.Port).ToHashSet(StringComparer.Ordinal);
            foreach (var port in state.Resolution.ControlInputs.Where(port => port.Required))
            {
                if (!controlTargets.Contains(port.Name))
                {
                    Add(issues, "node.control_input_missing", nodePath + "/controlInputs/" + Escape(port.Name), "Required control input is not connected.");
                }
            }
        }
    }

    private static void ValidateCycles(GraphDocument graph, ICollection<FlowIssue> issues, IReadOnlyDictionary<string, FlowNode> nodes, IReadOnlyDictionary<string, int> nodeIndexes)
    {
        var controlCycle = FindCycle(graph.ControlEdges, nodes.Keys);
        if (controlCycle.Count > 0)
        {
            Add(issues, "graph.control_cycle", NodePath(nodeIndexes[controlCycle[0]]), "Control graph must be acyclic.");
        }

        var dataEdges = graph.DataEdges.Select(edge => new ControlEdge(edge.Source, edge.Target));
        var dataCycle = FindCycle(dataEdges, nodes.Keys);
        if (dataCycle.Count > 0)
        {
            Add(issues, "graph.data_cycle", NodePath(nodeIndexes[dataCycle[0]]), "Data dependencies must be acyclic.");
        }

        if (controlCycle.Count == 0 && dataCycle.Count == 0)
        {
            var combinedCycle = FindCycle(graph.ControlEdges.Concat(dataEdges), nodes.Keys);
            if (combinedCycle.Count > 0)
            {
                Add(issues, "graph.dependency_cycle", NodePath(nodeIndexes[combinedCycle[0]]), "Combined control and data dependencies must be acyclic.");
            }
        }
    }

    private static IReadOnlyList<string> FindCycle(IEnumerable<ControlEdge> edges, IEnumerable<string> nodeIds)
    {
        var adjacency = nodeIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (adjacency.TryGetValue(edge.Source.NodeId, out var targets) && adjacency.ContainsKey(edge.Target.NodeId))
            {
                targets.Add(edge.Target.NodeId);
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in adjacency.Keys.Order(StringComparer.Ordinal))
        {
            if (Visit(id, adjacency, visiting, visited, out var cycle))
            {
                return cycle;
            }
        }

        return [];
    }

    private static bool Visit(string id, IReadOnlyDictionary<string, List<string>> adjacency, ISet<string> visiting, ISet<string> visited, out IReadOnlyList<string> cycle)
    {
        if (!visiting.Add(id))
        {
            cycle = [id];
            return true;
        }

        if (visited.Contains(id))
        {
            visiting.Remove(id);
            cycle = [];
            return false;
        }

        foreach (var target in adjacency[id].Order(StringComparer.Ordinal))
        {
            if (Visit(target, adjacency, visiting, visited, out cycle))
            {
                return true;
            }
        }

        visiting.Remove(id);
        visited.Add(id);
        cycle = [];
        return false;
    }

    private static HashSet<string> Reachable(IReadOnlyList<string> entries, GraphDocument graph, IReadOnlyDictionary<string, FlowNode> nodes)
    {
        var adjacency = graph.ControlEdges
            .Where(edge => nodes.ContainsKey(edge.Source.NodeId) && nodes.ContainsKey(edge.Target.NodeId))
            .GroupBy(edge => edge.Source.NodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Target.NodeId).ToArray(), StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(entries);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!result.Add(id) || !adjacency.TryGetValue(id, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                pending.Enqueue(target);
            }
        }

        return result;
    }

    private static void ValidateDataAvailability(
        GraphDocument graph,
        ICollection<FlowIssue> issues,
        IReadOnlyDictionary<string, FlowNode> nodes,
        IReadOnlyDictionary<string, NodeState> states,
        IReadOnlySet<string> reachable,
        IReadOnlyList<string> entries)
    {
        if (reachable.Count == 0)
        {
            return;
        }

        var predecessors = reachable.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var edge in graph.ControlEdges)
        {
            if (predecessors.TryGetValue(edge.Target.NodeId, out var values) && reachable.Contains(edge.Source.NodeId))
            {
                values.Add(edge.Source.NodeId);
            }
        }

        var dominators = reachable.ToDictionary(id => id, _ => reachable.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (dominators.ContainsKey(entry))
            {
                dominators[entry] = [entry];
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var id in reachable.Except(entries, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var preds = predecessors[id];
                if (preds.Count == 0)
                {
                    continue;
                }

                var orderedPredecessors = preds.Order(StringComparer.Ordinal).ToArray();
                var intersection = dominators[orderedPredecessors[0]].ToHashSet(StringComparer.Ordinal);
                foreach (var predecessor in orderedPredecessors.Skip(1))
                {
                    intersection.IntersectWith(dominators[predecessor]);
                }

                intersection.Add(id);
                if (!intersection.SetEquals(dominators[id]))
                {
                    dominators[id] = intersection;
                    changed = true;
                }
            }
        }

        foreach (var (edge, index) in graph.DataEdges.Select((value, index) => (value, index)))
        {
            if (!reachable.Contains(edge.Source.NodeId) || !reachable.Contains(edge.Target.NodeId) || !nodes.ContainsKey(edge.Source.NodeId) || !nodes.ContainsKey(edge.Target.NodeId))
            {
                continue;
            }

            if (IsDeclaredInputAlternative(graph, nodes, edge, edge) ||
                states.TryGetValue(edge.Target.NodeId, out var targetState) && targetState.Definition.Kind == "join" &&
                targetState.Resolution.DataInputs.Any(port => port.Name == edge.Target.Port))
            {
                continue;
            }

            if (!dominators.TryGetValue(edge.Target.NodeId, out var available) || !available.Contains(edge.Source.NodeId))
            {
                Add(issues, "data.value_unavailable", "/dataEdges/" + index, "Data value is not available along every reachable control path to the target.");
            }
        }
    }

    private static bool IsDeclaredInputAlternative(
        GraphDocument graph,
        IReadOnlyDictionary<string, FlowNode> nodes,
        DataEdge first,
        DataEdge second) =>
        graph.EntryNodeIds.Count > 1 &&
        graph.EntryNodeIds.Contains(first.Source.NodeId, StringComparer.Ordinal) &&
        graph.EntryNodeIds.Contains(second.Source.NodeId, StringComparer.Ordinal) &&
        nodes.TryGetValue(first.Source.NodeId, out var firstNode) &&
        nodes.TryGetValue(second.Source.NodeId, out var secondNode) &&
        firstNode.Kind is "dm-input" or "group-input" &&
        secondNode.Kind is "dm-input" or "group-input" &&
        first.Target == second.Target;

    private static bool IsDeclaredInputAlternative(
        GraphDocument graph,
        IReadOnlyDictionary<string, FlowNode> nodes,
        ControlEdge first,
        ControlEdge second) =>
        graph.EntryNodeIds.Count > 1 &&
        graph.EntryNodeIds.Contains(first.Source.NodeId, StringComparer.Ordinal) &&
        graph.EntryNodeIds.Contains(second.Source.NodeId, StringComparer.Ordinal) &&
        nodes.TryGetValue(first.Source.NodeId, out var firstNode) &&
        nodes.TryGetValue(second.Source.NodeId, out var secondNode) &&
        firstNode.Kind is "dm-input" or "group-input" &&
        secondNode.Kind is "dm-input" or "group-input" &&
        first.Target == second.Target;

    private static void ValidateFirstValidWarnings(
        GraphDocument graph,
        ICollection<FlowIssue> issues,
        IReadOnlyDictionary<string, FlowNode> nodes,
        IReadOnlyDictionary<string, NodeState> states,
        IReadOnlyDictionary<string, int> nodeIndexes)
    {
        var reverse = graph.ControlEdges
            .Where(edge => nodes.ContainsKey(edge.Source.NodeId) && nodes.ContainsKey(edge.Target.NodeId))
            .GroupBy(edge => edge.Target.NodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Source.NodeId).ToArray(), StringComparer.Ordinal);

        foreach (var state in states.Values.Where(state => state.Definition.Kind == "join" && HasFirstValidMode(state.Node.Config)))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>([state.Node.Id]);
            var effectFound = false;
            while (pending.Count > 0)
            {
                var id = pending.Pop();
                if (!seen.Add(id) || !reverse.TryGetValue(id, out var predecessors))
                {
                    continue;
                }

                foreach (var predecessor in predecessors)
                {
                    if (states.TryGetValue(predecessor, out var predecessorState) && IrreversibleEffects.Contains(predecessorState.Definition.Effect))
                    {
                        effectFound = true;
                    }

                    pending.Push(predecessor);
                }
            }

            if (effectFound)
            {
                Add(issues, "node.first_valid_effect_warning", NodePath(nodeIndexes[state.Node.Id]), "First-valid join has an irreversible effect before the join; cancellation cannot guarantee exactly-once behavior.", FlowIssueSeverity.Warning);
            }
        }
    }

    private static bool HasFirstValidMode(JsonElement config) =>
        config.ValueKind == JsonValueKind.Object && config.TryGetProperty("mode", out var mode) &&
        mode.ValueKind == JsonValueKind.String && string.Equals(mode.GetString()?.Replace("-", string.Empty, StringComparison.Ordinal), "firstvalid", StringComparison.OrdinalIgnoreCase);

    private static void Add(ICollection<FlowIssue> issues, string code, string path, string message, FlowIssueSeverity severity = FlowIssueSeverity.Error) =>
        issues.Add(new FlowIssue(code, path, message, severity));

    private static string NormalizeConfigPath(string path) =>
        path.StartsWith("/config", StringComparison.Ordinal) ? path : "/config" + (path == "/" ? string.Empty : path);

    private static string NodePath(int index) => "/nodes/" + index;

    private static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private sealed record NodeState(FlowNode Node, NodeDefinition Definition, NodeDefinitionResolution Resolution);
}
