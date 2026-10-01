using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class ChargeAccountingTests
{
    [Fact]
    public void Withheld_evidence_cannot_smuggle_a_declared_amount()
    {
        Assert.Throws<InvalidDataException>(() => ChargeAccounting.Project(
            [Entry(2m) with { Status = "evidence_unavailable" }], "BRL", 1m));
    }

    [Fact]
    public void Overflow_rejects_the_projection_instead_of_returning_a_partial_total()
    {
        Assert.Throws<InvalidDataException>(() => ChargeAccounting.Project(
            [Entry(decimal.MaxValue), Entry(1m)], "BRL", 1m));
    }

    [Fact]
    public void Complete_declarations_show_attested_total_but_keep_larger_reservation_per_attempt()
    {
        var result = ChargeAccounting.Project([Entry(0.5m), Entry(2m)], "BRL", 2m);
        Assert.Equal(2.5m, result.DeclaredTotal);
        Assert.Equal(3m, result.RetainedExposureLowerBound);
        Assert.Equal("BRL", result.Currency);
        Assert.False(result.AuthorizesRefundOrRetry);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("evidence_unavailable")]
    public void Missing_or_withheld_declaration_prevents_partial_sum_becoming_final(string status)
    {
        var absent = new ChargeAccountingEntry(Guid.NewGuid(), status == "missing" ? null : Guid.NewGuid(), 1m, status, null, null);
        var result = ChargeAccounting.Project([Entry(2m), absent], "BRL", 2m);
        Assert.Null(result.DeclaredTotal);
        Assert.Equal(2m, result.KnownDeclaredSubtotal);
        Assert.Equal(3m, result.RetainedExposureLowerBound);
        Assert.Equal(status == "missing" ? 1 : 0, result.MissingDeclarations);
        Assert.Equal(status == "missing" ? 0 : 1, result.UnavailableEvidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("USD")]
    public void Declaration_currency_requires_matching_approved_currency(string? currency) =>
        Assert.Throws<InvalidDataException>(() => ChargeAccounting.Project([Entry(1m)], currency, 1m));

    [Fact]
    public void Duplicate_attempt_or_decision_cannot_double_the_total()
    {
        var first = Entry(1m);
        Assert.Throws<InvalidDataException>(() => ChargeAccounting.Project([first, first], "BRL", 2m));
        Assert.Throws<InvalidDataException>(() => ChargeAccounting.Project([first, first with { AttemptId = Guid.NewGuid() }], "BRL", 2m));
    }

    [Fact]
    public void Larger_unexplained_parent_exposure_is_preserved()
    {
        var result = ChargeAccounting.Project([Entry(2m)], "BRL", 10m);
        Assert.Equal(10m, result.RetainedExposureLowerBound);
    }

    [Fact]
    public void Historical_empty_unapproved_plan_does_not_acquire_a_currency_or_zero_bill()
    {
        var result = ChargeAccounting.Project([], null, 0);
        Assert.Null(result.Currency);
        Assert.Null(result.DeclaredTotal);
    }

    private static ChargeAccountingEntry Entry(decimal charge) => new(Guid.NewGuid(), Guid.NewGuid(), 1m, "declared", "BRL", charge);
}
