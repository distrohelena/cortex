using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Filomena.Evaluation.Contracts;

public static partial class ManifestValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ExperimentManifest manifest)
    {
        var issues = new List<ValidationIssue>();
        if (manifest is null)
        {
            issues.Add(new("manifest.missing", "manifest"));
            return issues.AsReadOnly();
        }

        if (manifest.Scope.WorkspaceId == Guid.Empty || manifest.Scope.ProjectId == Guid.Empty)
        {
            issues.Add(new("scope.invalid", "scope"));
        }

        AddReferenceIssue(manifest.Target, manifest.Scope, "target", issues);
        AddReferenceIssue(manifest.Baseline, manifest.Scope, "baseline", issues);
        AddReferenceIssue(manifest.Dataset, manifest.Scope, "dataset", issues);
        AddReferenceIssue(manifest.Evaluator, manifest.Scope, "evaluator", issues);
        AddReferenceIssue(manifest.Policy, manifest.Scope, "policy", issues);

        if (manifest.Candidates.IsDefaultOrEmpty || manifest.Candidates.Length > MaximumCandidates)
        {
            issues.Add(new("manifest.candidates_invalid", "candidates"));
        }
        else
        {
            for (var index = 0; index < manifest.Candidates.Length; index++)
            {
                AddReferenceIssue(manifest.Candidates[index], manifest.Scope, $"candidates[{index}]", issues);
            }
        }

        if (manifest.Repetitions is < 1 or > MaximumRepetitions)
        {
            issues.Add(new("manifest.repetitions_invalid", "repetitions"));
        }

        if (!Enum.IsDefined(manifest.Order))
        {
            issues.Add(new("manifest.order_invalid", "order"));
        }

        if (manifest.Budgets is null || manifest.Budgets.MaxCalls <= 0 || manifest.Budgets.MaxCalls > MaximumCalls ||
            manifest.Budgets.MaxInputTokens <= 0 || manifest.Budgets.MaxOutputTokens <= 0 || manifest.Budgets.MaxCost <= 0)
        {
            issues.Add(new("budget.invalid", "budgets"));
        }

        if (manifest.StopRules is null || manifest.StopRules.MinRepetitions < 1 ||
            manifest.StopRules.MaxRepetitions < manifest.StopRules.MinRepetitions ||
            manifest.StopRules.MaxRepetitions > MaximumRepetitions ||
            (manifest.StopRules.MaxFailureRate is double rate && (!double.IsFinite(rate) || rate is < 0 or > 1)))
        {
            issues.Add(new("stop_rules.invalid", "stopRules"));
        }

        if (manifest.StopRules is not null && manifest.Repetitions > manifest.StopRules.MaxRepetitions)
        {
            issues.Add(new("stop_rules.repetitions_exceed_max", "repetitions"));
        }

        return issues.AsReadOnly();
    }

    internal static void AddReferenceIssue(ScopedRevisionRef? reference, EvaluationScope scope, string path, List<ValidationIssue> issues)
    {
        if (reference is null || reference.Revision is null || reference.Revision.Id == Guid.Empty)
        {
            issues.Add(new("revision.missing", path));
            return;
        }

        if (reference.Scope.WorkspaceId == Guid.Empty || reference.Scope.ProjectId == Guid.Empty)
        {
            issues.Add(new("revision.scope_invalid", path));
        }

        if (reference.Scope != scope)
        {
            issues.Add(new("revision.scope_mismatch", path));
        }

        if (reference.Revision.Sha256 is null || !Sha256Pattern().IsMatch(reference.Revision.Sha256))
        {
            issues.Add(new("revision.hash_invalid", path));
        }
    }

    private const int MaximumCandidates = 256;
    private const int MaximumRepetitions = 100_000;
    private const int MaximumCalls = 10_000_000;

    [GeneratedRegex("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

public static class ProfileValidator
{
    private sealed record ProviderDescriptor(FrozenSet<string> Parameters, bool HasThreshold, bool HasChoiceQuestions);

    private static readonly FrozenDictionary<string, ProviderDescriptor> SupportedProviders =
        new Dictionary<string, ProviderDescriptor>(StringComparer.Ordinal)
        {
            ["luna"] = new(new[] { "reasoningEffort", "maxOutputTokens" }.ToFrozenSet(StringComparer.Ordinal), false, false),
            ["lm-studio"] = new(new[] {
                "arbitrationTemperature", "arbitrationMaxTokens", "generationTemperature",
                "generationMaxTokens", "requestTimeoutSeconds"
            }.ToFrozenSet(StringComparer.Ordinal), false, false),
            ["jev"] = new(Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal), true, true)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SupportedEfforts =
        new[] { "low", "medium", "high", "xhigh", "max" }.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<ValidationIssue> Validate(ConfigurationProfile profile)
    {
        var issues = new List<ValidationIssue>();
        if (profile is null)
        {
            issues.Add(new("profile.missing", "profile"));
            return issues.AsReadOnly();
        }

        if (profile.ContractVersion != 1)
        {
            issues.Add(new("profile.contract_version", "contractVersion"));
        }

        if (profile.Recipe is not ("completion" or "typed-with-summary"))
        {
            issues.Add(new("profile.recipe_unsupported", "recipe"));
        }

        if (!SupportedProviders.TryGetValue(profile.Provider ?? string.Empty, out var descriptor))
        {
            issues.Add(new("profile.provider_unsupported", "provider"));
            descriptor = null;
        }

        AddRequired(profile.Provider, "provider", issues);
        AddRequired(profile.Model, "model", issues);
        AddRequired(profile.EndpointAlias, "endpointAlias", issues);
        AddRequired(profile.ResolvedModelId, "resolvedModelId", issues);
        if (profile.CapturedAt == default)
        {
            issues.Add(new("profile.snapshot_missing", "capturedAt"));
        }

        if (profile.Recipe == "typed-with-summary" && descriptor?.HasThreshold == true)
        {
            if (profile.Threshold is not double threshold || !double.IsFinite(threshold) || threshold is <= 0 or >= 1)
            {
                issues.Add(new("profile.threshold_invalid", "threshold"));
            }
        }
        else if (profile.Threshold is not null)
        {
            issues.Add(new("profile.threshold_unsupported", "threshold"));
        }

        ValidateParameters(profile.Parameters, profile.Provider ?? string.Empty, descriptor, issues);
        ValidateChoiceQuestions(profile.ChoiceQuestions, descriptor, profile.Recipe == "typed-with-summary", issues);

        if (profile.Recipe == "typed-with-summary")
        {
            if (profile.SummaryProfile is null)
            {
                issues.Add(new("profile.summary_profile_required", "summaryProfile"));
            }
            else
            {
                ValidateDocument(profile.SummaryProfile, "summaryProfile", issues);
                if (!string.Equals(profile.SummaryProfile.Kind, "summary-profile", StringComparison.Ordinal))
                {
                    issues.Add(new("profile.summary_profile_invalid", "summaryProfile"));
                }
            }
        }

        ValidateDocument(profile.Schema, "schema", issues);
        if (profile.Schema is not null)
        {
            if (!string.Equals(profile.Schema.Kind, "schema", StringComparison.Ordinal) ||
                profile.Schema.Payload.ValueKind != JsonValueKind.Object ||
                !profile.Schema.Payload.TryGetProperty("contractVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1)
            {
                issues.Add(new("schema.contract_version", "schema.contractVersion"));
            }
        }

        ValidateDocument(profile.Prompt, "prompt", issues);
        return issues.AsReadOnly();
    }

    private static void ValidateParameters(JsonElement parameters, string provider, ProviderDescriptor? descriptor, List<ValidationIssue> issues)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new("profile.parameters_invalid", "parameters"));
            return;
        }

        try
        {
            RevisionHasher.ValidateJson(parameters, 0);
        }
        catch (ArgumentException)
        {
            issues.Add(new("profile.parameters_invalid", "parameters"));
            return;
        }

        foreach (var parameter in parameters.EnumerateObject())
        {
            var path = $"parameters.{parameter.Name}";
            if (descriptor is null || !descriptor.Parameters.Contains(parameter.Name))
            {
                issues.Add(new("profile.parameter_unsupported", path));
                continue;
            }

            var valid = provider switch
            {
                "luna" when parameter.Name == "reasoningEffort" => parameter.Value.ValueKind == JsonValueKind.String &&
                    parameter.Value.GetString() is string effort && SupportedEfforts.Contains(effort),
                "luna" when parameter.Name == "maxOutputTokens" => IsPositiveInt(parameter.Value, 1_000_000),
                "lm-studio" when parameter.Name is "arbitrationTemperature" or "generationTemperature" => IsNumberInRange(parameter.Value, 0, 2),
                "lm-studio" when parameter.Name is "arbitrationMaxTokens" or "generationMaxTokens" => IsPositiveInt(parameter.Value, 1_000_000),
                "lm-studio" when parameter.Name == "requestTimeoutSeconds" => IsTimeout(parameter.Value),
                _ => false
            };

            if (!valid)
            {
                issues.Add(new("profile.parameter_invalid", path));
            }
        }
    }

    private static bool IsNumberInRange(JsonElement element, double minimum, double maximum) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value) &&
        double.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool IsPositiveInt(JsonElement element, int maximum) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && value > 0 && value <= maximum;

    private static bool IsTimeout(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && (value == -1 || value > 0);

    private static void ValidateChoiceQuestions(
        IReadOnlyList<ChoiceQuestionProfile>? questions,
        ProviderDescriptor? descriptor,
        bool isTypedRecipe,
        List<ValidationIssue> issues)
    {
        if (questions is null || questions.Count > 128)
        {
            issues.Add(new("profile.choice_questions_invalid", "choiceQuestions"));
            return;
        }

        if (!isTypedRecipe || descriptor?.HasChoiceQuestions != true)
        {
            if (questions.Count != 0)
            {
                issues.Add(new("profile.choice_questions_unsupported", "choiceQuestions"));
            }

            return;
        }

        if (questions.Count == 0)
        {
            issues.Add(new("profile.choice_questions_required", "choiceQuestions"));
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            var path = $"choiceQuestions[{index}]";
            if (question is null || string.IsNullOrWhiteSpace(question.Field) || question.Field.Length > 256 ||
                !fields.Add(question.Field) || string.IsNullOrWhiteSpace(question.Instructions) ||
                question.Instructions.Length > 16_384 || question.Criteria is null || question.Criteria.Count is < 2 or > 255 ||
                question.Criteria.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 256 ||
                    string.IsNullOrWhiteSpace(item.Value) || item.Value.Length > 16_384))
            {
                issues.Add(new("profile.choice_question_invalid", path));
            }
        }
    }

    private static void ValidateDocument(RevisionDocument? document, string path, List<ValidationIssue> issues)
    {
        if (document is null)
        {
            issues.Add(new("revision.missing", path));
            return;
        }

        try
        {
            _ = RevisionHasher.Hash(document);
        }
        catch (ArgumentException)
        {
            issues.Add(new("revision.document_invalid", path));
        }
    }

    private static void AddRequired(string? value, string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl))
        {
            issues.Add(new("profile.field_invalid", path));
        }
    }
}
