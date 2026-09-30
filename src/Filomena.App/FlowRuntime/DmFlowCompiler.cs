using System.Text.Json;

namespace Filomena.App.FlowRuntime;

public sealed class CompiledDmFlow
{
    private readonly CompiledPrompt[] prompts;

    internal CompiledDmFlow(IEnumerable<CompiledPrompt> prompts) => this.prompts = prompts.ToArray();

    internal IReadOnlyList<CompiledPrompt> Prompts => prompts;
}

internal sealed record CompiledPrompt(string Id, string Instruction, string? Provider, string? Model);

public static class DmFlowCompiler
{
    private const int MaxTextCharacters = 16_000;
    private const int MaxProjectCharacters = 1_048_576;

    public static CompiledDmFlow Compile(JsonElement projectDocument)
    {
        if (projectDocument.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
            projectDocument.GetRawText().Length > MaxProjectCharacters)
        {
            throw Invalid("The project document is missing or exceeds 1 MB.");
        }

        RejectDuplicateProperties(projectDocument);
        var format = RequiredString(projectDocument, "format");
        if (format != "filomena-local-project")
        {
            throw Invalid("Only filomena-local-project documents are executable.");
        }

        var project = RequiredObject(projectDocument, "project");
        if (RequiredInt(project, "version") != 1 || RequiredString(project, "stateScope") != "session")
        {
            throw Invalid("Only version 1 session projects are executable.");
        }

        var initialStateId = RequiredString(project, "initialStateId");
        ValidateId(initialStateId, "initialStateId");
        var states = RequiredArray(project, "states");
        if (states.GetArrayLength() != 1)
        {
            throw Invalid("An executable DM flow must contain exactly one state.");
        }

        var state = states[0];
        var stateId = RequiredString(state, "id");
        ValidateId(stateId, "state id");
        if (stateId != initialStateId)
        {
            throw Invalid("The initial state must be the only state.");
        }

        var graph = RequiredObject(state, "graph");
        if (RequiredInt(graph, "version") != 1)
        {
            throw Invalid("Only version 1 graphs are executable.");
        }

        var nodes = ReadNodes(RequiredArray(graph, "nodes"));
        var edges = ReadEdges(RequiredArray(graph, "edges"), nodes);
        ValidateGraph(nodes, edges);

        var order = TopologicalOrder(nodes, edges);
        var prompts = order
            .Select(id => nodes[id])
            .Where(node => node.Kind == "prompt")
            .Select(node => new CompiledPrompt(node.Id, node.Instruction!, node.Provider, node.Model))
            .ToArray();

        return new CompiledDmFlow(prompts);
    }

    private static Dictionary<string, ProjectNode> ReadNodes(JsonElement value)
    {
        if (value.GetArrayLength() > 20)
        {
            throw Invalid("A graph may contain at most 20 nodes.");
        }

        var nodes = new Dictionary<string, ProjectNode>(StringComparer.Ordinal);
        foreach (var node in value.EnumerateArray())
        {
            var id = RequiredString(node, "id");
            ValidateId(id, "node id");
            var kind = RequiredString(node, "kind");
            if (kind is not ("dm" or "prompt" or "send"))
            {
                throw Invalid($"Node '{id}' has an unsupported kind.");
            }

            string? instruction = null;
            string? provider = null;
            string? model = null;
            if (kind == "prompt")
            {
                instruction = RequiredString(node, "prompt");
                if (instruction.Length > MaxTextCharacters)
                {
                    throw Invalid($"Prompt node '{id}' exceeds 16,000 characters.");
                }

                provider = OptionalString(node, "provider");
                model = OptionalString(node, "model");
                if (provider?.Length > 120 || model?.Length > 256)
                {
                    throw Invalid($"Prompt node '{id}' has an oversized provider or model.");
                }

                if (!string.IsNullOrWhiteSpace(provider) &&
                    !string.Equals(provider.Trim(), "lmstudio", StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid($"Prompt node '{id}' uses an unsupported provider.");
                }
            }

            if (!nodes.TryAdd(id, new ProjectNode(id, kind, instruction, provider?.Trim(), model?.Trim())))
            {
                throw Invalid($"Node id '{id}' is duplicated.");
            }
        }

        var promptCount = nodes.Values.Count(node => node.Kind == "prompt");
        if (nodes.Values.Count(node => node.Kind == "dm") != 1 ||
            nodes.Values.Count(node => node.Kind == "send") != 1 ||
            promptCount is < 1 or > 8)
        {
            throw Invalid("An executable graph needs one dm, 1-8 prompts, and one send node.");
        }

        return nodes;
    }

    private static List<CompiledEdge> ReadEdges(JsonElement value, IReadOnlyDictionary<string, ProjectNode> nodes)
    {
        if (value.GetArrayLength() > 64)
        {
            throw Invalid("A graph may contain at most 64 wires.");
        }

        var edges = new List<CompiledEdge>();
        foreach (var edge in value.EnumerateArray())
        {
            var sourceId = RequiredString(edge, "source");
            var targetId = RequiredString(edge, "target");
            if (!nodes.TryGetValue(sourceId, out var source) || !nodes.TryGetValue(targetId, out var target))
            {
                throw Invalid("A wire references a node that does not exist.");
            }

            var sourcePort = OptionalPort(edge, "sourcePort");
            var targetPort = OptionalPort(edge, "targetPort");
            var candidates = CompatiblePorts(source, target);
            var matches = candidates
                .Where(candidate => (sourcePort is null || candidate.Source == sourcePort) &&
                                    (targetPort is null || candidate.Target == targetPort))
                .ToArray();
            if (matches.Length != 1)
            {
                throw Invalid($"Wire '{sourceId}' to '{targetId}' has missing or incompatible ports.");
            }

            var resolved = matches[0];
            edges.Add(new CompiledEdge(sourceId, targetId, resolved.Source, resolved.Target));
        }

        return edges;
    }

    private static void ValidateGraph(IReadOnlyDictionary<string, ProjectNode> nodes, IReadOnlyList<CompiledEdge> edges)
    {
        if (edges.Count != nodes.Count)
        {
            throw Invalid("Every supported DM graph must be a single connected path with a recipient wire.");
        }

        var incoming = new HashSet<string>(StringComparer.Ordinal);
        var outgoing = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (edge.SourceId == edge.TargetId || edge.SourceKind(nodes) == "send" || edge.TargetKind(nodes) == "dm")
            {
                throw Invalid("A supported DM graph cannot contain self, terminal-source, or input-target wires.");
            }

            var targetKey = $"{edge.TargetId}:{edge.TargetPort}";
            if (!incoming.Add(targetKey))
            {
                throw Invalid("Each input port accepts exactly one wire.");
            }

            outgoing[edge.SourceId] = outgoing.GetValueOrDefault(edge.SourceId) + 1;
        }

        var dm = nodes.Values.Single(node => node.Kind == "dm");
        var send = nodes.Values.Single(node => node.Kind == "send");
        RequireWire(edges, dm.Id, "message", null, "message", "message");
        RequireWire(edges, dm.Id, "from", send.Id, "to", "recipient");
        RequireIncoming(edges, send.Id, "message");
        foreach (var prompt in nodes.Values.Where(node => node.Kind == "prompt"))
        {
            RequireIncoming(edges, prompt.Id, "message");
        }

        if (outgoing.GetValueOrDefault(dm.Id) != 2 ||
            nodes.Values.Where(node => node.Kind == "prompt").Any(prompt => outgoing.GetValueOrDefault(prompt.Id) != 1))
        {
            throw Invalid("Each prompt must have one message successor, and DM must provide message and recipient.");
        }

        var reachable = ReachableFrom(dm.Id, edges);
        var canReachSend = ReachableFrom(send.Id, edges, reverse: true);
        if (reachable.Count != nodes.Count || canReachSend.Count != nodes.Count)
        {
            throw Invalid("The graph must be connected from DM to send and contain no loose nodes.");
        }
    }

