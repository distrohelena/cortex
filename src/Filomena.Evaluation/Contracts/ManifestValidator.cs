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

        if (manifest.Candidates is null || manifest.Candidates.Count is 0 or > MaximumCandidates)
        {
            issues.Add(new("manifest.candidates_invalid", "candidates"));
        }
        else
        {
            for (var index = 0; index < manifest.Candidates.Count; index++)
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
    private sealed record ProviderDescriptor(
        FrozenSet<string> Parameters,
        bool SupportsCompletion,
        bool SupportsTyped,
        bool HasThreshold,
        bool HasChoiceQuestions,
        FrozenSet<string> Models);

    private static readonly FrozenSet<string> EmptySet = Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ProviderDescriptor> SupportedProviders =
        new Dictionary<string, ProviderDescriptor>(StringComparer.Ordinal)
        {
            ["openai"] = new(new[] { "reasoningEffort", "maxOutputTokens" }.ToFrozenSet(StringComparer.Ordinal), true, false, false, false, EmptySet),
            ["lmstudio"] = new(new[] {
                "arbitrationTemperature", "arbitrationMaxTokens", "generationTemperature",
                "generationMaxTokens", "requestTimeoutSeconds"
            }.ToFrozenSet(StringComparer.Ordinal), true, false, false, false, EmptySet),
            ["jev"] = new(EmptySet, false, true, true, true, EmptySet),
            ["laya"] = new(EmptySet, false, true, true, true, new[]
            {
                "english", "multilingual", "typed-decisions", "convaiinnovations/laya",
                "convaiinnovations/laya-multilingual", "convaiinnovations/laya-typed-decisions"
            }.ToFrozenSet(StringComparer.Ordinal))
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SupportedEfforts =
        new[] { "minimal", "low", "medium", "high", "xhigh" }.ToFrozenSet(StringComparer.Ordinal);

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

        if (!SupportedProviders.TryGetValue(profile.Provider ?? string.Empty, out var descriptor))
        {
            issues.Add(new("profile.provider_unsupported", "provider"));
            descriptor = null;
        }

        var isCompletion = profile.Recipe == "completion";
        var isTyped = profile.Recipe == "typed-with-summary";
        if (!isCompletion && !isTyped)
        {
            issues.Add(new("profile.recipe_unsupported", "recipe"));
        }
        else if (descriptor is not null &&
            (isCompletion && !descriptor.SupportsCompletion && !descriptor.SupportsTyped ||
             isTyped && !descriptor.SupportsTyped))
        {
            issues.Add(new("profile.recipe_unsupported", "recipe"));
        }

        var supportsTypedDecisions = descriptor?.SupportsTyped == true;

        AddRequired(profile.Provider, "provider", issues);
        AddRequired(profile.Model, "model", issues);
        AddRequired(profile.EndpointAlias, "endpointAlias", issues);
        AddRequired(profile.ResolvedModelId, "resolvedModelId", issues);
        if (profile.CapturedAt == default)
        {
            issues.Add(new("profile.snapshot_missing", "capturedAt"));
        }

        if (descriptor is { Models.Count: > 0 } && !descriptor.Models.Contains(profile.Model ?? string.Empty))
        {
            issues.Add(new("profile.model_unsupported", "model"));
        }

        if (supportsTypedDecisions && descriptor?.HasThreshold == true)
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

        ValidateParameters(profile.Parameters, profile.Provider ?? string.Empty, descriptor, "parameters", issues);
        ValidateChoiceQuestions(profile.ChoiceQuestions, descriptor, supportsTypedDecisions, issues);

        if (isTyped)
        {
            if (profile.SummaryProfile is null)
            {
                issues.Add(new("profile.summary_profile_required", "summaryProfile"));
            }
            else
            {
                ValidateSummaryProfile(profile.SummaryProfile, issues);
            }
        }
        else if (profile.SummaryProfile is not null)
        {
            issues.Add(new("profile.summary_profile_unsupported", "summaryProfile"));
        }

        ValidateSchema(profile.Schema, "schema", issues);
        ValidatePrompt(profile.Prompt, "prompt", issues);
        return issues.AsReadOnly();
    }

    private static void ValidateSummaryProfile(SummaryModelProfile summary, List<ValidationIssue> issues)
    {
        if (summary.ContractVersion != 1)
        {
            issues.Add(new("summary.contract_version", "summaryProfile.contractVersion"));
        }

        if (!SupportedProviders.TryGetValue(summary.Provider ?? string.Empty, out var descriptor) || !descriptor.SupportsCompletion)
        {
            issues.Add(new("summary.provider_unsupported", "summaryProfile.provider"));
            descriptor = null;
        }

        AddRequired(summary.Provider, "summaryProfile.provider", issues);
        AddRequired(summary.Model, "summaryProfile.model", issues);
        AddRequired(summary.ResolvedModelId, "summaryProfile.resolvedModelId", issues);
        AddRequired(summary.EndpointAlias, "summaryProfile.endpointAlias", issues);
        if (summary.CapturedAt == default)
        {
            issues.Add(new("summary.snapshot_missing", "summaryProfile.capturedAt"));
        }

        ValidateParameters(summary.Parameters, summary.Provider ?? string.Empty, descriptor, "summaryProfile.parameters", issues);
        ValidateSchema(summary.Schema, "summaryProfile.schema", issues);
        ValidatePrompt(summary.Prompt, "summaryProfile.prompt", issues);
    }

    private static void ValidateParameters(JsonElement parameters, string provider, ProviderDescriptor? descriptor,
        string prefix, List<ValidationIssue> issues)
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
            var path = $"{prefix}.{parameter.Name}";
            if (descriptor is null || !descriptor.Parameters.Contains(parameter.Name))
            {
                issues.Add(new("profile.parameter_unsupported", path));
                continue;
            }

            var valid = provider switch
            {
                "openai" when parameter.Name == "reasoningEffort" => parameter.Value.ValueKind == JsonValueKind.String &&
                    parameter.Value.GetString() is string effort && SupportedEfforts.Contains(effort),
                "openai" when parameter.Name == "maxOutputTokens" => IsPositiveInt(parameter.Value, 1_000_000),
                "lmstudio" when parameter.Name is "arbitrationTemperature" or "generationTemperature" => IsNumberInRange(parameter.Value, 0, 2),
                "lmstudio" when parameter.Name is "arbitrationMaxTokens" or "generationMaxTokens" => IsPositiveInt(parameter.Value, 1_000_000),
                "lmstudio" when parameter.Name == "requestTimeoutSeconds" => IsTimeout(parameter.Value),
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
        bool supportsTypedDecisions,
        List<ValidationIssue> issues)
    {
        if (questions is null || questions.Count > 128)
        {
            issues.Add(new("profile.choice_questions_invalid", "choiceQuestions"));
            return;
        }

        if (!supportsTypedDecisions || descriptor?.HasChoiceQuestions != true)
        {
            if (questions.Count != 0)
            {
                issues.Add(new("profile.choice_questions_unsupported", "choiceQuestions"));
            }

            return;
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

    private static void ValidateSchema(RevisionDocument? document, string path, List<ValidationIssue> issues)
    {
        ValidateDocument(document, path, "schema", issues);
        if (document is null)
        {
            return;
        }

        if (document.Payload.ValueKind != JsonValueKind.Object ||
            !document.Payload.TryGetProperty("contractVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1)
        {
            issues.Add(new("schema.contract_version", $"{path}.contractVersion"));
        }
    }

    private static void ValidatePrompt(RevisionDocument? document, string path, List<ValidationIssue> issues) =>
        ValidateDocument(document, path, "prompt", issues);

    private static void ValidateDocument(RevisionDocument? document, string path, string expectedKind, List<ValidationIssue> issues)
    {
        if (document is null)
        {
            issues.Add(new("revision.missing", path));
            return;
        }

        try
        {
            _ = RevisionHasher.Hash(document);
            if (!string.Equals(document.Kind, expectedKind, StringComparison.Ordinal))
            {
                issues.Add(new("revision.document_kind_invalid", path));
            }
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
