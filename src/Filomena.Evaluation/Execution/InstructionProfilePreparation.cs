using Filomena.Core.Flows;
using Filomena.Evaluation.Contracts;
using System.Text.Json;

namespace Filomena.Evaluation.Execution;

public static class InstructionProfilePreparation
{
    // Local preparation only; settings are an explicit host-selected candidate, not active config.
    // Prompt and schema come from the inspected instruction; summary instructions remain explicit.
    public static ConfigurationProfile Bind(FlowInstructionSnapshot instruction, ConfigurationProfile settings)
    {
        if (string.IsNullOrWhiteSpace(instruction.ProjectRevision) || string.IsNullOrWhiteSpace(instruction.StateId) ||
            string.IsNullOrWhiteSpace(instruction.NodeId) || string.IsNullOrWhiteSpace(instruction.System))
            throw new InvalidDataException("Instruction provenance is required.");
        var summary = settings.SummaryProfile;
        if (settings.Provider is "jev" or "laya")
        {
            var summaryDefinition = ValidateTypedSchema(instruction.ResponseSchema, summary is not null);
            if (summaryDefinition is { } definition && summary is not null)
            {
                var summarySchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object", additionalProperties = false,
                    properties = new Dictionary<string, JsonElement> { ["resumo"] = definition },
                    required = new[] { "resumo" }
                });
                summary = new(summary.ContractVersion, summary.Provider, summary.Model, summary.ResolvedModelId,
                    summary.EndpointAlias, summary.CapturedAt, summary.Parameters, Schema(summarySchema), summary.Prompt);
            }
        }
        var prompt = new RevisionDocument(1, "prompt", JsonSerializer.SerializeToElement(new
        {
            system = instruction.System,
            source = new { projectRevision = instruction.ProjectRevision, stateId = instruction.StateId, nodeId = instruction.NodeId }
        }));
        var result = new ConfigurationProfile(settings.ContractVersion, settings.Recipe, settings.Provider,
            settings.Model, settings.EndpointAlias, settings.ResolvedModelId, settings.CapturedAt, settings.Parameters,
            settings.Threshold, settings.ChoiceQuestions, Schema(instruction.ResponseSchema), prompt, summary);
        if (ProfileValidator.Validate(result).Count != 0) throw new InvalidDataException("Invalid prepared evaluation profile.");
        return result;
    }

    private static RevisionDocument Schema(JsonElement? schema) => new(1, "schema",
        JsonSerializer.SerializeToElement(new { contractVersion = 1, responseSchema = schema }));

    private static JsonElement? ValidateTypedSchema(JsonElement? schema, bool hasSummary)
    {
        try
        {
            if (schema is not { } root || root.GetProperty("type").GetString() != "object" ||
                root.GetProperty("additionalProperties").ValueKind != JsonValueKind.False) throw Unsupported();
            RevisionHasher.ValidateJson(root, 0);
            var properties = root.GetProperty("properties").EnumerateObject().ToArray();
            var required = root.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToArray();
            if (properties.Length == 0 || properties.Length != required.Length ||
                !required.ToHashSet(StringComparer.Ordinal).SetEquals(properties.Select(p => p.Name))) throw Unsupported();
            JsonElement? summary = null;
            foreach (var field in properties)
            {
                var definition = field.Value;
                var type = definition.GetProperty("type").GetString();
                if (field.Name == "resumo" && type == "string" && !definition.TryGetProperty("enum", out _))
                {
                    if (!hasSummary || properties.Length < 2) throw Unsupported();
                    summary = definition.Clone();
                    continue;
                }
                if (definition.EnumerateObject().Any(p => p.Name is not ("type" or "enum" or "description" or "title"))) throw Unsupported();
                if (type == "boolean" && !definition.TryGetProperty("enum", out _)) continue;
                if (type != "string" || !definition.TryGetProperty("enum", out var values)) throw Unsupported();
                var choices = values.EnumerateArray().Select(v => v.GetString()).ToArray();
                if (choices.Length is < 2 or > 255 || choices.Any(string.IsNullOrWhiteSpace) ||
                    choices.Distinct(StringComparer.Ordinal).Count() != choices.Length) throw Unsupported();
            }
            if (hasSummary != (summary is not null)) throw Unsupported();
            return summary;
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw Unsupported();
        }
    }

    private static InvalidDataException Unsupported() => new("Typed evaluation requires enums/booleans; free resumo requires an explicit summary model. Other generation is unsupported.");
}
