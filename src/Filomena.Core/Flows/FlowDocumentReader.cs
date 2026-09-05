using System.Text;
using System.Text.Json;

namespace Filomena.Core.Flows;

public sealed record FlowReadResult(GraphDocument? Document, FlowIssues Issues);

public static class FlowDocumentReader
{
    private const int SupportedFormatVersion = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static FlowReadResult Read(string json)
    {
        var issues = new FlowIssues();
        byte[] utf8;
        try
        {
            utf8 = StrictUtf8.GetBytes(json);
        }
        catch (EncoderFallbackException)
        {
            issues.Add("document.input_invalid_utf8", "/", "Input is not valid UTF-8 text.");
            return new FlowReadResult(null, issues);
        }

        if (utf8.Length > FlowLimits.MaxInputUtf8Bytes)
        {
            issues.Add("document.input_too_large", "/", "Input exceeds the maximum UTF-8 size.");
            return new FlowReadResult(null, issues);
        }

        JsonDocument document;
        try
        {
            if (HasJsonDepthOverflow(utf8))
            {
                issues.Add("document.json_depth_exceeded", "/", "JSON nesting exceeds the supported limit.");
                return new FlowReadResult(null, issues);
            }

            DetectDuplicateProperties(utf8);
            document = JsonDocument.Parse(utf8, new JsonDocumentOptions
            {
                MaxDepth = FlowLimits.MaxJsonParseDepth,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (DuplicatePropertyException exception)
        {
            issues.Add("document.property_duplicate", exception.Path, "JSON object contains a duplicate property.");
            return new FlowReadResult(null, issues);
        }
        catch (JsonException)
        {
            issues.Add("document.json_malformed", "/", "Input is not valid JSON.");
            return new FlowReadResult(null, issues);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add("document.root_invalid", "/", "Flow document root must be an object.");
                return new FlowReadResult(null, issues);
            }

            var root = document.RootElement;
            ValidateUnknownFields(root, "/", ["id", "formatVersion", "role", "characterId", "entryNodeIds", "nodes", "dataEdges", "controlEdges"], issues, "document");
            var id = ReadRequiredString(root, "id", "/", issues, "document");
            var formatVersion = ReadRequiredInt(root, "formatVersion", "/", issues, "document");
            if (formatVersion is not null && formatVersion != SupportedFormatVersion)
            {
                issues.Add("document.version_unsupported", "/formatVersion", "Flow document format version is unsupported.");
            }

            var role = ReadRole(root, issues);
            var characterId = ReadOptionalString(root, "characterId", "/", issues, "document");
            var entryNodeIds = ReadStringArray(root, "entryNodeIds", "/", issues, "document");
            var nodes = ReadNodes(root, issues);
            var dataEdges = ReadDataEdges(root, issues);
            var controlEdges = ReadControlEdges(root, issues);

            if (issues.Count > 0 || id is null || formatVersion is null || role is null || nodes is null || dataEdges is null || controlEdges is null)
            {
                return new FlowReadResult(null, issues);
            }

            return new FlowReadResult(new GraphDocument(id, formatVersion.Value, role.Value, characterId, nodes, dataEdges, controlEdges, entryNodeIds ?? []), issues);
        }
    }

    private static List<FlowNode>? ReadNodes(JsonElement root, FlowIssues issues)
    {
        if (!root.TryGetProperty("nodes", out var element))
        {
            issues.Add("document.field_required", "/nodes", "Required field is missing.");
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add("document.field_invalid", "/nodes", "Field has an invalid JSON type.");
            return null;
        }

        var nodeElements = element.EnumerateArray().ToArray();
        if (nodeElements.Length > FlowLimits.MaxNodes)
        {
            issues.Add("document.nodes_limit", "/nodes", "Node count exceeds the supported limit.");
        }

        var nodes = new List<FlowNode>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (node, index) in nodeElements.Select((value, index) => (value, index)))
        {
            var path = $"/nodes/{index}";
            if (node.ValueKind != JsonValueKind.Object)
            {
                issues.Add("node.invalid", path, "Node must be an object.");
                continue;
            }

            ValidateUnknownFields(node, path, ["id", "kind", "blockVersion", "config"], issues, "node");
            var id = ReadRequiredString(node, "id", path, issues, "node");
            var kind = ReadRequiredString(node, "kind", path, issues, "node");
            var blockVersion = ReadRequiredInt(node, "blockVersion", path, issues, "node");
            if (blockVersion is <= 0)
            {
                issues.Add("node.block_version_invalid", Join(path, "blockVersion"), "Block version must be positive.");
            }

            if (!node.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object)
            {
                issues.Add("node.config_invalid", Join(path, "config"), "Node config must be an object.");
            }
            else
            {
                ValidatePrompts(config, Join(path, "config"), issues);
            }

            if (id is not null && !ids.Add(id))
            {
                issues.Add("node.id_duplicate", Join(path, "id"), "Node identifiers must be unique.");
            }

            if (id is not null && kind is not null && blockVersion is > 0 && config.ValueKind == JsonValueKind.Object)
            {
                nodes.Add(new FlowNode(id, kind, blockVersion.Value, config));
            }
        }

        return nodes;
    }

    private static List<DataEdge>? ReadDataEdges(JsonElement root, FlowIssues issues)
    {
        if (!TryReadEdgeArray(root, "dataEdges", issues, out var elements))
        {
            return null;
        }

        var result = new List<DataEdge>();
        foreach (var (edge, index) in elements.Select((value, index) => (value, index)))
        {
            var path = $"/dataEdges/{index}";
            if (edge.ValueKind != JsonValueKind.Object)
            {
                issues.Add("edge.invalid", path, "Edge must be an object.");
                continue;
            }

            ValidateUnknownFields(edge, path, ["source", "fieldPath", "target"], issues, "edge");
            var source = ReadPort(edge, "source", path, issues);
            var target = ReadPort(edge, "target", path, issues);
            var fieldPath = ReadStringArray(edge, "fieldPath", path, issues, "edge");
            if (source is not null && target is not null && fieldPath is not null)
            {
                result.Add(new DataEdge(source, fieldPath, target));
            }
        }

        return result;
    }

    private static List<ControlEdge>? ReadControlEdges(JsonElement root, FlowIssues issues)
    {
        if (!TryReadEdgeArray(root, "controlEdges", issues, out var elements))
        {
            return null;
        }

        var result = new List<ControlEdge>();
        foreach (var (edge, index) in elements.Select((value, index) => (value, index)))
        {
            var path = $"/controlEdges/{index}";
            if (edge.ValueKind != JsonValueKind.Object)
            {
                issues.Add("edge.invalid", path, "Edge must be an object.");
                continue;
            }

            ValidateUnknownFields(edge, path, ["source", "target"], issues, "edge");
            var source = ReadPort(edge, "source", path, issues);
            var target = ReadPort(edge, "target", path, issues);
            if (source is not null && target is not null)
            {
                result.Add(new ControlEdge(source, target));
            }
        }

        return result;
    }

    private static bool TryReadEdgeArray(JsonElement root, string name, FlowIssues issues, out JsonElement[] elements)
    {
        elements = [];
        if (!root.TryGetProperty(name, out var element))
        {
            issues.Add("document.field_required", "/" + name, "Required field is missing.");
            return false;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add("document.field_invalid", "/" + name, "Field has an invalid JSON type.");
            return false;
        }

        elements = element.EnumerateArray().ToArray();
        var dataCount = root.TryGetProperty("dataEdges", out var data) && data.ValueKind == JsonValueKind.Array ? data.GetArrayLength() : 0;
        var controlCount = root.TryGetProperty("controlEdges", out var control) && control.ValueKind == JsonValueKind.Array ? control.GetArrayLength() : 0;
        if (name == "dataEdges" && dataCount + controlCount > FlowLimits.MaxEdges)
        {
            issues.Add("document.edges_limit", "/", "Total edge count exceeds the supported limit.");
        }

        return true;
    }

    private static PortRef? ReadPort(JsonElement edge, string name, string path, FlowIssues issues)
    {
        var portPath = Join(path, name);
        if (!edge.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            issues.Add("port.invalid", portPath, "Port reference must be an object.");
            return null;
        }

        ValidateUnknownFields(element, portPath, ["nodeId", "port"], issues, "port");
        var nodeId = ReadRequiredString(element, "nodeId", portPath, issues, "port");
        var port = ReadRequiredString(element, "port", portPath, issues, "port");
        return nodeId is not null && port is not null ? new PortRef(nodeId, port) : null;
    }

    private static GraphRole? ReadRole(JsonElement root, FlowIssues issues)
    {
        var value = ReadRequiredString(root, "role", "/", issues, "document");
        return value switch
        {
            "ingress" => GraphRole.Ingress,
            "character" => GraphRole.Character,
            null => null,
            _ => AddRoleIssue(issues),
        };
    }

    private static GraphRole? AddRoleIssue(FlowIssues issues)
    {
        issues.Add("document.role_unsupported", "/role", "Flow document role is unsupported.");
        return null;
    }

    private static string? ReadRequiredString(JsonElement parent, string name, string path, FlowIssues issues, string issuePrefix)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            issues.Add(issuePrefix + ".field_required", Join(path, name), "Required field is missing.");
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            issues.Add(issuePrefix + ".field_invalid", Join(path, name), "Field has an invalid JSON type or value.");
            return null;
        }

