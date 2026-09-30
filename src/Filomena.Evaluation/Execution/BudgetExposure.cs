namespace Filomena.Evaluation.Execution;

public sealed record AttemptBudgetEvidence(Guid AttemptId, string State, CallReservation Reservation,
    long? ObservedInputTokens, long? ObservedOutputTokens);
public sealed record BudgetExposureReport(BudgetConsumption ParentReservations, BudgetConsumption AttemptReservations,
    BudgetConsumption ConservativeExposure, long? ObservedInputTokens, long? ObservedOutputTokens,
    int UnresolvedAttempts, bool ReservationMismatch, bool ObservedTokenOverrun)
{
    public decimal ReservedCost => AttemptReservations.Cost;
    public string MonetaryStatus => "not_reconciled_currency_and_billing_unavailable";
}

public static class BudgetExposure
{
    // Read-only reconciliation of accounting evidence. Retaining a reservation is
    // not proof of a remote upper billing bound, confirmed spend or permission to retry.
    public static BudgetExposureReport Reconcile(IReadOnlyList<AttemptBudgetEvidence> attempts, BudgetConsumption parent)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Calls < 0 || parent.InputTokens < 0 || parent.OutputTokens < 0 || parent.Cost < 0)
            throw new InvalidDataException("Invalid parent reservation totals.");
        var ids = new HashSet<Guid>();
        long reservedInput = 0, reservedOutput = 0, exposureInput = 0, exposureOutput = 0;
        long? observedInput = 0, observedOutput = 0;
        decimal reservedCost = 0;
        int unresolved = 0;
        bool overrun = false;
        try
        {
            foreach (var attempt in attempts)
            {
                if (attempt is null || attempt.AttemptId == Guid.Empty || !ids.Add(attempt.AttemptId) ||
                    attempt.State is not ("observed" or "started" or "indeterminate") || attempt.Reservation is null ||
                    attempt.Reservation.InputTokens < 0 || attempt.Reservation.OutputTokens < 0 || attempt.Reservation.Cost is null or < 0 ||
                    attempt.ObservedInputTokens < 0 || attempt.ObservedOutputTokens < 0 ||
                    attempt.State != "observed" && (attempt.ObservedInputTokens is not null || attempt.ObservedOutputTokens is not null))
                    throw new InvalidDataException("Invalid or duplicated attempt accounting evidence.");
                reservedInput = checked(reservedInput + attempt.Reservation.InputTokens);
                reservedOutput = checked(reservedOutput + attempt.Reservation.OutputTokens);
                reservedCost = checked(reservedCost + attempt.Reservation.Cost.Value);
                exposureInput = checked(exposureInput + Math.Max(attempt.Reservation.InputTokens, attempt.ObservedInputTokens ?? 0));
                exposureOutput = checked(exposureOutput + Math.Max(attempt.Reservation.OutputTokens, attempt.ObservedOutputTokens ?? 0));
                observedInput = observedInput is long input && attempt.ObservedInputTokens is long currentInput ? checked(input + currentInput) : null;
                observedOutput = observedOutput is long output && attempt.ObservedOutputTokens is long currentOutput ? checked(output + currentOutput) : null;
                if (attempt.State != "observed") unresolved++;
                overrun |= attempt.ObservedInputTokens > attempt.Reservation.InputTokens || attempt.ObservedOutputTokens > attempt.Reservation.OutputTokens;
            }
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException("Accounting totals exceed the supported range; no partial report.", error);
        }
        var reservations = new BudgetConsumption(ids.Count, reservedInput, reservedOutput, reservedCost);
        return new(parent, reservations, new(Math.Max(parent.Calls, ids.Count), Math.Max(parent.InputTokens, exposureInput),
                Math.Max(parent.OutputTokens, exposureOutput), Math.Max(parent.Cost, reservedCost)),
            observedInput, observedOutput, unresolved, parent != reservations, overrun);
    }
}
