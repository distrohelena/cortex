using Cortex.Core.Flows;
using Cortex.Inference.SystemOne;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cortex.Inference;

/// <summary>Converts the closed enum/boolean flow schema subset into typed-decision questions.</summary>
internal static class TypedDecisionSchema
{
    internal static JsonElement Questions(FlowCompletionRequest request,
        IReadOnlyDictionary<string, SystemOneChoiceQuestion> profiles, int maximumOptions)
    {
        try
        {
            if (request.ResponseSchema is not { } schema || schema.GetProperty("type").GetString() != "object" ||
                schema.GetProperty("additionalProperties").ValueKind != JsonValueKind.False)
                throw Unsupported();
            var properties = schema.GetProperty("properties").EnumerateObject().ToArray();
            var required = schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()).ToArray();
            if (properties.Length == 0 || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length ||
                required.Length != properties.Length || !required.ToHashSet(StringComparer.Ordinal).SetEquals(properties.Select(p => p.Name))) throw Unsupported();
            var questions = new JsonObject();
            foreach (var property in properties)
            {
                var definition = property.Value;
                if (definition.EnumerateObject().Any(p => p.Name is not ("type" or "enum" or "description" or "title"))) throw Unsupported();
                var instructions = new JsonObject
                {
                    ["field"] = property.Name,
                    ["description"] = definition.TryGetProperty("description", out var text) ? text.GetString() : property.Name,
                    ["task"] = request.System
                };
                var question = new JsonObject { ["instructions"] = instructions };
                switch (definition.GetProperty("type").GetString())
                {
                    case "boolean":
                        if (definition.TryGetProperty("enum", out _)) throw Unsupported();
                        question["type"] = "noul";
                        break;
                    case "string" when definition.TryGetProperty("enum", out var choices):
                        var criteria = new JsonObject();
                        foreach (var choice in choices.EnumerateArray())
                        {
                            var value = choice.GetString();
                            if (string.IsNullOrWhiteSpace(value) || criteria.ContainsKey(value)) throw Unsupported();
                            criteria[value] = value;
                        }
                        if (criteria.Count < 2 || criteria.Count > maximumOptions) throw Unsupported();
                        if (profiles.TryGetValue(property.Name, out var profile))
                        {
                            if (string.IsNullOrWhiteSpace(profile.Instructions) || profile.Criteria.Any(p => string.IsNullOrWhiteSpace(p.Value)) ||
                                !profile.Criteria.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(criteria.Select(p => p.Key))) throw Unsupported();
                            // Choice semantics supplement the task; they must not erase the
                            // pinned node instructions, state guidance or field description.
                            instructions["choiceGuidance"] = profile.Instructions;
                            foreach (var option in profile.Criteria) criteria[option.Key] = option.Value;
                        }
                        question["type"] = "choice";
                        question["criteria"] = criteria;
                        break;
                    default: throw Unsupported();
                }
                questions.Add(property.Name, question);
            }
            return JsonSerializer.SerializeToElement(questions);
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw Unsupported(); }
    }
    private static InvalidDataException Unsupported() => new("System 1 flow outputs must be a closed object of required string enums or booleans; free text and implicit score scales are unsupported.");
}
