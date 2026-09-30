using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Storage;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class MonetaryBudgetPostgresTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Approval_is_scoped_immutable_idempotent_and_cannot_relabel_claimed_work()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires disposable PostgreSQL with migration 007; not selected offline.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var token = TestContext.Current.CancellationToken;
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var proposer = EvaluationAccess.FromHost("budget-test", scope, EvaluationRole.Proposer);
        var evaluator = EvaluationAccess.FromHost("budget-test", scope, EvaluationRole.Evaluator);
        var authority = EvaluationAccess.FromHost("budget-test", scope, EvaluationRole.Operator);
        var reference = new ScopedRevisionRef(scope, await new PostgresEvaluationStore(source).PutRevisionAsync(proposer,
            new(1, "test", JsonSerializer.SerializeToElement(new { value = 1 })), token));
        var manifest = new ExperimentManifest(scope, reference, reference, reference, reference, reference,
            [reference], 1, 1, CandidateOrder.Interleaved, new(2, 1000, 1000, 1), new(1, 1, null));
        var coordinator = new ExperimentCoordinator(source);
        var plan = await coordinator.PlanAsync(evaluator, Guid.NewGuid(), manifest, [reference], token);
        var request = new MonetaryBudgetApprovalRequest(1, Guid.NewGuid(), plan.ExperimentId, plan.PlanSha256, "BRL", 1);
        var store = new MonetaryBudgetStore(source);
        Assert.Null(await store.ReadAsync(authority, plan.ExperimentId, token));
        var receipts = await Task.WhenAll(store.ApproveAsync(authority, request, token), store.ApproveAsync(authority, request, token));
        Assert.Single(receipts, r => !r.AlreadyRecorded);
        Assert.Single(receipts, r => r.AlreadyRecorded);
        var saved = await store.ReadAsync(evaluator, plan.ExperimentId, token);
        Assert.NotNull(saved);
        Assert.Equal("BRL", saved.Currency);
        Assert.Equal(plan.PlanSha256, saved.PlanSha256);
        foreach (var foreign in new[] { new EvaluationScope(scope.WorkspaceId, Guid.NewGuid()), new EvaluationScope(Guid.NewGuid(), scope.ProjectId) })
        {
            var foreignAccess = EvaluationAccess.FromHost("budget-test", foreign, EvaluationRole.Operator);
            Assert.Null(await store.ReadAsync(foreignAccess, plan.ExperimentId, token));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ApproveAsync(foreignAccess, request, token));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(authority, request with { Currency = "USD" }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(authority, request with { ApprovalId = Guid.NewGuid() }, token));
        Assert.NotNull(await coordinator.ClaimNextAsync(evaluator, plan.ExperimentId, Guid.NewGuid(), token));
        Assert.True((await store.ApproveAsync(authority, request, token)).AlreadyRecorded); // Receipt, never renewal.
        var second = await coordinator.PlanAsync(evaluator, Guid.NewGuid(), manifest, [reference], token);
        Assert.NotNull(await coordinator.ClaimNextAsync(evaluator, second.ExperimentId, Guid.NewGuid(), token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(authority,
            request with { ApprovalId = Guid.NewGuid(), ExperimentId = second.ExperimentId, PlanSha256 = second.PlanSha256 }, token));

        // Competing admission and approval share the experiment lock. Either order
        // is legal, but a successful approval must have happened before the claim.
        var racing = await coordinator.PlanAsync(evaluator, Guid.NewGuid(), manifest, [reference], token);
        var racingRequest = request with { ApprovalId = Guid.NewGuid(), ExperimentId = racing.ExperimentId, PlanSha256 = racing.PlanSha256 };
        var approvalTask = Record.ExceptionAsync(async () => { await store.ApproveAsync(authority, racingRequest, token); }).AsTask();
        var claimTask = coordinator.ClaimNextAsync(evaluator, racing.ExperimentId, Guid.NewGuid(), token);
        await Task.WhenAll(approvalTask, claimTask);
        Assert.NotNull(await claimTask);
        var approvalError = await approvalTask;
        if (approvalError is null)
            Assert.NotNull(await store.ReadAsync(authority, racing.ExperimentId, token));
        else
        {
            Assert.IsType<InvalidOperationException>(approvalError);
            Assert.Null(await store.ReadAsync(authority, racing.ExperimentId, token));
        }
        await using var mutation = source.CreateCommand("UPDATE evaluation.monetary_budget_approvals SET currency='USD' WHERE workspace_id=@w AND project_id=@p");
        mutation.Parameters.AddWithValue("w", scope.WorkspaceId);
        mutation.Parameters.AddWithValue("p", scope.ProjectId);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync(token));
        Assert.Contains(rejected.SqlState, new[] { "42501", "P0001" });
    }
}
