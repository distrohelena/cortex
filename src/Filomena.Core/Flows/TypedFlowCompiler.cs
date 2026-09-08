using System.Text.Json;

namespace Filomena.Core.Flows;

public sealed record TypedFlowIssue(string NodeId, string Port, string Reason);
public sealed class TypedFlowException(IReadOnlyList<TypedFlowIssue> issues) : Exception("Graph inválido")
{
    public IReadOnlyList<TypedFlowIssue> Issues { get; } = issues;
}
public sealed record TypedPort(string Id, string Name, FlowType Type, bool Required, JsonElement Config);
public sealed record TypedNode(FlowNode Source, IReadOnlyList<TypedPort> Inputs, IReadOnlyList<TypedPort> Outputs, IReadOnlyList<string> Exits);
public sealed record TypedFlow(GraphDocument Graph, string EntryId, IReadOnlyDictionary<string, TypedNode> Nodes);

public static class TypedFlowCompiler
{
    internal static bool IsTextOutput(JsonElement config)
    {
        if (!config.TryGetProperty("outputMode", out var mode)) return false;
        if (mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("text" or "structured"))
            throw new InvalidDataException("Formato da saída deve ser text ou structured.");
        return mode.GetString() == "text";
    }

    private static readonly HashSet<string> ExecutableKinds =
        ["dm", "group", "accumulate", "history", "memory", "exit", "property", "prompt", "switch", "branch"];

    public static TypedFlow Compile(JsonElement source, string entryId) => CompileCore(source, entryId, true);

