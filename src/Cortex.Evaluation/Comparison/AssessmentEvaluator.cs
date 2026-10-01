using System.Text.Json;

namespace Cortex.Evaluation.Comparison;

public sealed record FieldCriterion(string Id, string Field, IReadOnlyList<JsonElement> AcceptedValues, bool Critical);
public sealed record CriterionAssessment(string Criterion, string Outcome, string Diagnostic, bool CriticalFailure);
public sealed record OutputAssessment(string Outcome, IReadOnlyList<CriterionAssessment> Criteria);

public static class AssessmentEvaluator
{
    // Only deterministic enum/boolean criteria are judged here. Semantic/style review is explicit,
    // and this partial rubric does not claim validation of the provider's entire JSON schema.
    public static OutputAssessment Evaluate(string output, IReadOnlyList<FieldCriterion> criteria,
        IReadOnlyList<string> manualCriteria)
    {
        if (output is null || criteria is null || manualCriteria is null)
            throw new InvalidDataException("Missing evaluation input.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var criterion in criteria)
        {
            if (criterion is null || !ValidId(criterion.Id) || !ids.Add(criterion.Id) || !ValidId(criterion.Field) ||
                criterion.AcceptedValues is null || criterion.AcceptedValues.Count == 0 || criterion.AcceptedValues.Any(v =>
                    v.ValueKind is not (JsonValueKind.String or JsonValueKind.True or JsonValueKind.False)))
                throw new InvalidDataException("Invalid deterministic evaluation criterion.");
        }
        foreach (var id in manualCriteria)
            if (!ValidId(id) || !ids.Add(id)) throw new InvalidDataException("Invalid manual evaluation criterion.");
        var results = new List<CriterionAssessment>();
        JsonDocument? document = null;
        try
        {
            if (output.Length > 1_048_576) throw new JsonException();
            document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !Unique(document.RootElement))
                throw new JsonException();
            foreach (var criterion in criteria)
            {
                var exists = document.RootElement.TryGetProperty(criterion.Field, out var actual);
                var accepted = exists && criterion.AcceptedValues.Any(value => JsonElement.DeepEquals(value, actual));
                results.Add(new(criterion.Id, accepted ? "pass" : "fail",
                    accepted ? "accepted_value" : exists ? "unexpected_value" : "missing_field",
                    !accepted && criterion.Critical));
            }
        }
        catch (JsonException)
        {
            foreach (var criterion in criteria)
                results.Add(new(criterion.Id, "fail", "invalid_output", criterion.Critical));
        }
        finally { document?.Dispose(); }
        results.AddRange(manualCriteria.Select(id => new CriterionAssessment(id, "unscored", "manual_review_required", false)));
        var outcome = results.Any(r => r.Outcome == "fail") ? "fail"
            : results.Count == 0 || results.Any(r => r.Outcome == "unscored") ? "unscored" : "pass";
        return new(outcome, results.AsReadOnly());
    }

    private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 && !value.Any(char.IsControl);

    private static bool Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array) return element.EnumerateArray().All(Unique);
        if (element.ValueKind != JsonValueKind.Object) return true;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().All(property => names.Add(property.Name) && Unique(property.Value));
    }
}
