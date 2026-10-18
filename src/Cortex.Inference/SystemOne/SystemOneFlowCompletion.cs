using Cortex.Core.Flows;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cortex.Inference.SystemOne;

public sealed record SystemOneChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string> Criteria);

/// <summary>Enum/boolean-only flow completion. Free text is deliberately not emulated.</summary>
public sealed class SystemOneFlowCompletion(SystemOneClient client, string provider, double booleanThreshold,
    Action<string, SystemOneResult>? observe = null,
    IReadOnlyDictionary<string, SystemOneChoiceQuestion>? choiceQuestions = null) : IFlowCompletion
{
    private readonly Dictionary<string, SystemOneChoiceQuestion> profiles = choiceQuestions?.ToDictionary(p => p.Key,
        p => new SystemOneChoiceQuestion(p.Value.Instructions, p.Value.Criteria.ToDictionary()), StringComparer.Ordinal) ?? [];

    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        if (provider is not ("jev" or "laya") || request.Provider != provider || string.IsNullOrWhiteSpace(request.Model) ||
            !double.IsFinite(booleanThreshold) || booleanThreshold is <= 0 or >= 1)
            throw new InvalidDataException("System 1 requires an explicit provider, model and boolean threshold in (0,1).");
        if (provider == "laya" && request.Model is not ("english" or "multilingual" or "typed-decisions" or
            "convaiinnovations/laya" or "convaiinnovations/laya-multilingual" or "convaiinnovations/laya-typed-decisions"))
            throw new InvalidDataException("Unsupported Laya model; implicit auto-routing is forbidden.");
        var questions = Questions(request);
        var result = request.Diagnostics is { } diagnostics
            ? await client.DecideAsync(request.Model, request.Inputs, questions, diagnostics, token)
            : await client.DecideAsync(request.Model, request.Inputs, questions, token);
        var output = new JsonObject();
        foreach (var question in questions.EnumerateObject())
        {
            var answer = result.Answers.GetProperty(question.Name);
            output[question.Name] = question.Value.GetProperty("type").GetString() == "choice"
                ? JsonValue.Create(answer.GetProperty("choice").GetString())
                : JsonValue.Create(answer.GetProperty("noul").GetDouble() >= booleanThreshold);
        }
        observe?.Invoke(request.NodeId, result);
        return output.ToJsonString();
    }

    private JsonElement Questions(FlowCompletionRequest request)
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
                        if (criteria.Count is < 2 or > 255) throw Unsupported();
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