    private static void RequireWire(
        IReadOnlyList<CompiledEdge> edges,
        string sourceId,
        string sourcePort,
        string? targetId,
        string targetPort,
        string description)
    {
        if (!edges.Any(edge => edge.SourceId == sourceId && edge.SourcePort == sourcePort &&
                              (targetId is null || edge.TargetId == targetId) && edge.TargetPort == targetPort))
        {
            throw Invalid($"The required {description} wire is missing.");
        }
    }

    private static void RequireIncoming(IReadOnlyList<CompiledEdge> edges, string targetId, string targetPort)
    {
        if (!edges.Any(edge => edge.TargetId == targetId && edge.TargetPort == targetPort))
        {
            throw Invalid($"The required '{targetPort}' input on '{targetId}' is missing.");
        }
    }

    private static IReadOnlyList<string> TopologicalOrder(
        IReadOnlyDictionary<string, ProjectNode> nodes,
        IReadOnlyList<CompiledEdge> edges)
    {
        var indegree = nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in edges) indegree[edge.TargetId]++;

        var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var order = new List<string>(nodes.Count);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            order.Add(current);
            foreach (var edge in edges.Where(edge => edge.SourceId == current))
            {
                if (--indegree[edge.TargetId] == 0) queue.Enqueue(edge.TargetId);
            }
        }

        if (order.Count != nodes.Count)
        {
            throw Invalid("The graph contains a cycle.");
        }

        return order;
    }

    private static HashSet<string> ReachableFrom(string start, IReadOnlyList<CompiledEdge> edges, bool reverse = false)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { start };
        var pending = new Queue<string>();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var edge in edges.Where(edge => reverse ? edge.TargetId == current : edge.SourceId == current))
            {
                var next = reverse ? edge.SourceId : edge.TargetId;
                if (visited.Add(next)) pending.Enqueue(next);
            }
        }

        return visited;
    }

    private static PortCandidate[] CompatiblePorts(ProjectNode source, ProjectNode target) =>
        (source.Kind, target.Kind) switch
        {
            ("dm", "prompt") => [new("message", "message")],
            ("prompt", "prompt") => [new("message", "message")],
            ("prompt", "send") => [new("message", "message")],
            ("dm", "send") => [new("message", "message"), new("from", "to")],
            _ => [],
        };

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"Property '{name}' must be an object.");
        }

        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"Property '{name}' must be an array.");
        }

        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid($"Property '{name}' must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) throw Invalid($"Property '{name}' requires an object.");
        if (!parent.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw Invalid($"Property '{name}' must be a string.");
        return value.GetString();
    }

    private static string? OptionalPort(JsonElement parent, string name) => OptionalString(parent, name);

    private static int RequiredInt(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            throw Invalid($"Property '{name}' must be an integer.");
        }

        return result;
    }

    private static void ValidateId(string value, string description)
    {
        if (value.Length > 80) throw Invalid($"The {description} is too long.");
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid($"Property '{property.Name}' is duplicated.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private sealed record ProjectNode(string Id, string Kind, string? Instruction, string? Provider, string? Model);
    private sealed record PortCandidate(string Source, string Target);
    private sealed record CompiledEdge(string SourceId, string TargetId, string SourcePort, string TargetPort)
    {
        public string SourceKind(IReadOnlyDictionary<string, ProjectNode> nodes) => nodes[SourceId].Kind;
        public string TargetKind(IReadOnlyDictionary<string, ProjectNode> nodes) => nodes[TargetId].Kind;
    }
}
