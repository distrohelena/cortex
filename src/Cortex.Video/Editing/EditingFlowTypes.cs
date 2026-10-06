using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;

namespace Cortex.Video.Editing;

public static class EditingFlowTypes
{
    public static ObjectType Script { get; } = CreateScript();
    public static ObjectType ScriptReference { get; } = new(new Dictionary<string, FlowField>
    {
        ["path"] = new(new StringType(), true), ["revision"] = new(new NumberType(), true), ["sha256"] = new(new StringType(), true)
    });

    public static JsonElement ExecutionProjection(EditScriptDocument document)
    {
        var value = JsonNode.Parse(EditingJson.Serialize(document).GetRawText())!.AsObject();
        value.Remove("metadata");
        return JsonSerializer.SerializeToElement(value);
    }

    private static ObjectType CreateScript()
    {
        var schema = EditingSchemas.Get(EditingDocumentKinds.Script);
        return (ObjectType)Convert(schema, schema, true);
    }

    private static FlowType Convert(JsonElement schema, JsonElement root, bool top = false)
    {
        if (schema.TryGetProperty("$ref", out var reference)) return Convert(root.GetProperty("$defs").GetProperty(reference.GetString()!.Split('/')[^1]), root);
        if (schema.TryGetProperty("const", out var constant)) return new EnumType([constant.GetString()!]);
        if (schema.TryGetProperty("enum", out var options)) return new EnumType(options.EnumerateArray().Select(value => value.GetString()!));
        var kind = schema.GetProperty("type").GetString();
        return kind switch
        {
            "string" => new StringType(), "integer" or "number" => new NumberType(), "boolean" => new BooleanType(),
            "array" => new ArrayType(Convert(schema.GetProperty("items"), root)),
            "object" => Object(schema, root, top),
            _ => throw new InvalidDataException("Unsupported editorial execution field type.")
        };
    }

    private static ObjectType Object(JsonElement schema, JsonElement root, bool top)
    {
        var required = schema.TryGetProperty("required", out var names) ? names.EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal) : [];
        return new(schema.GetProperty("properties").EnumerateObject().Where(property => !top || property.Name != "metadata")
            .ToDictionary(property => property.Name, property => new FlowField(Convert(property.Value, root), required.Contains(property.Name))));
    }
}
