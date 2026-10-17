using System.Text.Json;
using Cortex.Core.Boundaries;

namespace Cortex.Core.Flows;

public sealed record TypedFlowIssue(string NodeId, string Port, string Reason);
public sealed class TypedFlowException(IReadOnlyList<TypedFlowIssue> issues) : Exception("Graph inválido")
{
    public IReadOnlyList<TypedFlowIssue> Issues { get; } = issues;
}
public sealed record TypedPort(string Id, string Name, FlowType Type, bool Required, JsonElement Config);
public sealed record TypedNode(FlowNode Source, IReadOnlyList<TypedPort> Inputs, IReadOnlyList<TypedPort> Outputs, IReadOnlyList<string> Exits)
{
    internal FlowEffect? HostEffect { get; init; }
    internal FlowNodeExecutionMode? HostExecutionMode { get; init; }
}
public sealed record TypedFlow(GraphDocument Graph, string EntryId, IReadOnlyDictionary<string, TypedNode> Nodes)
{
    internal IReadOnlyDictionary<string, FlowNodeHandler> HostNodes { get; init; } = new Dictionary<string, FlowNodeHandler>();
}

public static class TypedFlowCompiler
{
    public const int CurrentGraphVersion = 4;
    /// <summary>Describes nodes without creating sessions or binding host delegates.</summary>
    public static IReadOnlyDictionary<string, FlowNodeDescription> Describe(JsonElement source, IEnumerable<NodeDefinition> definitions) =>
        FlowNodeDescriptionCatalog.Describe(source, definitions);

    /// <summary>Describes nodes and projects exact compiler ports whenever the graph compiles.</summary>
    public static IReadOnlyDictionary<string, FlowNodeDescription> Describe(JsonElement source, IEnumerable<FlowNodeHandler> hostNodes) =>
        FlowNodeDescriptionCatalog.Describe(source, hostNodes);

    internal static bool IsTextOutput(JsonElement config)
    {
        if (!config.TryGetProperty("outputMode", out var mode)) return false;
        if (mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("text" or "structured"))
            throw new InvalidDataException("Formato da saída deve ser text ou structured.");
        return mode.GetString() == "text";
    }

    private static readonly HashSet<string> ExecutableKinds =
        ["dm", "group", "accumulate", "history", "memory", "interpretBoundaries", "exit", "property", "count", "prompt", "switch", "branch", "send", "humanFeedback"];
    public static IReadOnlyList<string> AvailableBuiltinKinds => ExecutableKinds.Order(StringComparer.Ordinal).ToArray();
    internal static bool IsBuiltinKind(string kind) => kind == "note" || ExecutableKinds.Contains(kind);

    public static TypedFlow Compile(JsonElement source, string entryId) => CompileCore(source, entryId, true, new Dictionary<string, FlowNodeHandler>(), new Dictionary<string, NodeDefinition>());

    /// <summary>Validates a graph from metadata only, without binding host delegates.</summary>
    public static TypedFlow Validate(JsonElement source, string entryId, IEnumerable<NodeDefinition> definitions)
    {
        var available = definitions.ToDictionary(definition => definition.Kind, StringComparer.Ordinal);
        if (available.Keys.Any(IsBuiltinKind)) throw new ArgumentException("Host definitions cannot replace built-in node kinds.", nameof(definitions));
        return CompileCore(source, entryId, true, new Dictionary<string, FlowNodeHandler>(), available);
    }

    public static TypedFlow Compile(JsonElement source, string entryId, IEnumerable<FlowNodeHandler> hostNodes)
    {
        var handlers = hostNodes.ToDictionary(handler => handler.Definition.Kind, StringComparer.Ordinal);
        if (handlers.Keys.Any(IsBuiltinKind)) throw new ArgumentException("Host nodes cannot replace built-in node kinds.", nameof(hostNodes));
        return CompileCore(source, entryId, true, handlers, handlers.ToDictionary(pair => pair.Key, pair => pair.Value.Definition, StringComparer.Ordinal));
    }