    private static TypedFlow CompileCore(JsonElement source, string entryId, bool validateAlternateEntries)
    {
        var issues = new List<TypedFlowIssue>();
        void Error(string node, string port, string reason) => issues.Add(new(node, port, reason));
        try
        {
            if (source.GetProperty("version").GetInt32() != 3) throw new InvalidDataException("Use graph versão 3.");
            var raw = source.GetProperty("nodes").EnumerateArray().ToArray();
            if (raw.Length is < 1 or > 32) throw new InvalidDataException("Use 1–32 nodes.");
            var allNodes = raw.Select(n => new FlowNode(n.GetProperty("id").GetString()!, n.GetProperty("kind").GetString()!, 1, n)).ToDictionary(n => n.Id);
            var allData = source.GetProperty("edges").EnumerateArray().Select(e => new DataEdge(Ref(e, "source"), [], Ref(e, "target"))).ToArray();
            var allControl = source.TryGetProperty("controlEdges", out var ce) ? ce.EnumerateArray().Select(e => new ControlEdge(Ref(e, "source"), Ref(e, "target"))).ToArray() : [];
            var nodes = allNodes;
            var data = allData;
            var control = allControl;
            if (data.Length > 64 || control.Length > 64) throw new InvalidDataException("Máximo de 64 fios de cada tipo.");
            var graph = new GraphDocument("local", 3, GraphRole.Character, null, nodes.Values, data, control, [entryId]);
            if (!nodes.TryGetValue(entryId, out var entry) || entry.Kind is not ("dm" or "group")) throw new InvalidDataException("Selecione uma entrada DM/grupo.");
            ValidateGlobalStructure(allNodes, allData, allControl, issues);
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
                .Where(node => node.Kind is "dm" or "group" && node.Id != entryId)
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
            graph = new GraphDocument("local", 3, GraphRole.Character, null, nodes.Values, data, control, [entryId]);

            var resolved = new Dictionary<string, TypedNode>();
            var visiting = new HashSet<string>();
            TypedNode Resolve(string id)
            {
                if (resolved.TryGetValue(id, out var cached)) return cached;
                if (!nodes.TryGetValue(id, out var node)) throw new InvalidDataException("Node inexistente: " + id);
                if (!visiting.Add(id)) throw new InvalidDataException("Ciclo de dados.");
                var config = node.Config;
                TypedPort Port(string key, FlowType type, bool required = true) => new(key, key, type, required, default);
                TypedPort Incoming(string key)
                {
                    var links = data.Where(e => e.Target == new PortRef(id, key)).ToArray();
                    if (links.Length != 1) throw new InvalidDataException("Conecte exatamente uma origem em " + key);
                    return Resolve(links[0].Source.NodeId).Outputs.Single(p => p.Id == links[0].Source.Port);
                }
                TypedNode result;
                switch (node.Kind)
                {
                    case "dm":
                    case "group":
                        result = new(node, [], [Port("message", FlowDomain.Message)], ["then"]); break;
                    case "accumulate":
                        ValidateAccumulator(config);
                        result = new(node, [Port("message", FlowDomain.Message)], [Port("messages", new ArrayType(FlowDomain.Message))], ["then"]); break;
                    case "history":
                        result = new(node, [], [Port("messages", new ArrayType(FlowDomain.Message))], ["then"]); break;
                    case "exit":
                        var exitSettings = config.GetProperty("settings");
                        if (exitSettings.GetProperty("mode").GetString() != "switch") throw new InvalidDataException("Trocar Estado executável aceita somente o modo switch.");
                        var stateLinks = data.Where(edge => edge.Target == new PortRef(id, "state")).ToArray();
                        var hasConfiguredTarget = exitSettings.TryGetProperty("targetStateId", out var targetState) && targetState.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(targetState.GetString());
                        if (hasConfiguredTarget)
                        {
                            if (stateLinks.Length != 0 || exitSettings.TryGetProperty("stateMapping", out _)) throw new InvalidDataException("Use targetStateId OU a entrada enum state com stateMapping.");
                            result = new(node, [], [], []); break;
                        }
                        if (stateLinks.Length != 1) throw new InvalidDataException("Conecte um enum em state ou configure targetStateId.");
                        var stateInput = Incoming("state");
                        if (stateInput.Type is not EnumType stateEnum) throw new InvalidDataException("A entrada state deve ser Enum.");
                        if (!exitSettings.TryGetProperty("stateMapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Configure stateMapping para todos os valores do enum.");
                        var mappings = mapping.EnumerateObject().ToArray();
                        if (mappings.Length > 64 || mappings.Select(item => item.Name).ToHashSet(StringComparer.Ordinal).SetEquals(stateEnum.Values) is false ||
                            mappings.Any(item => item.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.Value.GetString())))
                            throw new InvalidDataException("stateMapping deve corresponder exatamente aos valores do enum e apontar para IDs preenchidos.");
                        result = new(node, [Port("state", stateInput.Type)], [], []); break;
                    case "memory":
                        var memory = MemoryNodeSettings.Read(config);
                        var personRequired = memory.Scope == Memory.MemoryScope.Person;
                        var personType = data.Any(e => e.Target == new PortRef(id, "persons")) ? Incoming("persons").Type : FlowDomain.Person;
                        if (!MemoryNodeSettings.IsPersonInput(personType)) throw new InvalidDataException("Memória recebe Pessoa, Pessoa? ou lista dessas pessoas.");
                        var queryConfig = memory.QueryText is null ? default : JsonSerializer.SerializeToElement(new { value = memory.QueryText });
                        if (memory.QueryText is not null && string.IsNullOrWhiteSpace(memory.QueryText)) throw new InvalidDataException("Consulta configurada vazia.");
                        result = new(node, [Port("persons", personType, personRequired),
                            new("query", "query", new StringType(), memory.Scope == Memory.MemoryScope.Conversation, queryConfig)],
                            [Port("memories", new ArrayType(FlowDomain.Memory))], ["then"]); break;
                    case "property":
                        var input = Incoming("object");
                        var path = PropertyPath(config);
                        if (path.Length == 0) throw new InvalidDataException("Escolha uma propriedade.");
                        result = new(node, [Port("object", input.Type)], [Port("value", FlowDomain.PropertyType(input.Type, path))], []); break;
                    case "prompt":
                        if (!config.TryGetProperty("prompt", out var prompt) || prompt.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(prompt.GetString())) throw new InvalidDataException("Preencha a instrução.");
                        InstructionContext.Enabled(config, "usePersonalityCore");
                        InstructionContext.Enabled(config, "useStateGuidance");
                        InstructionContext.Enabled(config, "useInterestProfile");
                        var inputs = Fields(id, config, "inputs", false);
                        var outputs = Fields(id, config, "responseFields", true);
                        if (outputs.Length == 0) outputs = [Port("message", new StringType())];
                        if (IsTextOutput(config) && (outputs.Length != 1 || outputs[0].Type is not StringType))
                            throw new InvalidDataException("Saída Texto exige exatamente um campo Texto.");
                        result = new(node, inputs, outputs, ["then"]); break;
                    case "branch":
                        var left = Incoming("a");
                        var branchSettings = config.GetProperty("settings");
                        var hasLiteral = branchSettings.TryGetProperty("b", out var literal);
                        var right = hasLiteral
                            ? new TypedPort("b", "B", left.Type, true, JsonSerializer.SerializeToElement(new { value = literal }))
                            : Port("b", Incoming("b").Type);
                        TypedBranch.Validate(left.Type, right.Type, branchSettings.GetProperty("operator").GetString());
                        if (hasLiteral) FlowDomain.Check(literal, left.Type);
                        result = new(node, [Port("a", left.Type), right], [], ["true", "false"]); break;
                    case "switch":
                        var value = Incoming("value");
                        if (value.Type is not (StringType or EnumType)) throw new InvalidDataException("Switch recebe Texto ou Enum.");
                        var cases = config.GetProperty("settings").GetProperty("cases").EnumerateArray().ToArray();
                        var names = cases.Select(c => c.GetProperty("value").GetString()!).ToArray();
                        var ids = cases.Select(c => c.GetProperty("id").GetString()!).ToArray();
                        if (cases.Length > 64 || names.Distinct().Count() != names.Length || ids.Distinct().Count() != ids.Length || ids.Any(string.IsNullOrWhiteSpace) || ids.Contains("other")) throw new InvalidDataException("Casos inválidos ou duplicados.");
                        if (value.Type is EnumType en && !en.Values.ToHashSet().SetEquals(names)) throw new InvalidDataException("Atualize os casos para corresponder ao enum.");
                        result = new(node, [Port("value", value.Type)], [], HasOther(config) ? [.. ids, "other"] : ids); break;
                    default: throw new InvalidDataException("Node fora deste recorte executável: " + node.Kind);
                }
                visiting.Remove(id); resolved[id] = result; return result;
            }
            foreach (var id in nodes.Keys)
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
                if (!resolved.TryGetValue(edge.Target.NodeId, out var to) || to.Source.Kind is "property" or "dm" or "group" || edge.Target.Port != "exec") Error(edge.Target.NodeId, edge.Target.Port, "Entrada de execução inexistente.");
            }
            foreach (var group in control.GroupBy(e => e.Source)) if (group.Count() > 1) Error(group.Key.NodeId, group.Key.Port, "Sem fan-out: conecte apenas um próximo node.");
            DetectCycles(nodes.Keys, data.Select(e => (e.Source.NodeId, e.Target.NodeId)), issues);
            DetectCycles(nodes.Keys, control.Select(e => (e.Source.NodeId, e.Target.NodeId)), issues);
            if (issues.Count == 0) ValidateAvailability(graph, resolved, entryId, issues);
            if (issues.Count == 0 && validateAlternateEntries)
                foreach (var alternate in allNodes.Values.Where(node => node.Kind is "dm" or "group" && node.Id != entryId))
                    try { _ = CompileCore(source, alternate.Id, false); }
                    catch (TypedFlowException exception) { issues.AddRange(exception.Issues); }
            if (issues.Count > 0) throw new TypedFlowException(issues);
            return new(graph, entryId, resolved);
        }
        catch (Exception e) when (ConfigurationError(e)) { throw new TypedFlowException([new("graph", "config", e.Message)]); }
    }

    private static bool ConfigurationError(Exception e) => e is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or FlowSchemaException;

    private static void ValidateGlobalStructure(
        IReadOnlyDictionary<string, FlowNode> nodes,
        IReadOnlyList<DataEdge> data,
        IReadOnlyList<ControlEdge> control,
        List<TypedFlowIssue> issues)
    {
        foreach (var node in nodes.Values)
            if (!ExecutableKinds.Contains(node.Kind)) issues.Add(new(node.Id, "kind", "Node fora deste recorte executável: " + node.Kind));
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

        var reachable = new HashSet<string>(nodes.Values.Where(node => node.Kind is "dm" or "group").Select(node => node.Id), StringComparer.Ordinal);
        var pending = new Queue<string>(reachable);
        while (pending.TryDequeue(out var current))
            foreach (var edge in control.Where(item => item.Source.NodeId == current))
                if (reachable.Add(edge.Target.NodeId)) pending.Enqueue(edge.Target.NodeId);
        foreach (var node in nodes.Values.Where(node => node.Kind is not ("property" or "dm" or "group")))
            if (!reachable.Contains(node.Id)) issues.Add(new(node.Id, "exec", "Node operacional não alcançado por nenhuma entrada."));
    }
    private static PortRef Ref(JsonElement e, string end) => new(e.GetProperty(end).GetString()!, e.GetProperty(end + "Port").GetString()!);
    public static string[] PropertyPath(JsonElement config) => config.GetProperty("settings").GetProperty("path").EnumerateArray().Select(p => p.GetString()!).ToArray();
    public static bool HasOther(JsonElement config) => config.GetProperty("settings").TryGetProperty("other", out var other) && other.ValueKind == JsonValueKind.True;
    private static void ValidateAccumulator(JsonElement config)
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
    private static TypedPort[] Fields(string nodeId, JsonElement config, string key, bool output)
    {
        if (!config.TryGetProperty(key, out var fields)) return [];
        if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() > 32) throw new InvalidDataException("Lista de campos inválida.");
        var ids = new HashSet<string>(); var names = new HashSet<string>();
        return fields.EnumerateArray().Select(field =>
        {
            try
            {
                var id = field.GetProperty("id").GetString()!; var name = field.GetProperty("name").GetString()!;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !ids.Add(id) || !names.Add(name)) throw new InvalidDataException("IDs e nomes de campos devem ser únicos e preenchidos.");
                var type = FlowDomain.Parse(field);
                if (output && type is not (StringType or NumberType or BooleanType or EnumType)) throw new InvalidDataException("Resposta JSON deve ser flat/escalar.");
                if (type is EnumType en && en.Values.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Enum vazio.");
                foreach (var bound in new[] { "min", "max" }) if (field.TryGetProperty(bound, out var v) && (type is not NumberType || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d))) throw new InvalidDataException("Limite numérico inválido.");
                if (field.TryGetProperty("min", out var min) && field.TryGetProperty("max", out var max) && min.GetDouble() > max.GetDouble()) throw new InvalidDataException("Mínimo maior que máximo.");
                if (field.TryGetProperty("maxWords", out var words) && (type is not StringType || !words.TryGetInt32(out var w) || w < 1)) throw new InvalidDataException("maxWords inválido.");
                if (!output && field.TryGetProperty("value", out var value))
                {
                    if (type is not (StringType or NumberType or BooleanType or EnumType)) throw new InvalidDataException("Valor local somente escalar.");
                    FlowDomain.Check(value, type);
                }
                return new TypedPort(id, name, type, output || !field.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.False, field.Clone());
            }
            catch (Exception e) when (ConfigurationError(e))
            {
                var port = field.ValueKind == JsonValueKind.Object && field.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String ? idValue.GetString()! : key;
                throw new TypedFlowException([new(nodeId, port, e.Message)]);
            }
        }).ToArray();
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

    private static void ValidateAvailability(GraphDocument graph, Dictionary<string, TypedNode> nodes, string entry, List<TypedFlowIssue> issues)
    {
        var paths = new Dictionary<string, HashSet<string>>(); var visits = 0;
        void Walk(string id, HashSet<string> prior)
        {
            if (++visits > 4096) throw new InvalidDataException("Graph excede limite de caminhos.");
            if (paths.TryGetValue(id, out var known)) known.IntersectWith(prior); else paths[id] = new(prior);
            var next = new HashSet<string>(prior) { id };
            foreach (var e in graph.ControlEdges.Where(e => e.Source.NodeId == id)) Walk(e.Target.NodeId, next);
        }
        Walk(entry, []);
        IEnumerable<string> Producers(string id)
        {
            if (nodes[id].Source.Kind != "property") return [id];
            return graph.DataEdges.Where(e => e.Target.NodeId == id).SelectMany(e => Producers(e.Source.NodeId));
        }
        foreach (var consumer in paths.Keys)
            foreach (var edge in graph.DataEdges.Where(e => e.Target.NodeId == consumer))
                foreach (var producer in Producers(edge.Source.NodeId))
                    if (!paths[consumer].Contains(producer)) issues.Add(new(consumer, edge.Target.Port, "Produtor não executa em todos os caminhos anteriores: " + producer));
        foreach (var node in nodes.Values.Where(n => n.Source.Kind is not ("property" or "dm" or "group")))
            if (!paths.ContainsKey(node.Source.Id)) issues.Add(new(node.Source.Id, "exec", "Node operacional não alcançado pela entrada selecionada."));
    }
}
