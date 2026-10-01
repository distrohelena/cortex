using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class BudgetReservationTests
{
    [Fact]
    public void Last_call_can_only_be_reserved_once_from_the_updated_balance()
    {
        var limit = new ExperimentBudgets(1, 100, 100, 1);
        var balance = BudgetReservation.Add(limit, new(0, 0, 0, 0), new(50, 50, 0.5m));
        Assert.Equal(1, balance.Calls);
        Assert.Throws<InvalidOperationException>(() => BudgetReservation.Add(limit, balance, new(1, 1, 0.01m)));
    }

    [Theory]
    [InlineData(101, 1, 0.1)]
    [InlineData(1, 101, 0.1)]
    [InlineData(1, 1, 1.1)]
    public void Every_budget_dimension_is_enforced(int input, int output, double cost)
    {
        Assert.Throws<InvalidOperationException>(() => BudgetReservation.Add(new(10, 100, 100, 1), new(0, 0, 0, 0), new(input, output, (decimal)cost)));
    }

    [Fact]
    public void Unknown_financial_reservation_is_not_zero()
    {
        Assert.Throws<InvalidDataException>(() => BudgetReservation.Add(new(10, 100, 100, 1), new(0, 0, 0, 0), new(1, 1, null)));
    }

    [Fact]
    public void Negative_consumption_cannot_reopen_budget()
    {
        Assert.Throws<InvalidDataException>(() => BudgetReservation.Add(new(10, 100, 100, 1), new(-1, 0, 0, 0), new(1, 1, 0.01m)));
    }
}
