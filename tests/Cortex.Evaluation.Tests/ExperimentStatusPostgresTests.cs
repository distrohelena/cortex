using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Cortex.Evaluation.Storage;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class ExperimentStatusPostgresTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Status_reads_scoped_consistent_metadata_without_changing_pending_work_or_reservations()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires migrated isolated PostgreSQL; never selected offline.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var evaluator = EvaluationAccess.FromHost("status-test", scope, EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("status-test", scope, EvaluationRole.Proposer);
        var store = new PostgresEvaluationStore(source);
        var token = TestContext.Current.CancellationToken;
        var reference = new ScopedRevisionRef(scope, await store.PutRevisionAsync(proposer,
            new(1, "test", JsonSerializer.SerializeToElement(new { value = 1 })), token));
        var manifest = new ExperimentManifest(scope, reference, reference, reference, reference, reference,
            [reference], 1, 1, CandidateOrder.Interleaved, new(2, 1000, 1000, 1), new(1, 1, null));
        var id = Guid.NewGuid();
        var coordinator = new ExperimentCoordinator(source);
        var plan = await coordinator.PlanAsync(evaluator, id, manifest, [reference], token);
        await MonetaryTestApproval.GrantAsync(source, scope, plan, manifest.Budgets.MaxCost, token);
        var statuses = new ExperimentStatusReader(source);
        var planned = await statuses.ReadAsync(evaluator, id, token);
        Assert.NotNull(planned);
        Assert.Equal("planned", planned.StoredState);
        Assert.Equal(1, planned.Trials.Pending);
        Assert.False(planned.AccountingComplete);
        Assert.Equal(planned, await statuses.ReadAsync(evaluator, id, token));
        Assert.Null(await statuses.ReadAsync(evaluator, Guid.NewGuid(), token));
        var foreign = EvaluationAccess.FromHost("status-test", new(scope.WorkspaceId, Guid.NewGuid()), EvaluationRole.Evaluator);
        Assert.Null(await statuses.ReadAsync(foreign, id, token));
        var foreignWorkspace = EvaluationAccess.FromHost("status-test", new(Guid.NewGuid(), scope.ProjectId), EvaluationRole.Evaluator);
        Assert.Null(await statuses.ReadAsync(foreignWorkspace, id, token));

        var claim = await coordinator.ClaimNextAsync(evaluator, id, Guid.NewGuid(), token);
        Assert.NotNull(claim);
        var ledger = new AttemptLedger(source, evaluator, id, claim.TrialId, claim.Owner, claim.Generation, new(100, 100, 0.01m), "BRL");
        var attempt = await ledger.BeginAsync(new("openai", new string('a', 64), 20), token);
        var running = await statuses.ReadAsync(evaluator, id, token);
        Assert.NotNull(running);
        Assert.True(running.AccountingPending);
        Assert.False(running.ReconciliationRequired);
        Assert.Equal(1, running.Attempts.Started);
        await coordinator.CancelAsync(evaluator, id, token);
        var cancelled = await statuses.ReadAsync(evaluator, id, token);
        Assert.NotNull(cancelled);
        Assert.True(cancelled.ReconciliationRequired);
        Assert.Equal(1, cancelled.Trials.Running);
        Assert.Equal(running.Reserved, cancelled.Reserved);
        Assert.Null(cancelled.ObservedCost);
        Assert.Equal(cancelled, await statuses.ReadAsync(evaluator, id, token));
        // A late accounting response remains valid; querying did not consume/reset its attempt.
        await ledger.FinishAsync(attempt, new("response_received", TimeSpan.FromMilliseconds(1),
            new(200, "fixture", null, "completed", "available", new(10, 10, 0, 0), null), null), token);
        var observed = await statuses.ReadAsync(evaluator, id, token);
        Assert.NotNull(observed);
        Assert.Equal(1, observed.Attempts.Observed);
        Assert.Equal(cancelled.Reserved, observed.Reserved);
        Assert.False(observed.AccountingComplete);
        var tools = new PersistedEvaluationTools(source);
        var response = await tools.InvokeAsync(evaluator, JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            operation = "experiment.status",
            payload = new { experimentId = id }
        }), token);
        Assert.True(response.Success, response.ErrorCode);
        Assert.Equal("read_only", response.PersistenceStatus);
        Assert.Equal("cancelled", response.Result!.Value.GetProperty("storedState").GetString());
        Assert.False(response.Result.Value.TryGetProperty("manifest", out _));
        Assert.False(response.Result.Value.TryGetProperty("output", out _));
    }
}
