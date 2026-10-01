using Cortex.Evaluation.Execution;

namespace Cortex.Evaluation.Comparison;

public sealed record PhysicalAttemptMeasurement(Guid AttemptId, Guid TrialId, string State,
    PhysicalCallOutcome? Outcome, CostEstimate? Cost)
{
    public string? Provider { get; init; }
    public string? RequestSha256 { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
}
public sealed record TrialMeasurement(Guid TrialId, int PhysicalCalls, IReadOnlyList<Guid> AttemptIds,
    TimeSpan? PipelineElapsed, long? InputTokens, long? OutputTokens, decimal? EstimatedCost,
    string? Currency, string CostStatus, bool HasUnresolvedAttempts, bool HasFailedResponses)
{
    public IReadOnlyList<Guid> PriceSnapshotIds { get; init; } = Array.Empty<Guid>();

    // Inputs must be resolved by the scoped host reader. This pure arithmetic is not
    // evidence completeness, model-price compatibility, quality or execution authority.
    public static TrialMeasurement Aggregate(Guid trialId, TimeSpan? pipelineElapsed, IReadOnlyList<PhysicalAttemptMeasurement> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        if (trialId == Guid.Empty || pipelineElapsed < TimeSpan.Zero)
            throw new InvalidDataException("Invalid trial measurement identity or elapsed time.");
        var snapshot = attempts.ToArray();
        var ids = new HashSet<Guid>();
        foreach (var attempt in snapshot)
        {
            if (attempt is null || attempt.TrialId != trialId || attempt.AttemptId == Guid.Empty || !ids.Add(attempt.AttemptId) ||
                attempt.State is not ("observed" or "started" or "indeterminate") || attempt.Outcome?.Elapsed < TimeSpan.Zero ||
                attempt.State == "observed" && (attempt.Outcome?.Status != "response_received" || attempt.Outcome.Observation is null || attempt.Outcome.ErrorCode is not null) ||
                attempt.State == "started" && attempt.Outcome is not null ||
                attempt.State == "indeterminate" && attempt.Outcome is not null &&
                    (attempt.Outcome.Status != "indeterminate" || attempt.Outcome.Observation is not null))
                throw new InvalidDataException("Invalid or duplicated physical attempt measurement.");
            var usage = attempt.Outcome?.Observation?.Usage;
            if (usage?.InputTokens < 0 || usage?.OutputTokens < 0 || usage?.CachedInputTokens < 0 ||
                usage?.ReasoningTokens < 0 || usage?.CachedInputTokens > usage?.InputTokens || usage?.ReasoningTokens > usage?.OutputTokens)
                throw new InvalidDataException("Invalid observed token counters.");
            if (attempt.Cost is { } cost && (cost.PriceSnapshotId == Guid.Empty || cost.Currency is not { Length: 3 } ||
                cost.Currency.Any(c => c is < 'A' or > 'Z') || cost.Amount < 0 ||
                cost.Status is not ("list_price_estimate" or "unavailable") ||
                cost.Status == "list_price_estimate" && (cost.Amount is null || attempt.State != "observed") ||
                cost.Status == "unavailable" && cost.Amount is not null))
                throw new InvalidDataException("Invalid cost estimate evidence.");
        }
        var currencies = snapshot.Where(a => a.Cost is not null).Select(a => a.Cost!.Currency).Distinct(StringComparer.Ordinal).ToArray();
        decimal? total = null;
        if (snapshot.Length != 0 && currencies.Length == 1 && snapshot.All(a => a.Cost?.Status == "list_price_estimate"))
        {
            try { total = snapshot.Sum(a => a.Cost!.Amount!.Value); }
            catch (OverflowException) { /* An unrepresentable total is unknown, never zero. */ }
        }
        return new(trialId, snapshot.Length, Array.AsReadOnly(snapshot.Select(a => a.AttemptId).ToArray()), pipelineElapsed,
            SumTokens(snapshot, usage => usage.InputTokens), SumTokens(snapshot, usage => usage.OutputTokens), total,
            currencies.Length == 1 && snapshot.All(a => a.Cost is not null) ? currencies[0] : null,
            total is null ? "unavailable" : "list_price_estimate",
            snapshot.Any(a => a.State != "observed"), snapshot.Any(a => a.Outcome?.Observation?.HttpSucceeded == false))
        {
            PriceSnapshotIds = Array.AsReadOnly(snapshot.Where(a => a.Cost is not null).Select(a => a.Cost!.PriceSnapshotId).Distinct().ToArray())
        };
    }

    private static long? SumTokens(PhysicalAttemptMeasurement[] attempts, Func<ProviderTokenUsage, long?> select)
    {
        if (attempts.Length == 0) return null;
        long sum = 0;
        foreach (var attempt in attempts)
        {
            if (attempt.Outcome?.Observation?.Usage is not { } usage || select(usage) is not long value) return null;
            try { sum = checked(sum + value); }
            catch (OverflowException) { return null; }
        }
        return sum;
    }
}
