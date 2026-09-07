using System.Text.Json;

namespace Filomena.App.Playground;

internal sealed class PlaygroundProjectStore(string directory)
{
    private readonly string path = Path.Combine(directory, "project.json");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<JsonElement?> ReadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
            Validate(document.RootElement);
            return document.RootElement.Clone();
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(JsonElement document, CancellationToken token)
    {
        Validate(document);
        await gate.WaitAsync(token);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(temporary, document.GetRawText(), token);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { gate.Release(); }
        }
    }

    private static void Keys(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name)) throw new InvalidDataException();
    }

    private static string Text(JsonElement value, string key, int max = 80)
    {
        if (!value.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.String ||
            item.GetString() is not { } text || string.IsNullOrWhiteSpace(text) || text.Length > max)
            throw new InvalidDataException();
        return text;
    }

    private static JsonElement Array(JsonElement value, string key, int max)
    {
        if (!value.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > max)
            throw new InvalidDataException();
        return array;
    }

    private static void DraftStateId(JsonElement project)
    {
        if (!project.TryGetProperty("initialStateId", out var value) || value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 80) throw new InvalidDataException();
    }

    // This validates a design document, not an executable workflow. No compiler or model is invoked.
    private static void Validate(JsonElement document)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(document.GetRawText()) > 1048576) throw new InvalidDataException();
        Keys(document, "format", "project");
        if (Text(document, "format") != "filomena-local-project" || !document.TryGetProperty("project", out var project)) throw new InvalidDataException();
        Keys(project, "version", "stateScope", "initialStateId", "states");
        if (!project.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 || Text(project, "stateScope") != "session") throw new InvalidDataException();
        DraftStateId(project);
        var states = Array(project, "states", 16);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in states.EnumerateArray())
        {
            Keys(state, "id", "name", "graph");
            if (!ids.Add(Text(state, "id"))) throw new InvalidDataException();
            Text(state, "name");
            if (!state.TryGetProperty("graph", out var graph)) throw new InvalidDataException();
            Keys(graph, "version", "nodes", "edges", "controlEdges");
            if (!graph.TryGetProperty("version", out var gv) || gv.ValueKind != JsonValueKind.Number || !gv.TryGetInt32(out var gn) || gn is not (2 or 3)) throw new InvalidDataException();
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in Array(graph, "nodes", 20).EnumerateArray())
            {
                Keys(node, "id", "kind", "x", "y", "prompt", "provider", "model", "settings", "responseFields", "inputs", "correctionRetry", "title");
                if (!nodeIds.Add(Text(node, "id"))) throw new InvalidDataException();
                if (!new[] { "dm", "group", "input", "prompt", "accumulate", "history", "memory", "branch", "exit", "send", "property", "switch" }.Contains(Text(node, "kind"))) throw new InvalidDataException();
                foreach (var coordinate in new[] { "x", "y" })
                    if (!node.TryGetProperty(coordinate, out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetDouble(out var n) || !double.IsFinite(n) || n < 0 || n > 4000) throw new InvalidDataException();
                if (node.TryGetProperty("settings", out var settings))
                {
                    Keys(settings, "quietSeconds", "maxMessages", "maxWaitSeconds", "maxAgeMinutes", "maxCharacters", "includeCharacterMessages", "operator", "targetStateId", "mode", "stateMapping", "path", "cases", "retiredCases", "other", "scope", "categories", "maxItems", "activeOnly", "queryText");
                    if (settings.TryGetProperty("cases", out _))
                        foreach (var item in Array(settings, "cases", 64).EnumerateArray()) Keys(item, "id", "value");
                }
                foreach (var fieldKey in new[] { "inputs", "responseFields" })
                    if (node.TryGetProperty(fieldKey, out _))
                        foreach (var field in Array(node, fieldKey, 32).EnumerateArray())
                            Keys(field, "id", "name", "description", "type", "values", "required", "value", "min", "max", "maxWords");
            }
            foreach (var key in new[] { "edges", "controlEdges" })
            {
                if (key == "controlEdges" && !graph.TryGetProperty(key, out _)) continue;
                foreach (var edge in Array(graph, key, 64).EnumerateArray())
                {
                    Keys(edge, "source", "target", "sourcePort", "targetPort");
                    if (!nodeIds.Contains(Text(edge, "source")) || !nodeIds.Contains(Text(edge, "target"))) throw new InvalidDataException();
                }
            }
        }
    }
}
