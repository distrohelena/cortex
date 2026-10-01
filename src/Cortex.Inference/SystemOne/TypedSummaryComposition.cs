using Cortex.Core.Flows;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cortex.Inference.SystemOne;

/// <summary>Combines typed fields with a separately validated summary; never returns a partial result.</summary>
public static class TypedSummaryComposition
{
    public static async Task<string> CompleteAsync(IFlowCompletion typed, IFlowCompletion baseline,
        FlowCompletionRequest request, string provider, string model, string summaryField, CancellationToken token)
    {
        if (request.ResponseSchema is not { } original) throw new InvalidDataException("Missing situation schema.");
        var schema = JsonNode.Parse(original.GetRawText())!.AsObject();
        if (schema["type"]?.GetValue<string>() != "object" || schema["additionalProperties"]?.GetValue<bool>() != false ||
            schema["properties"] is not JsonObject properties || schema["required"] is not JsonArray required ||
            required.Count != properties.Count || !required.Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(properties.Select(p => p.Key)) ||
            properties[summaryField] is not JsonObject summaryDefinition || summaryDefinition["type"]?.GetValue<string>() != "string" || properties.Count < 2)
            throw new InvalidDataException("Invalid situation/summary schema.");
        var summaryCopy = summaryDefinition.DeepClone();
        properties.Remove(summaryField);
        schema["required"] = new JsonArray(properties.Select(p => JsonValue.Create(p.Key)).ToArray());
        var answer = JsonNode.Parse(await typed.CompleteAsync(request with { Provider = provider, Model = model, ResponseSchema = JsonSerializer.SerializeToElement(schema) }, token))!.AsObject();
        if (!answer.Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(properties.Select(p => p.Key)))
            throw new InvalidDataException("Typed situation fields do not match the schema.");
        var summarySchema = new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = new JsonObject { [summaryField] = summaryCopy },
            ["required"] = new JsonArray(summaryField)
        };
        using var summary = JsonDocument.Parse(await baseline.CompleteAsync(request with
        { ResponseSchema = JsonSerializer.SerializeToElement(summarySchema) }, token));
        var root = summary.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty(summaryField, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("Invalid generative summary; no partial situation may escape.");
        answer[summaryField] = value.GetString();
        return answer.ToJsonString();
    }
}
