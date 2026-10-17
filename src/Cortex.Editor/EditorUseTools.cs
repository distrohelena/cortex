using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;

namespace Cortex.Editor;

public sealed record UseToolDefinition(string EntryId, string AfterNode, string[] Parameters, string? ArtifactPort = null, string? ArtifactDirectorySetting = null);
public sealed record EditorArtifact(string Path, string ContentType);

public sealed class EditorUseTools : IDisposable
{
    private readonly JsonElement _template;
    private readonly IReadOnlyList<NodeDefinition> _definitions;
    private readonly Func<IReadOnlyList<FlowNodeHandler>> _bindHandlers;
    private readonly ConcurrentDictionary<string, EditorExecutionService> _services = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, UseToolDefinition> Definitions { get; }
    public bool Busy => _services.Values.Any(service => service.Busy);
    public IReadOnlyDictionary<string, EditorRunSnapshot> Snapshots => _services.ToDictionary(pair => pair.Key, pair => pair.Value.Snapshot());

    public EditorUseTools(JsonElement template, IReadOnlyList<NodeDefinition> definitions, Func<IReadOnlyList<FlowNodeHandler>> bindHandlers)
    {
        _template = template.Clone(); _definitions = definitions; _bindHandlers = bindHandlers;
        var definitionsMap = new Dictionary<string, UseToolDefinition>(StringComparer.Ordinal);
        if (template.TryGetProperty("usage", out var usage) && usage.TryGetProperty("tools", out var tools))
            foreach (var item in tools.EnumerateObject())
            {
                var spec = item.Value.Deserialize<UseToolDefinition>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Invalid use tool.");
                var node = Node(spec.EntryId);
                var definition = _definitions.SingleOrDefault(definition => definition.Kind == node.GetProperty("kind").GetString());
                if (definition?.ExecutionMode != FlowNodeExecutionMode.Entry || string.IsNullOrWhiteSpace(spec.AfterNode) || spec.Parameters is null || spec.Parameters.Distinct().Count() != spec.Parameters.Length || spec.Parameters.Any(name => !definition.ConfigFields.Any(field => field.Name == name)))
                    throw new InvalidDataException("A use tool requires an available entry and declared parameters.");
                _ = Node(spec.AfterNode);
                if ((spec.ArtifactPort is null) != (spec.ArtifactDirectorySetting is null)) throw new InvalidDataException("Provide an artifact port and locked directory setting together.");
                if (spec.ArtifactPort is not null)
                {
                    var resolution = definition.Resolve(Settings(node));
                    var port = resolution.DataOutputs.SingleOrDefault(port => port.Name == spec.ArtifactPort);
                    if (definition.Effect != FlowEffect.WriteState || port?.Type is not ObjectType output || !output.Fields.TryGetValue("path", out var path) || path.Type is not StringType ||
                        spec.Parameters.Contains(spec.ArtifactDirectorySetting!) || !node.GetProperty("settings").TryGetProperty(spec.ArtifactDirectorySetting!, out var directory) || directory.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(directory.GetString()))
                        throw new InvalidDataException("Tool artifacts require a generated path and a locked output directory.");
                }
                definitionsMap.Add(item.Name, spec); _services[item.Name] = new(_definitions, _bindHandlers);
            }
        Definitions = definitionsMap;
    }

    public EditorUseTools(JsonElement template, IReadOnlyList<FlowNodeHandler> handlers)
        : this(template, handlers.Select(handler => handler.Definition).ToArray(), () => handlers) { }

    private static JsonElement Settings(JsonElement node) => node.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object
        ? settings : JsonSerializer.SerializeToElement(new { });

    private JsonElement Node(string id) => _template.GetProperty("nodes").EnumerateArray().SingleOrDefault(node => node.GetProperty("id").GetString() == id) is var result && result.ValueKind == JsonValueKind.Object
        ? result : throw new InvalidDataException("Unknown use-tool node.");
    public EditorRunSnapshot Snapshot(string id) => Service(id).Snapshot();
    private EditorExecutionService Service(string id) => _services.TryGetValue(id, out var service) ? service : throw new InvalidDataException("Unknown use tool.");

    public async Task StartAsync(string id, string completedNode, Dictionary<string, JsonElement> parameters, CancellationToken token)
    {
        var service = Service(id); var spec = Definitions[id];
        if (completedNode != spec.AfterNode) throw new InvalidOperationException("This tool is unavailable at this review stage.");
        var graph = JsonNode.Parse(_template.GetRawText())!;
        var node = graph["nodes"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == spec.EntryId)!;
        var definition = _definitions.Single(definition => definition.Kind == node["kind"]!.GetValue<string>());
        node["settings"] ??= new JsonObject();
        foreach (var (name, value) in parameters)
        {
            if (!spec.Parameters.Contains(name)) throw new InvalidDataException("This tool parameter is locked.");
            var field = definition.ConfigFields.Single(field => field.Name == name);
            FlowDomain.Check(value, field.Type);
            node["settings"]![name] = JsonNode.Parse(value.GetRawText());
        }
        await service.StartAsync(JsonSerializer.SerializeToElement(graph), spec.EntryId, token);
    }

    public Task WaitAsync(string id, CancellationToken token) => Service(id).WaitAsync(token);
    public EditorArtifact? Artifact(string id)
    {
        var snapshot = Service(id).Snapshot(); var spec = Definitions[id];
        if (snapshot.State != "completed" || spec.ArtifactPort is null || snapshot.Result is null) return null;
        var output = snapshot.Result.Value.GetProperty("outputs").GetProperty(spec.EntryId).GetProperty(spec.ArtifactPort).GetProperty("value");
        var root = Path.GetFullPath(Node(spec.EntryId).GetProperty("settings").GetProperty(spec.ArtifactDirectorySetting!).GetString()!);
        var path = Path.GetFullPath(output.GetProperty("path").GetString()!);
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative) || !File.Exists(path) || !Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) return null;
        if (new FileInfo(path).Length > 268435456) return null;
        for (var parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
        {
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) return null;
            if (string.Equals(parent, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return new(path, "video/mp4");
        }
        return null;
    }
    public void Cancel() { foreach (var service in _services.Values) service.Cancel(); }
    public void Reset() { foreach (var id in Definitions.Keys) { var old = _services[id]; _services[id] = new(_definitions, _bindHandlers); old.Dispose(); } }
    public void Dispose() { foreach (var service in _services.Values) service.Dispose(); }
}
