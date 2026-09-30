using Filomena.Evaluation.Execution;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class BudgetExposureTests
{
    [Fact]
    public void Known_usage_below_reservation_does_not_refund_or_relabel_money_as_billed()
    {
        var result = BudgetExposure.Reconcile([Call(10, 20)], new(1, 100, 100, 0.01m));
        Assert.Equal(10, result.ObservedInputTokens);
        Assert.Equal(20, result.ObservedOutputTokens);
        Assert.Equal(100, result.ConservativeExposure.InputTokens);
        Assert.Equal(100, result.ConservativeExposure.OutputTokens);
        Assert.Equal(0.01m, result.ReservedCost);
        Assert.Equal("not_reconciled_currency_and_billing_unavailable", result.MonetaryStatus);
        Assert.False(result.ReservationMismatch);
        Assert.False(result.ObservedTokenOverrun);
    }

    [Fact]
    public void Observed_overrun_raises_conservative_exposure_without_hiding_original_reservation()
    {
        var result = BudgetExposure.Reconcile([Call(130, 20)], new(1, 100, 100, 0.01m));
        Assert.True(result.ObservedTokenOverrun);
        Assert.Equal(130, result.ConservativeExposure.InputTokens);
        Assert.Equal(100, result.AttemptReservations.InputTokens);
    }

    [Fact]
    public void Unknown_attempt_keeps_reservation_and_partial_usage_does_not_become_total()
    {
        var pending = Call(null, null) with { State = "indeterminate" };
        var result = BudgetExposure.Reconcile([Call(10, 20), pending], new(2, 200, 200, 0.02m));
        Assert.Null(result.ObservedInputTokens);
        Assert.Null(result.ObservedOutputTokens);
        Assert.Equal(200, result.ConservativeExposure.InputTokens);
        Assert.Equal(1, result.UnresolvedAttempts);
    }

    [Fact]
    public void Parent_mismatch_is_visible_and_does_not_authorize_repair()
    {
        var result = BudgetExposure.Reconcile([Call(10, 20)], new(0, 0, 0, 0));
        Assert.True(result.ReservationMismatch);
        Assert.Equal(1, result.AttemptReservations.Calls);
        Assert.Equal(0, result.ParentReservations.Calls);
    }

    [Fact]
    public void Unexplained_parent_reservation_is_not_dropped_from_exposure()
    {
        var result = BudgetExposure.Reconcile([Call(10, 20)], new(2, 300, 400, 0.5m));
        Assert.True(result.ReservationMismatch);
        Assert.Equal(new BudgetConsumption(2, 300, 400, 0.5m), result.ConservativeExposure);
    }

    [Fact]
    public void Repeated_attempt_or_negative_usage_is_rejected()
    {
        var call = Call(10, 20);
        Assert.Throws<InvalidDataException>(() => BudgetExposure.Reconcile([call, call], new(2, 200, 200, 0.02m)));
        Assert.Throws<InvalidDataException>(() => BudgetExposure.Reconcile([Call(-1, 20)], new(1, 100, 100, 0.01m)));
    }

    [Fact]
    public void Summation_overflow_does_not_emit_a_plausible_partial_total()
    {
        Assert.Throws<InvalidDataException>(() => BudgetExposure.Reconcile([Call(long.MaxValue, 20), Call(1, 20)], new(2, 200, 200, 0.02m)));
    }

    private static AttemptBudgetEvidence Call(long? input, long? output) =>
        new(Guid.NewGuid(), "observed", new(100, 100, 0.01m), input, output);
}
