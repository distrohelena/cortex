using Cortex.Evaluation.Contracts;

namespace Cortex.Evaluation.Execution;

public sealed record AttemptAdmissionState(string ExperimentState, bool CancelRequested, string TrialState,
    Guid? LeaseOwner, long LeaseGeneration, bool HasUnresolvedAttempt);

public static class AttemptAdmission
{
    // The durable caller must load this state under the same transaction/row locks
    // used to persist the reservation. This pure policy is not a concurrency lock.
    public static BudgetConsumption Reserve(AttemptAdmissionState state, Guid owner, long generation,
        ExperimentBudgets limit, BudgetConsumption consumed, CallReservation reservation)
    {
        if (state.ExperimentState != "running" || state.TrialState != "running" ||
            state.CancelRequested || state.HasUnresolvedAttempt || owner == Guid.Empty || generation < 1 ||
            state.LeaseOwner != owner || state.LeaseGeneration != generation)
            throw new InvalidOperationException("Attempt admission requires running, uncancelled work and its current fenced owner, with no unresolved attempt.");
        return BudgetReservation.Add(limit, consumed, reservation);
    }
}
