namespace Cortex.Evaluation.Execution;

public sealed record TrialStateCounts(long Pending, long Running, long Completed, long Indeterminate, long Other)
{
    public long CompletedWithoutObservedAttempt { get; init; }
}
public sealed record AttemptStateCounts(long Started, long Observed, long Indeterminate, long Other);

/// <summary>Read-only accounting evidence, not an execution grant or a quality verdict.</summary>
public sealed record ExperimentStatus
{
    private ExperimentStatus() { }

    public required Guid ExperimentId { get; init; }
    public required string StoredState { get; init; }
    public required bool CancellationRequested { get; init; }
    public required TrialStateCounts Trials { get; init; }
    public required AttemptStateCounts Attempts { get; init; }
    public required BudgetConsumption Reserved { get; init; }
    public required bool AccountingPending { get; init; }
    public required bool AccountingComplete { get; init; }
    public required bool ReconciliationRequired { get; init; }
    public string QualityStatus => "not_assessed";
    // Reservation is a bound, not observed usage, estimated price or a confirmed bill.
    public decimal? ObservedCost => null;

    public static ExperimentStatus Create(Guid experimentId, string state, bool cancelled,
        TrialStateCounts trials, AttemptStateCounts attempts, BudgetConsumption reserved)
    {
        ArgumentNullException.ThrowIfNull(trials);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(reserved);
        if (experimentId == Guid.Empty || string.IsNullOrWhiteSpace(state) ||
            trials.Pending < 0 || trials.Running < 0 || trials.Completed < 0 || trials.Indeterminate < 0 || trials.Other < 0 ||
            trials.CompletedWithoutObservedAttempt < 0 || trials.CompletedWithoutObservedAttempt > trials.Completed ||
            attempts.Started < 0 || attempts.Observed < 0 || attempts.Indeterminate < 0 || attempts.Other < 0 ||
            reserved.Calls < 0 || reserved.InputTokens < 0 || reserved.OutputTokens < 0 || reserved.Cost < 0)
            throw new InvalidDataException("Invalid experiment status metadata.");

        var pending = attempts.Started > 0 || attempts.Indeterminate > 0 || attempts.Other > 0;
        var complete = state == "completed" && !pending && trials.Completed > 0 &&
            trials.Pending == 0 && trials.Running == 0 && trials.Indeterminate == 0 && trials.Other == 0 &&
            trials.CompletedWithoutObservedAttempt == 0 && attempts.Observed >= trials.Completed && attempts.Observed == reserved.Calls;
        var reconcile = state is not ("planned" or "running" or "completed" or "cancelled") ||
            trials.Indeterminate > 0 || trials.Other > 0 || attempts.Indeterminate > 0 || attempts.Other > 0 ||
            state == "completed" && !complete || (cancelled || state == "cancelled") && (pending || trials.Running > 0);
        return new()
        {
            ExperimentId = experimentId,
            StoredState = state,
            CancellationRequested = cancelled,
            Trials = trials,
            Attempts = attempts,
            Reserved = reserved,
            AccountingPending = pending,
            AccountingComplete = complete,
            ReconciliationRequired = reconcile
        };
    }
}
