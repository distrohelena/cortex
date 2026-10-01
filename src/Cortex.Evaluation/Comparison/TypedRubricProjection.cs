using System.Text.Json;
using Cortex.Evaluation.Contracts;

namespace Cortex.Evaluation.Comparison;

public sealed record TypedRubric(IReadOnlyList<FieldCriterion> Criteria, IReadOnlyList<string> NotEvaluatedDimensions);

public static class TypedRubricProjection
{
    public static TypedRubric Project(JsonElement dimensions, JsonElement expectations,
        IReadOnlyCollection<string> outputFields, IReadOnlyCollection<string> criticalDimensions)
    {
        try
        {
            if (dimensions.ValueKind != JsonValueKind.Object || expectations.ValueKind != JsonValueKind.Object ||
                outputFields is null || criticalDimensions is null) throw Invalid();
            RevisionHasher.ValidateJson(dimensions, 0);
            RevisionHasher.ValidateJson(expectations, 0);
            var definitions = dimensions.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            if (criticalDimensions.Any(name => !definitions.ContainsKey(name)) || outputFields.Any(string.IsNullOrWhiteSpace)) throw Invalid();
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions.Values)
            {
                var field = definition.GetProperty("currentField").GetString();
                if (string.IsNullOrWhiteSpace(field) || !targets.Add(field) ||
                    definition.GetProperty("valueType").GetString() is not ("enum" or "boolean")) throw Invalid();
            }
            var criteria = new List<FieldCriterion>();
            var notEvaluated = new List<string>();
            foreach (var expectation in expectations.EnumerateObject())
            {
                if (!definitions.TryGetValue(expectation.Name, out var definition) || expectation.Value.ValueKind != JsonValueKind.Array ||
                    expectation.Value.GetArrayLength() == 0) throw Invalid();
                var field = definition.GetProperty("currentField").GetString()!;
                var accepted = expectation.Value.EnumerateArray().Select(v => v.Clone()).ToArray();
                if (definition.GetProperty("valueType").GetString() == "boolean")
                {
                    if (accepted.Any(v => v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))) throw Invalid();
                }
                else
                {
                    var possible = definition.GetProperty("values").EnumerateArray().Select(v => v.GetString()).ToHashSet(StringComparer.Ordinal);
                    if (possible.Count == 0 || possible.Any(string.IsNullOrWhiteSpace) ||
                        accepted.Any(v => v.ValueKind != JsonValueKind.String || !possible.Contains(v.GetString()))) throw Invalid();
                }
                if (!outputFields.Contains(field, StringComparer.Ordinal)) notEvaluated.Add(expectation.Name);
                else criteria.Add(new(expectation.Name, field, Array.AsReadOnly(accepted),
                    criticalDimensions.Contains(expectation.Name, StringComparer.Ordinal)));
            }
            return new(criteria.AsReadOnly(), notEvaluated.AsReadOnly());
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private static InvalidDataException Invalid() => new("Invalid typed rubric mapping or expected value.");
}