    private static TypedFlow CompileCore(JsonElement source, string entryId, bool validateAlternateEntries,
        IReadOnlyDictionary<string, FlowNodeHandler> hostNodes, IReadOnlyDictionary<string, NodeDefinition> suppliedDefinitions)
    {
        var issues = new List<TypedFlowIssue>();
        void Error(string node, string port, string reason) => issues.Add(new(node, port, reason));
        var hostDefinitions = suppliedDefinitions.Count == 0
            ? hostNodes.ToDictionary(pair => pair.Key, pair => pair.Value.Definition, StringComparer.Ordinal) : suppliedDefinitions;
        bool IsEntry(FlowNode node) => node.Kind is "dm" or "group" || hostDefinitions.TryGetValue(node.Kind, out var definition) && definition.ExecutionMode == FlowNodeExecutionMode.Entry;
        bool IsPure(FlowNode node) => node.Kind is "property" or "count" || hostDefinitions.TryGetValue(node.Kind, out var definition) && definition.ExecutionMode == FlowNodeExecutionMode.Pure;
        try
        {
            int? graphVersion = source.TryGetProperty("version", out var versionElement) && versionElement.TryGetInt32(out var parsedVersion)
                ? parsedVersion : null;
            if (graphVersion != CurrentGraphVersion)
                throw new TypedFlowException([new("graph", "version",
                    $"Graph version {graphVersion?.ToString() ?? "missing or invalid"} is unsupported; expected version {CurrentGraphVersion}.")]);
            var raw = source.GetProperty("nodes").EnumerateArray().ToArray();
            if (raw.Length is < 1 or > 64) throw new InvalidDataException("Use 1–64 nodes.");
            var allNodes = raw.Select(n => new FlowNode(n.GetProperty("id").GetString()!, n.GetProperty("kind").GetString()!, 1, n)).ToDictionary(n => n.Id);
            var allData = source.GetProperty("edges").EnumerateArray().Select(e => new DataEdge(Ref(e, "source"), [], Ref(e, "target"))).ToArray();
            var allControl = source.TryGetProperty("controlEdges", out var ce) ? ce.EnumerateArray().Select(e => new ControlEdge(Ref(e, "source"), Ref(e, "target"))).ToArray() : [];
            var nodes = allNodes;
            var data = allData;
            var control = allControl;
            if (data.Length > 128 || control.Length > 128) throw new InvalidDataException("Máximo de 128 fios de cada tipo.");
            var graph = new GraphDocument("local", CurrentGraphVersion, GraphRole.Character, null, nodes.Values, data, control, [entryId]);
            if (!nodes.TryGetValue(entryId, out var entry) || !IsEntry(entry)) throw new InvalidDataException("Selecione uma entrada DM/grupo ou uma entrada registrada pelo host.");
            ValidateGlobalStructure(allNodes, allData, allControl, issues, hostNodes, hostDefinitions, IsEntry, IsPure);
            if (issues.Count > 0) throw new TypedFlowException(issues);
            // DM and group may intentionally feed the same typed input. Compile one
            // deterministic entry path and retain the single-producer rule inside it.
            var activeControlNodes = new HashSet<string>(StringComparer.Ordinal) { entryId };
            var pending = new Queue<string>();
            pending.Enqueue(entryId);
            while (pending.TryDequeue(out var current))
                foreach (var edge in allControl.Where(item => item.Source.NodeId == current))
                    if (activeControlNodes.Add(edge.Target.NodeId)) pending.Enqueue(edge.Target.NodeId);

            var inactiveEntries = allNodes.Values
                .Where(node => IsEntry(node) && node.Id != entryId)
                .Select(node => node.Id)
                .ToHashSet(StringComparer.Ordinal);
            var inactiveControlNodes = new HashSet<string>(inactiveEntries, StringComparer.Ordinal);
            var inactivePending = new Queue<string>(inactiveEntries);
            while (inactivePending.TryDequeue(out var current))
                foreach (var edge in allControl.Where(item => item.Source.NodeId == current))
                    if (inactiveControlNodes.Add(edge.Target.NodeId)) inactivePending.Enqueue(edge.Target.NodeId);
            var scopedNodeIds = new HashSet<string>(activeControlNodes, StringComparer.Ordinal);
            var dataPending = new Queue<string>(activeControlNodes);
            while (dataPending.TryDequeue(out var target))
                foreach (var edge in allData.Where(item => item.Target.NodeId == target &&
                    !inactiveEntries.Contains(item.Source.NodeId) &&
                    (!inactiveControlNodes.Contains(item.Source.NodeId) || activeControlNodes.Contains(item.Source.NodeId))))
                    if (scopedNodeIds.Add(edge.Source.NodeId)) dataPending.Enqueue(edge.Source.NodeId);

            nodes = allNodes.Where(item => scopedNodeIds.Contains(item.Key)).ToDictionary();
            data = allData.Where(edge => scopedNodeIds.Contains(edge.Target.NodeId) && scopedNodeIds.Contains(edge.Source.NodeId)).ToArray();
            control = allControl.Where(edge => activeControlNodes.Contains(edge.Source.NodeId)).ToArray();
            graph = new GraphDocument("local", CurrentGraphVersion, GraphRole.Character, null, nodes.Values, data, control, [entryId]);

            var resolved = new Dictionary<string, TypedNode>();
            var resolvedContracts = new Dictionary<string, ResolvedFlowNode>();
            var visiting = new HashSet<string>();
            ResolvedFlowNode ResolveContract(string id)
            {
                if (resolvedContracts.TryGetValue(id, out var cached)) return cached;
                if (!nodes.TryGetValue(id, out var node)) throw new InvalidDataException("Node inexistente: " + id);
                if (!visiting.Add(id)) throw new InvalidDataException("Ciclo de dados.");
                var contract = TypedFlowNodeResolver.Resolve(node, hostDefinitions, portName =>
                {
                    var links = data.Where(edge => edge.Target == new PortRef(id, portName)).ToArray();
                    if (links.Length != 1) return null;
                    var upstream = ResolveContract(links[0].Source.NodeId);
                    return upstream.Outputs.FirstOrDefault(port => port.Id == links[0].Source.Port);
                }, allowUnresolved: false, data, control);
                visiting.Remove(id);
                resolvedContracts[id] = contract;
                return contract;
            }
            TypedNode Resolve(string id)
            {
                if (resolved.TryGetValue(id, out var cached)) return cached;
                var contract = ResolveContract(id);
                static TypedPort Convert(ResolvedFlowPort port) => new(port.Id, port.Name,
                    port.Type ?? throw new InvalidDataException("Port type is unresolved: " + port.Name), port.Required, port.Config);
                var typed = new TypedNode(contract.Source, contract.Inputs.Select(Convert).ToArray(),
                    contract.Outputs.Select(Convert).ToArray(), contract.ControlOutputs.Select(port => port.Name).ToArray())
                {
                    HostEffect = hostNodes.ContainsKey(contract.Source.Kind) ? contract.Effect : null,
                    HostExecutionMode = hostNodes.ContainsKey(contract.Source.Kind) ? contract.ExecutionMode : null
                };
                resolved[id] = typed;
                return typed;
            }            foreach (var id in nodes.Keys)
                try { Resolve(id); } catch (Exception e) when (ConfigurationError(e)) { Error(id, "config", e.Message); visiting.Clear(); }
            foreach (var edge in data)
            {
                if (!resolved.TryGetValue(edge.Source.NodeId, out var from) || !resolved.TryGetValue(edge.Target.NodeId, out var to)) { Error(edge.Target.NodeId, edge.Target.Port, "Origem/destino inválido."); continue; }
                var output = from.Outputs.FirstOrDefault(p => p.Id == edge.Source.Port);
                var input = to.Inputs.FirstOrDefault(p => p.Id == edge.Target.Port);
                if (output is null || input is null) Error(to.Source.Id, edge.Target.Port, "Porta removida ou inexistente.");
                else if (!output.Type.IsCompatibleWith(input.Type)) Error(to.Source.Id, input.Id, "Tipos incompatíveis.");
            }
            foreach (var node in resolved.Values) foreach (var port in node.Inputs)
            {
                var count = data.Count(e => e.Target == new PortRef(node.Source.Id, port.Id));
                var configured = port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _);
                if (count > 1 || (count > 0 && configured)) Error(node.Source.Id, port.Id, "Use um fio OU valor configurado.");
                if (count == 0 && port.Required && !configured) Error(node.Source.Id, port.Id, "Entrada obrigatória ausente.");
            }
            foreach (var edge in control)
            {
                if (!resolved.TryGetValue(edge.Source.NodeId, out var from) || !from.Exits.Contains(edge.Source.Port)) Error(edge.Source.NodeId, edge.Source.Port, "Saída de execução inexistente.");
                if (!resolved.TryGetValue(edge.Target.NodeId, out var to) || IsPure(to.Source) || IsEntry(to.Source) || edge.Target.Port != "exec") Error(edge.Target.NodeId, edge.Target.Port, "Entrada de execução inexistente.");
            }
            foreach (var group in control.GroupBy(e => e.Source)) if (group.Count() > 1) Error(group.Key.NodeId, group.Key.Port, "Sem fan-out: conecte apenas um próximo node.");
            DetectCycles(nodes.Keys, data.Select(e => (e.Source.NodeId, e.Target.NodeId)), issues);
            DetectCycles(nodes.Keys, control.Select(e => (e.Source.NodeId, e.Target.NodeId)), issues);
            if (issues.Count == 0) ValidateAvailability(graph, resolved, entryId, issues, IsPure);
            if (issues.Count == 0 && validateAlternateEntries)
                foreach (var alternate in allNodes.Values.Where(node => IsEntry(node) && node.Id != entryId))
                    try { _ = CompileCore(source, alternate.Id, false, hostNodes, hostDefinitions); }
                    catch (TypedFlowException exception) { issues.AddRange(exception.Issues); }
            if (issues.Count > 0) throw new TypedFlowException(issues);
            return new(graph, entryId, resolved) { HostNodes = hostNodes };
        }
        catch (Exception e) when (ConfigurationError(e)) { throw new TypedFlowException([new("graph", "config", e.Message)]); }
    }

    private static bool ConfigurationError(Exception e) => e is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or FlowSchemaException or BoundaryCompilationException;

    internal static IReadOnlyDictionary<string, JsonElement> ConfiguredInputValues(
        string nodeId, JsonElement settings, IReadOnlyList<FlowDataPortDefinition> inputs,
        ICollection<TypedFlowIssue>? metadataIssues = null)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        void Report(string port, string message)
        {
            var issue = new TypedFlowIssue(nodeId, port, message);
            if (metadataIssues is null) throw new TypedFlowException([issue]);
            metadataIssues.Add(issue);
        }

        if (settings.ValueKind != JsonValueKind.Object)
        {
            Report("inputValues", "Node settings must be an object to configure input values.");
            return values;
        }
        if (!settings.TryGetProperty("inputValues", out var configured))
            return values;
        if (configured.ValueKind != JsonValueKind.Object)
        {
            Report("inputValues", "Configured input values must be an object keyed by declared input name.");
            return values;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in configured.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                Report(property.Name, "Configured input name is duplicated.");
                continue;
            }
            var port = inputs.FirstOrDefault(input => input.Name == property.Name);
            if (port is null)
            {
                Report(property.Name, "Configured value targets an undeclared input.");
                continue;
            }
            try
            {
                FlowDomain.Check(property.Value, port.Type);
            }
            catch (InvalidDataException exception)
            {
                Report(property.Name, exception.Message);
                continue;
            }
            values.Add(property.Name, property.Value.Clone());
        }

        return values;
    }

    private static void ValidateGlobalStructure(
        IReadOnlyDictionary<string, FlowNode> nodes,
        IReadOnlyList<DataEdge> data,
        IReadOnlyList<ControlEdge> control,
        List<TypedFlowIssue> issues,
        IReadOnlyDictionary<string, FlowNodeHandler> hostNodes, IReadOnlyDictionary<string, NodeDefinition> hostDefinitions,
        Func<FlowNode, bool> isEntry, Func<FlowNode, bool> isPure)
    {
        foreach (var node in nodes.Values)
        {
            if (!IsBuiltinKind(node.Kind) && !hostDefinitions.ContainsKey(node.Kind)) issues.Add(new(node.Id, "kind", "Node fora deste recorte executável: " + node.Kind));
            if (node.Kind == "note" && (data.Any(edge => edge.Source.NodeId == node.Id || edge.Target.NodeId == node.Id) ||
                control.Any(edge => edge.Source.NodeId == node.Id || edge.Target.NodeId == node.Id)))
                issues.Add(new(node.Id, "ports", "Notas são comentários e não possuem portas."));
        }
        foreach (var edge in data)
        {
            if (!nodes.ContainsKey(edge.Source.NodeId)) issues.Add(new(edge.Source.NodeId, edge.Source.Port, "Origem inexistente."));
            if (!nodes.ContainsKey(edge.Target.NodeId)) issues.Add(new(edge.Target.NodeId, edge.Target.Port, "Destino inexistente."));
        }
        foreach (var edge in control)
        {
            if (!nodes.ContainsKey(edge.Source.NodeId)) issues.Add(new(edge.Source.NodeId, edge.Source.Port, "Origem inexistente."));
            if (!nodes.ContainsKey(edge.Target.NodeId)) issues.Add(new(edge.Target.NodeId, edge.Target.Port, "Destino inexistente."));
        }
        foreach (var group in control.GroupBy(edge => edge.Source))
            if (group.Count() > 1) issues.Add(new(group.Key.NodeId, group.Key.Port, "Sem fan-out: conecte apenas um próximo node."));
        DetectCycles(nodes.Keys, data.Select(edge => (edge.Source.NodeId, edge.Target.NodeId)), issues);
        DetectCycles(nodes.Keys, control.Select(edge => (edge.Source.NodeId, edge.Target.NodeId)), issues);

        var reachable = new HashSet<string>(nodes.Values.Where(isEntry).Select(node => node.Id), StringComparer.Ordinal);
        var pending = new Queue<string>(reachable);
        while (pending.TryDequeue(out var current))
            foreach (var edge in control.Where(item => item.Source.NodeId == current))
                if (reachable.Add(edge.Target.NodeId)) pending.Enqueue(edge.Target.NodeId);
        foreach (var node in nodes.Values.Where(node => node.Kind is not ("dm" or "group" or "note") && !isPure(node)))
            if (!reachable.Contains(node.Id)) issues.Add(new(node.Id, "exec", "Node operacional não alcançado por nenhuma entrada."));
    }
    private static PortRef Ref(JsonElement e, string end) => new(e.GetProperty(end).GetString()!, e.GetProperty(end + "Port").GetString()!);
    public static string[] PropertyPath(JsonElement config) => config.GetProperty("settings").GetProperty("path").EnumerateArray().Select(p => p.GetString()!).ToArray();
    public static bool HasOther(JsonElement config) => config.GetProperty("settings").TryGetProperty("other", out var other) && other.ValueKind == JsonValueKind.True;
    internal static void ValidateAccumulator(JsonElement config)
    {
        var settings = config.GetProperty("settings");
        var enabled = 0;
        foreach (var key in new[] { "quietSeconds", "maxMessages", "maxWaitSeconds" })
        {
            var value = settings.GetProperty(key);
            if (value.ValueKind == JsonValueKind.Null) continue;
            if (key == "maxMessages")
            {
                if (!value.TryGetInt32(out var count) || count is < 1 or > 10000) throw new InvalidDataException("maxMessages deve ser inteiro positivo até 10000.");
            }
            else if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var seconds) || !double.IsFinite(seconds) || seconds <= 0 || seconds > 86400)
            {
                throw new InvalidDataException(key + " deve ser positivo e no máximo 86400 segundos.");
            }
            enabled++;
        }
        if (enabled == 0) throw new InvalidDataException("Acumular precisa de ao menos um limite ativo.");
    }
    internal static TypedPort[] Fields(string nodeId, JsonElement config, string key, bool output, ICollection<TypedFlowIssue>? metadataIssues = null)
    {
        if (!config.TryGetProperty(key, out var fields)) return [];
        if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() > 32) throw new InvalidDataException("Lista de campos inválida.");
        var ids = new HashSet<string>(); var names = new HashSet<string>();
        var resolved = new List<TypedPort>();
        foreach (var field in fields.EnumerateArray())
        {
            TypedPort? knownPort = null;
            try
            {
                var id = field.GetProperty("id").GetString()!; var name = field.GetProperty("name").GetString()!;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !ids.Add(id) || !names.Add(name)) throw new InvalidDataException("IDs e nomes de campos devem ser únicos e preenchidos.");
                var type = FlowDomain.Parse(field);
                if (output && type is not (StringType or NumberType or BooleanType or EnumType
                    or NullableType { InnerType: StringType or NumberType or BooleanType or EnumType }))
                    throw new InvalidDataException("Resposta JSON deve ser flat/escalar.");
                if (type is EnumType en && en.Values.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Enum vazio.");
                knownPort = new TypedPort(id, name, type, output || !field.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.False, field.Clone());
                foreach (var bound in new[] { "min", "max" }) if (field.TryGetProperty(bound, out var v) && (type is not NumberType || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d))) throw new InvalidDataException("Limite numérico inválido.");
                if (field.TryGetProperty("min", out var min) && field.TryGetProperty("max", out var max) && min.GetDouble() > max.GetDouble()) throw new InvalidDataException("Mínimo maior que máximo.");
                if (field.TryGetProperty("maxWords", out var words) && (type is not StringType || !words.TryGetInt32(out var w) || w < 1)) throw new InvalidDataException("maxWords inválido.");
                if (!output && field.TryGetProperty("value", out var value))
                {
                    if (type is not (StringType or NumberType or BooleanType or EnumType)) throw new InvalidDataException("Valor local somente escalar.");
                    FlowDomain.Check(value, type);
                }
                resolved.Add(knownPort);
            }
            catch (Exception e) when (ConfigurationError(e))
            {
                var port = field.ValueKind == JsonValueKind.Object && field.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String ? idValue.GetString()! : key;
                if (metadataIssues is not null)
                {
                    metadataIssues.Add(new(nodeId, port, e.Message));
                    if (knownPort is not null) resolved.Add(knownPort);
                    continue;
                }
                throw new TypedFlowException([new(nodeId, port, e.Message)]);
            }
        }
        return resolved.ToArray();
    }

    private static void DetectCycles(IEnumerable<string> ids, IEnumerable<(string Source, string Target)> links, List<TypedFlowIssue> issues)
    {
        var edges = links.ToArray(); var visited = new HashSet<string>(); var active = new HashSet<string>();
        void Visit(string id)
        {
            if (active.Contains(id)) { issues.Add(new(id, "edges", "Ciclo não permitido.")); return; }
            if (!visited.Add(id)) return; active.Add(id);
            foreach (var edge in edges.Where(e => e.Source == id)) Visit(edge.Target);
            active.Remove(id);
        }
        foreach (var id in ids) Visit(id);
    }

    private static void ValidateAvailability(GraphDocument graph, Dictionary<string, TypedNode> nodes, string entry, List<TypedFlowIssue> issues,
        Func<FlowNode, bool> isPure)
    {
        var paths = new Dictionary<string, HashSet<string>>(); var visits = 0;
        void Walk(string id, HashSet<string> prior)
        {
            if (++visits > 4096) throw new InvalidDataException("Graph excede limite de caminhos.");
            if (paths.TryGetValue(id, out var known)) known.IntersectWith(prior); else paths[id] = new(prior);
            var next = new HashSet<string>(prior) { id };
            foreach (var e in graph.ControlEdges.Where(e => e.Source.NodeId == id))
            {
                var available = new HashSet<string>(next);
                if (nodes[id].Source.Kind == "humanFeedback" && e.Source.Port == "timeout") available.Remove(id);
                Walk(e.Target.NodeId, available);
            }
        }
        Walk(entry, []);
        IEnumerable<string> Producers(string id)
        {
            if (!isPure(nodes[id].Source)) return [id];
            return graph.DataEdges.Where(e => e.Target.NodeId == id).SelectMany(e => Producers(e.Source.NodeId));
        }
        foreach (var consumer in paths.Keys)
            foreach (var edge in graph.DataEdges.Where(e => e.Target.NodeId == consumer))
                foreach (var producer in Producers(edge.Source.NodeId))
                    if (!paths[consumer].Contains(producer)) issues.Add(new(consumer, edge.Target.Port, "Produtor não executa em todos os caminhos anteriores: " + producer));
        foreach (var node in nodes.Values.Where(n => n.Source.Kind is not ("dm" or "group") && !isPure(n.Source)))
            if (!paths.ContainsKey(node.Source.Id)) issues.Add(new(node.Source.Id, "exec", "Node operacional não alcançado pela entrada selecionada."));
    }
}
