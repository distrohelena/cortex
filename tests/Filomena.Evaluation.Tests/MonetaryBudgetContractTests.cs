using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using System.Text.Json;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class MonetaryBudgetContractTests
{
    [Fact]
    public void Missing_monetary_ceiling_is_not_deserialized_as_an_explicit_zero()
    {
        var json = JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            approvalId = Guid.NewGuid(),
            experimentId = Guid.NewGuid(),
            planSha256 = new string('a', 64),
            currency = "BRL"
        });
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MonetaryBudgetApprovalRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Approval_binds_exact_plan_scope_currency_and_existing_limit_before_execution()
    {
        var (access, request, plan) = Fixture();
        var approved = MonetaryBudgetContract.Approve(access, request, plan);
        Assert.Equal("BRL", approved.Currency);
        Assert.Equal(plan.PlanSha256, approved.PlanSha256);
        Assert.Equal(plan.ExperimentId, approved.ExperimentId);
        Assert.Equal(10m, approved.MaximumCost);
        Assert.Equal(access.Scope, approved.Scope);
        Assert.Equal(request.ApprovalId, approved.ApprovalId);
        Assert.Equal(access.Principal, approved.ApprovedBy);
    }

    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Evaluator)]
    public void Imported_configuration_does_not_grant_monetary_authority(EvaluationRole role)
    {
        var (access, request, plan) = Fixture();
        Assert.Throws<UnauthorizedAccessException>(() => MonetaryBudgetContract.Approve(
            EvaluationAccess.FromHost("caller", access.Scope, role), request, plan));
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("experiment")]
    [InlineData("scope")]
    [InlineData("started")]
    [InlineData("reserved")]
    [InlineData("state")]
    [InlineData("ceiling")]
    public void Old_or_started_or_mismatched_plan_cannot_be_relabelled_with_a_currency(string defect)
    {
        var (access, request, plan) = Fixture();
        plan = defect switch
        {
            "revision" => plan with { PlanSha256 = new string('b', 64) },
            "experiment" => plan with { ExperimentId = Guid.NewGuid() },
            "scope" => plan with { Scope = new(Guid.NewGuid(), Guid.NewGuid()) },
            "started" => plan with { ExistingAttempts = 1 },
            "reserved" => plan with { Reservations = new(0, 0, 0, 1m) },
            "state" => plan with { ExecutionState = "running" },
            _ => plan with { MaximumCost = 5m }
        };
        if (defect == "scope")
            Assert.Throws<UnauthorizedAccessException>(() => MonetaryBudgetContract.Approve(access, request, plan));
        else
            Assert.Throws<InvalidOperationException>(() => MonetaryBudgetContract.Approve(access, request, plan));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("usd")]
    [InlineData("USDX")]
    public void Missing_or_invalid_currency_has_no_default(string? currency)
    {
        var (access, request, plan) = Fixture();
        Assert.Throws<InvalidDataException>(() => MonetaryBudgetContract.Approve(access, request with { Currency = currency! }, plan));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("approval")]
    [InlineData("experiment")]
    [InlineData("hash")]
    [InlineData("negative")]
    public void Malformed_approval_never_returns_an_approval(string defect)
    {
        var (access, request, plan) = Fixture();
        request = defect switch
        {
            "version" => request with { ContractVersion = 2 },
            "approval" => request with { ApprovalId = Guid.Empty },
            "experiment" => request with { ExperimentId = Guid.Empty },
            "hash" => request with { PlanSha256 = new string('A', 64) },
            _ => request with { MaximumCost = -1m }
        };
        Assert.Throws<InvalidDataException>(() => MonetaryBudgetContract.Approve(access, request, plan));
    }

    [Theory]
    [InlineData("calls")]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("negative")]
    [InlineData("missing")]
    public void Every_reservation_dimension_must_be_known_and_zero(string dimension)
    {
        var (access, request, plan) = Fixture();
        var reservation = dimension switch
        {
            "calls" => new BudgetConsumption(1, 0, 0, 0),
            "input" => new BudgetConsumption(0, 1, 0, 0),
            "output" => new BudgetConsumption(0, 0, 1, 0),
            "negative" => new BudgetConsumption(-1, 0, 0, 0),
            _ => null
        };
        Assert.Throws<InvalidOperationException>(() => MonetaryBudgetContract.Approve(
            access, request, plan with { Reservations = reservation! }));
    }

    private static (EvaluationAccess Access, MonetaryBudgetApprovalRequest Request, UnstartedMonetaryPlan Plan) Fixture()
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var experiment = Guid.NewGuid();
        var hash = new string('a', 64);
        return (EvaluationAccess.FromHost("budget-operator", scope, EvaluationRole.Operator),
            new(1, Guid.NewGuid(), experiment, hash, "BRL", 10m),
            new(scope, experiment, hash, "planned", 0, new(0, 0, 0, 0), 10m));
    }
}
