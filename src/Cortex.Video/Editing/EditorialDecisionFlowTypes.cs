using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
namespace Cortex.Video.Editing;
public static class EditorialDecisionFlowTypes
{
    public static ObjectType Plan{get;}=Create();
    private static ObjectType Create(){var schema=JsonNode.Parse(EditingSchemas.Get(EditingDocumentKinds.Decisions).GetRawText())!;schema["properties"]!.AsObject().Remove("metadata");var scene=schema["properties"]!["scenes"]!["items"]!;scene["properties"]!.AsObject().Remove("overrides");scene["required"]=new JsonArray(scene["required"]!.AsArray().Where(n=>n!.GetValue<string>()!="overrides").Select(n=>JsonValue.Create(n!.GetValue<string>())).ToArray<JsonNode?>());return (ObjectType)Convert(JsonSerializer.SerializeToElement(schema));}
    private static FlowType Convert(JsonElement schema){if(schema.TryGetProperty("const",out var constant))return new EnumType([constant.GetString()!]);if(schema.TryGetProperty("enum",out var codes))return new EnumType(codes.EnumerateArray().Select(c=>c.GetString()!));return schema.GetProperty("type").GetString() switch{
        "string"=>new StringType(),"integer" or "number"=>new NumberType(),"boolean"=>new BooleanType(),"array"=>new ArrayType(Convert(schema.GetProperty("items"))),"object"=>Object(schema),_=>throw new InvalidDataException("Unknown decision flow type.")};}
    private static ObjectType Object(JsonElement schema){var required=schema.TryGetProperty("required",out var names)?names.EnumerateArray().Select(n=>n.GetString()).ToHashSet():[];return new(schema.GetProperty("properties").EnumerateObject().ToDictionary(p=>p.Name,p=>new FlowField(Convert(p.Value),required.Contains(p.Name))));}
    public static JsonElement ExecutionProjection(EditorialDecisionPlanDocument document){var node=JsonNode.Parse(EditingJson.Serialize(EditorialDecisionMerge.Effective(document)).GetRawText())!;node.AsObject().Remove("metadata");foreach(var scene in node["scenes"]!.AsArray())scene!.AsObject().Remove("overrides");return JsonSerializer.SerializeToElement(node);}
}
