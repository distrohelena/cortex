using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class MonetaryAdmissionTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("scope")]
    [InlineData("experiment")]
    [InlineData("plan")]
    [InlineData("currency")]
    [InlineData("ceiling")]
    public void An_approval_for_another_budget_never_authorizes_a_reservation(string defect)
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var id = Guid.NewGuid();
        var hash = new string('a', 64);
        MonetaryBudgetApproval? approval = new(Guid.NewGuid(), scope, id, hash, "BRL", 1m, "operator");
        approval = defect switch
        {
            "missing" => null,
            "scope" => approval with { Scope = new(Guid.NewGuid(), Guid.NewGuid()) },
            "experiment" => approval with { ExperimentId = Guid.NewGuid() },
            "plan" => approval with { PlanSha256 = new string('b', 64) },
            "currency" => approval with { Currency = "USD" },
            _ => approval with { MaximumCost = 2m }
        };
        Assert.Throws<InvalidOperationException>(() => MonetaryBudgetContract.DemandForAttempt(
            approval, scope, id, hash, "BRL", 1m));
    }

    [Fact]
    public void Exact_stored_approval_allows_the_subsequent_reservation_check()
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var id = Guid.NewGuid();
        var hash = new string('a', 64);
        var approval = new MonetaryBudgetApproval(Guid.NewGuid(), scope, id, hash, "BRL", 1m, "operator");
        MonetaryBudgetContract.DemandForAttempt(approval, scope, id, hash, "BRL", 1m);
    }
}