        return element.GetString();
    }

    private static int? ReadRequiredInt(JsonElement parent, string name, string path, FlowIssues issues, string issuePrefix)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            issues.Add(issuePrefix + ".field_required", Join(path, name), "Required field is missing.");
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            issues.Add(issuePrefix + ".field_invalid", Join(path, name), "Field has an invalid JSON type or value.");
            return null;
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement parent, string name, string path, FlowIssues issues, string issuePrefix)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            issues.Add(issuePrefix + ".field_invalid", Join(path, name), "Field has an invalid JSON type or value.");
            return null;
        }

        return element.GetString();
    }

    private static string[]? ReadStringArray(JsonElement parent, string name, string path, FlowIssues issues, string issuePrefix)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(issuePrefix + ".field_invalid", Join(path, name), "Field has an invalid JSON type or value.");
            return null;
        }

        var values = new List<string>();
        foreach (var (value, index) in element.EnumerateArray().Select((value, index) => (value, index)))
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                issues.Add(issuePrefix + ".field_invalid", Join(Join(path, name), index.ToString()), "Array item has an invalid JSON type or value.");
                continue;
            }

            values.Add(value.GetString()!);
        }

        return values.ToArray();
    }

    private static void ValidatePrompts(JsonElement element, string path, FlowIssues issues)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = Join(path, property.Name);
                if (property.Name == "prompt" && property.Value.ValueKind == JsonValueKind.String &&
                    StrictUtf8.GetByteCount(property.Value.GetString()!) > FlowLimits.MaxPromptUtf8Bytes)
                {
                    issues.Add("node.prompt_too_large", propertyPath, "Prompt exceeds the supported UTF-8 size.");
                }

                ValidatePrompts(property.Value, propertyPath, issues);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var (value, index) in element.EnumerateArray().Select((value, index) => (value, index)))
            {
                ValidatePrompts(value, Join(path, index.ToString()), issues);
            }
        }
    }

    private static void ValidateUnknownFields(JsonElement element, string path, string[] allowed, FlowIssues issues, string issuePrefix)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedSet.Contains(property.Name))
            {
                issues.Add(issuePrefix + ".field_unknown", Join(path, property.Name), "Field is not supported.");
            }
        }
    }

    private static void DetectDuplicateProperties(byte[] utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = int.MaxValue, CommentHandling = JsonCommentHandling.Disallow });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                objects.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString()!;
                if (objects.Count > 0 && !objects.Peek().Add(name))
                {
                    throw new DuplicatePropertyException("/");
                }
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                objects.Pop();
            }
        }
    }

    private static string Join(string path, string segment) =>
        path == "/" ? "/" + Escape(segment) : path + "/" + Escape(segment);

    private static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static bool HasJsonDepthOverflow(byte[] utf8)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        foreach (var value in utf8)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (value == (byte)'\\')
                {
                    escaped = true;
                }
                else if (value == (byte)'\"')
                {
                    inString = false;
                }

                continue;
            }

            if (value == (byte)'\"')
            {
                inString = true;
            }
            else if (value is (byte)'{' or (byte)'[')
            {
                depth++;
                if (depth > FlowLimits.MaxJsonParseDepth)
                {
                    return true;
                }
            }
            else if (value is (byte)'}' or (byte)']')
            {
                depth--;
            }
        }

        return false;
    }

    private sealed class DuplicatePropertyException(string path) : Exception
    {
        public string Path { get; } = path;
    }
}
