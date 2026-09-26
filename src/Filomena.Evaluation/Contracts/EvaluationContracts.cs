using System.Text.Json;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Collections.Frozen;

namespace Filomena.Evaluation.Contracts;

public readonly record struct EvaluationScope(Guid WorkspaceId, Guid ProjectId);

public sealed record RevisionRef(Guid Id, string Sha256);

public sealed record ScopedRevisionRef(EvaluationScope Scope, RevisionRef Revision);

public sealed record RevisionDocument
{
    public RevisionDocument(int contractVersion, string kind, JsonElement payload)
    {
        ContractVersion = contractVersion;
        Kind = kind;
        Payload = payload.ValueKind == JsonValueKind.Undefined ? payload : payload.Clone();
    }

    public int ContractVersion { get; }
    public string Kind { get; }
    public JsonElement Payload { get; }
}

public sealed record ValidationIssue(string Code, string Path);

public sealed record ChoiceQuestionProfile(string Field, string Instructions, IReadOnlyDictionary<string, string> Criteria);

public enum CandidateOrder
{
    Interleaved,
    Sequential
}

public sealed record ExperimentBudgets(int MaxCalls, int MaxInputTokens, int MaxOutputTokens, decimal MaxCost);

public sealed record ExperimentStopRules(int MinRepetitions, int MaxRepetitions, double? MaxFailureRate);

public sealed record ExperimentManifest
{
    public ExperimentManifest(
        EvaluationScope scope,
        ScopedRevisionRef target,
        ScopedRevisionRef baseline,
        ScopedRevisionRef dataset,
        ScopedRevisionRef evaluator,
        ScopedRevisionRef policy,
        IReadOnlyList<ScopedRevisionRef>? candidates,
        int repetitions,
        long seed,
        CandidateOrder order,
        ExperimentBudgets budgets,
        ExperimentStopRules stopRules)
    {
        Scope = scope;
        Target = target;
        Baseline = baseline;
        Dataset = dataset;
        Evaluator = evaluator;
        Policy = policy;
        Candidates = candidates?.ToImmutableArray() ?? default;
        Repetitions = repetitions;
        Seed = seed;
        Order = order;
        Budgets = budgets;
        StopRules = stopRules;
    }

    public EvaluationScope Scope { get; init; }
    public ScopedRevisionRef Target { get; init; }
    public ScopedRevisionRef Baseline { get; init; }
    public ScopedRevisionRef Dataset { get; init; }
    public ScopedRevisionRef Evaluator { get; init; }
    public ScopedRevisionRef Policy { get; init; }
    public ImmutableArray<ScopedRevisionRef> Candidates { get; init; }
    public int Repetitions { get; init; }
    public long Seed { get; init; }
    public CandidateOrder Order { get; init; }
    public ExperimentBudgets Budgets { get; init; }
    public ExperimentStopRules StopRules { get; init; }
}

public sealed record ConfigurationProfile
{
    public ConfigurationProfile(
        int contractVersion,
        string recipe,
        string provider,
        string model,
        string endpointAlias,
        string resolvedModelId,
        DateTimeOffset capturedAt,
        JsonElement parameters,
        double? threshold,
        IReadOnlyList<ChoiceQuestionProfile> choiceQuestions,
        RevisionDocument schema,
        RevisionDocument prompt,
        RevisionDocument? summaryProfile)
    {
        ContractVersion = contractVersion;
        Recipe = recipe;
        Provider = provider;
        Model = model;
        EndpointAlias = endpointAlias;
        ResolvedModelId = resolvedModelId;
        CapturedAt = capturedAt;
        Parameters = parameters.ValueKind == JsonValueKind.Undefined ? parameters : parameters.Clone();
        Threshold = threshold;
        ChoiceQuestions = Array.AsReadOnly((choiceQuestions ?? Array.Empty<ChoiceQuestionProfile>())
            .Select(CloneQuestion).ToArray());
        Schema = schema;
        Prompt = prompt;
        SummaryProfile = summaryProfile;
    }

    public int ContractVersion { get; }
    public string Recipe { get; }
    public string Provider { get; }
    public string Model { get; }
    public string EndpointAlias { get; }
    public string ResolvedModelId { get; }
    public DateTimeOffset CapturedAt { get; }
    public JsonElement Parameters { get; }
    public double? Threshold { get; }
    public IReadOnlyList<ChoiceQuestionProfile> ChoiceQuestions { get; }
    public RevisionDocument Schema { get; }
    public RevisionDocument Prompt { get; }
    public RevisionDocument? SummaryProfile { get; }

    private static ChoiceQuestionProfile CloneQuestion(ChoiceQuestionProfile? question)
    {
        if (question is null)
        {
            return null!;
        }

        IReadOnlyDictionary<string, string> criteria = question.Criteria is null
            ? null!
            : new ReadOnlyDictionary<string, string>(question.Criteria.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        return new ChoiceQuestionProfile(question.Field, question.Instructions, criteria);
    }
}
