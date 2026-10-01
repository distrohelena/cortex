using System.Text;
using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Cortex.Evaluation.Storage;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class RecoveryQuarantinePostgresTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Quarantine_fences_owner_retains_uncertainty_and_is_durable_idempotent_without_retry()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires migrated isolated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var evaluator = EvaluationAccess.FromHost("recovery", scope, EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("recovery", scope, EvaluationRole.Proposer);
        var operational = EvaluationAccess.FromHost("recovery-operator", scope, EvaluationRole.Operator);
        var store = new PostgresEvaluationStore(source);
        var token = TestContext.Current.CancellationToken;
        var reference = new ScopedRevisionRef(scope, await store.PutRevisionAsync(proposer,
            new(1, "test", JsonSerializer.SerializeToElement(new { value = 1 })), token));
        var manifest = new ExperimentManifest(scope, reference, reference, reference, reference, reference,
            [reference], 2, 1, CandidateOrder.Interleaved, new(4, 1000, 1000, 1), new(1, 2, null));
        var coordinator = new ExperimentCoordinator(source);
        var id = Guid.NewGuid();
        var plan = await coordinator.PlanAsync(evaluator, id, manifest, [reference], token);
        await MonetaryTestApproval.GrantAsync(source, scope, plan, manifest.Budgets.MaxCost, token);
        var claim = await coordinator.ClaimNextAsync(evaluator, id, Guid.NewGuid(), token);
        Assert.NotNull(claim);
        var ledger = new AttemptLedger(source, evaluator, id, claim.TrialId, claim.Owner, claim.Generation, new(100, 100, 0.01m), "BRL");
        var intent = new PhysicalCallIntent("openai", new string('a', 64), 20);
        var attempt = await ledger.BeginAsync(intent, token); // Counterproof: original owner admitted normally.
        var before = await new ExperimentStatusReader(source).ReadAsync(evaluator, id, token);
        var evidence = await store.RegisterArtifactAsync(evaluator,
            new("recovery-evidence", false, Encoding.UTF8.GetBytes("controlled interrupted worker evidence")), token);
        var request = new RecoveryQuarantineRequest(id, claim.TrialId, claim.Owner, claim.Generation, Guid.NewGuid(), evidence);
        var recovery = new ExperimentRecovery(source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.QuarantineAsync(operational,
            request with { ExpectedGeneration = claim.Generation + 1 }, token));
        var foreign = EvaluationAccess.FromHost("recovery-operator", new(scope.WorkspaceId, Guid.NewGuid()), EvaluationRole.Operator);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => recovery.QuarantineAsync(foreign, request, token));
        await Assert.ThrowsAsync<InvalidDataException>(() => recovery.QuarantineAsync(operational,
            request with { Evidence = new(evidence.Id, new string('b', 64)) }, token));
        Assert.Equal(before, await new ExperimentStatusReader(source).ReadAsync(evaluator, id, token));

        var receipts = await Task.WhenAll(recovery.QuarantineAsync(operational, request, token),
            recovery.QuarantineAsync(operational, request, token));
        Assert.Single(receipts, r => !r.AlreadyRecorded);
        Assert.Single(receipts, r => r.AlreadyRecorded);
        Assert.All(receipts, r => Assert.Equal(claim.Generation + 1, r.FencedGeneration));
        await using var reopened = NpgsqlDataSource.Create(connection!);
        Assert.True((await new ExperimentRecovery(reopened).QuarantineAsync(operational, request, token)).AlreadyRecorded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.QuarantineAsync(operational,
            request with { ExpectedGeneration = claim.Generation + 1 }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.BeginAsync(intent, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.FinishAsync(attempt,
            new("indeterminate", TimeSpan.FromMilliseconds(1), null, "transport_failure"), token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ClaimNextAsync(evaluator, id, Guid.NewGuid(), token));
        var after = await new ExperimentStatusReader(reopened).ReadAsync(operational, id, token);
        Assert.NotNull(after);
        Assert.Equal(before!.Reserved, after.Reserved);
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(1, after.Trials.Pending);
        Assert.Equal(1, after.Trials.Indeterminate);
        Assert.Equal("indeterminate", after.StoredState);
        Assert.True(after.CancellationRequested);
        Assert.True(after.ReconciliationRequired);
        Assert.Null(after.ObservedCost);
        await using var query = reopened.CreateCommand("SELECT count(*) FROM evaluation.audit_log WHERE workspace_id=@workspace AND project_id=@project AND id=@decision AND action='recovery_quarantined'");
        query.Parameters.AddWithValue("workspace", scope.WorkspaceId);
        query.Parameters.AddWithValue("project", scope.ProjectId);
        query.Parameters.AddWithValue("decision", request.DecisionId);
        Assert.Equal(1L, await query.ExecuteScalarAsync(token));
    }
}
