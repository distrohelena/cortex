using System.Text.Json;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public sealed record EditorGraphSnapshot(JsonElement Graph, string? EntryId, string? FilePath);

public sealed class EditorGraphStore
{
    public JsonElement Graph { get; private set; } = JsonSerializer.SerializeToElement(new { version = 4, nodes = Array.Empty<object>(), edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
    public string? EntryId { get; private set; }
    public string? FilePath { get; private set; }

    public EditorGraphSnapshot Capture() => new(Graph.Clone(), EntryId, FilePath);
    public void Restore(EditorGraphSnapshot snapshot)
    {
        Replace(snapshot.Graph, snapshot.EntryId);
        FilePath = snapshot.FilePath;
    }

    public async Task OpenFileAsync(string path, string? entryId, CancellationToken token)
    {
        var full = Path.GetFullPath(path);
        if (new FileInfo(full).Length > 4194304) throw new InvalidDataException("The graph exceeds 4 MiB.");
        using var source = JsonDocument.Parse(await File.ReadAllTextAsync(full, token));
        Replace(source.RootElement, entryId);
        FilePath = full;
    }
    public void Open(JsonElement graph, string? entryId) { Replace(graph, entryId); FilePath = null; }
    public void Replace(JsonElement graph, string? entryId)
    {
        if (graph.GetRawText().Length > 4194304 || graph.ValueKind != JsonValueKind.Object ||
            !graph.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 4 ||
            !graph.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() > 64 ||
            !graph.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array || edges.GetArrayLength() > 128 ||
            graph.TryGetProperty("controlEdges", out var controls) && (controls.ValueKind != JsonValueKind.Array || controls.GetArrayLength() > 128))
            throw new InvalidDataException("Use a v4 graph with at most 64 nodes and 128 wires of each type.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes.EnumerateArray())
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()) || !ids.Add(id.GetString()!) ||
                !node.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(kind.GetString()))
                throw new InvalidDataException("Nodes require unique IDs and nonempty kinds.");
        foreach (var edge in edges.EnumerateArray().Concat(graph.TryGetProperty("controlEdges", out var wires) ? wires.EnumerateArray() : []))
            foreach (var field in new[] { "source", "target", "sourcePort", "targetPort" })
                if (edge.ValueKind != JsonValueKind.Object || !edge.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new InvalidDataException("Wires require nonempty node and port IDs.");
        Graph = graph.Clone();
        EntryId = entryId;
    }
    public async Task SaveAsync(CancellationToken token)
    {
        if (FilePath is null) throw new InvalidOperationException("Save this graph by downloading it.");
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(Graph, CortexSettingsStore.JsonOptions), token);
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
