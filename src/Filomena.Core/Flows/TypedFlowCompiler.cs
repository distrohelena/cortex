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
    public static TypedFlow Compile(JsonElement source, string entryId)
    {
        var issues = new List<TypedFlowIssue>();
        void Error(string node, string port, string reason) => issues.Add(new(node, port, reason));
        try
        {
            if (source.GetProperty("version").GetInt32() != 3) throw new InvalidDataException("Use graph versão 3.");
            var raw = source.GetProperty("nodes").EnumerateArray().ToArray();
            if (raw.Length is < 1 or > 20) throw new InvalidDataException("Use 1–20 nodes.");
            var nodes = raw.Select(n => new FlowNode(n.GetProperty("id").GetString()!, n.GetProperty("kind").GetString()!, 1, n)).ToDictionary(n => n.Id);
            var data = source.GetProperty("edges").EnumerateArray().Select(e => new DataEdge(Ref(e, "source"), [], Ref(e, "target"))).ToArray();
            var control = source.TryGetProperty("controlEdges", out var ce) ? ce.EnumerateArray().Select(e => new ControlEdge(Ref(e, "source"), Ref(e, "target"))).ToArray() : [];
            if (data.Length > 64 || control.Length > 64) throw new InvalidDataException("Máximo de 64 fios de cada tipo.");
            var graph = new GraphDocument("local", 3, GraphRole.Character, null, nodes.Values, data, control, [entryId]);
            if (!nodes.TryGetValue(entryId, out var entry) || entry.Kind is not ("dm" or "group")) throw new InvalidDataException("Selecione uma entrada DM/grupo.");
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
                    case "property":
                        var input = Incoming("object");
                        var path = PropertyPath(config);
                        if (path.Length == 0) throw new InvalidDataException("Escolha uma propriedade.");
                        result = new(node, [Port("object", input.Type)], [Port("value", FlowDomain.PropertyType(input.Type, path))], []); break;
                    case "prompt":
                        if (!config.TryGetProperty("prompt", out var prompt) || prompt.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(prompt.GetString())) throw new InvalidDataException("Preencha a instrução.");
                        var inputs = Fields(id, config, "inputs", false);
                        var outputs = Fields(id, config, "responseFields", true);
                        if (outputs.Length == 0) outputs = [Port("message", new StringType())];
                        result = new(node, inputs, outputs, ["then"]); break;
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
            if (issues.Count > 0) throw new TypedFlowException(issues);
            return new(graph, entryId, resolved);
        }
        catch (Exception e) when (ConfigurationError(e)) { throw new TypedFlowException([new("graph", "config", e.Message)]); }
    }

    private static bool ConfigurationError(Exception e) => e is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or FlowSchemaException;
    private static PortRef Ref(JsonElement e, string end) => new(e.GetProperty(end).GetString()!, e.GetProperty(end + "Port").GetString()!);
    public static string[] PropertyPath(JsonElement config) => config.GetProperty("settings").GetProperty("path").EnumerateArray().Select(p => p.GetString()!).ToArray();
    public static bool HasOther(JsonElement config) => config.GetProperty("settings").TryGetProperty("other", out var other) && other.ValueKind == JsonValueKind.True;
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
