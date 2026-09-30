using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class MonetaryBudgetToolTests
{
    [Theory]
    [InlineData(EvaluationRole.Proposer, "unauthorized")]
    [InlineData(EvaluationRole.Evaluator, "unauthorized")]
    [InlineData(EvaluationRole.Operator, "invalid_request")]
    public async Task Charge_declaration_requires_operator_and_explicit_evidence(EvaluationRole role, string error)
    {
        var result = await Invoke(role, "attempt.charge.declare", new { });
        Assert.Equal(error, result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData(EvaluationRole.Evaluator)]
    [InlineData(EvaluationRole.Proposer)]
    public async Task Only_operator_can_record_monetary_approval(EvaluationRole role)
    {
        var result = await Invoke(role, "experiment.budget.approve", Request());
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("version")]
    [InlineData("plan")]
    [InlineData("id")]
    public async Task Invalid_approval_is_rejected_before_storage(string defect)
    {
        var request = Request();
        request = defect switch
        {
            "currency" => request with { Currency = "" },
            "version" => request with { ContractVersion = 2 },
            "plan" => request with { PlanSha256 = "" },
            _ => request with { ApprovalId = Guid.Empty }
        };
        var result = await Invoke(EvaluationRole.Operator, "experiment.budget.approve", request);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Fact]
    public async Task Approval_history_requires_operational_read_authority()
    {
        var result = await Invoke(EvaluationRole.Proposer, "experiment.budget.show", new { experimentId = Guid.NewGuid() });
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Fact]
    public async Task Reading_budget_requires_explicit_experiment()
    {
        var result = await Invoke(EvaluationRole.Operator, "experiment.budget.show", new { experimentId = Guid.Empty });
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static MonetaryBudgetApprovalRequest Request() =>
        new(1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), "BRL", 1);

    private static async Task<PersistedToolResponse> Invoke(EvaluationRole role, string operation, object payload)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        return await new PersistedEvaluationTools(source).InvokeAsync(
            EvaluationAccess.FromHost("budget", new(Guid.NewGuid(), Guid.NewGuid()), role),
            JsonSerializer.Serialize(new { contractVersion = 1, operation, payload }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);
    }
}
