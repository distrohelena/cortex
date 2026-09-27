using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Npgsql;
using Xunit;
using System.Text.Json;
using Filomena.Evaluation.Storage;

namespace Filomena.Evaluation.Tests;

public sealed class ExperimentCoordinatorTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Plan_is_idempotent_claims_are_exclusive_and_cancellation_blocks_further_claims()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires explicitly configured, migrated isolated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var proposer = EvaluationAccess.FromHost("coordinator-test", scope, EvaluationRole.Proposer);
        var evaluator = EvaluationAccess.FromHost("coordinator-test", scope, EvaluationRole.Evaluator);
        var store = new PostgresEvaluationStore(source);
        var token = TestContext.Current.CancellationToken;
        var baseline = new ScopedRevisionRef(scope, await store.PutRevisionAsync(proposer,
            new(1, "test", JsonSerializer.SerializeToElement(new { value = 1 })), token));
        var candidate = new ScopedRevisionRef(scope, await store.PutRevisionAsync(proposer,
            new(1, "test", JsonSerializer.SerializeToElement(new { value = 2 })), token));
        var manifest = new ExperimentManifest(scope, baseline, baseline, baseline, baseline, baseline,
            [candidate], 1, 1, CandidateOrder.Interleaved, new(4, 1000, 1000, 1), new(1, 1, null));
        var coordinator = new ExperimentCoordinator(source);
        var id = Guid.NewGuid();
        var receipt = await coordinator.PlanAsync(evaluator, id, manifest, [baseline], token);
        Assert.Equal(receipt, await coordinator.PlanAsync(evaluator, id, manifest, [baseline], token));
        var claims = await Task.WhenAll(coordinator.ClaimNextAsync(evaluator, id, Guid.NewGuid(), token),
            coordinator.ClaimNextAsync(evaluator, id, Guid.NewGuid(), token));
        Assert.All(claims, claim => Assert.NotNull(claim));
        Assert.NotEqual(claims[0]!.TrialId, claims[1]!.TrialId);
        Assert.Null(await coordinator.ClaimNextAsync(evaluator, id, Guid.NewGuid(), token));
        await coordinator.CancelAsync(evaluator, id, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ClaimNextAsync(evaluator, id, Guid.NewGuid(), token));
        // Running work is retained, not reset to pending by another caller or cancellation.
        await using var query = source.CreateCommand("SELECT count(*) FROM evaluation.trials WHERE workspace_id=@workspace AND state='running'");
        query.Parameters.AddWithValue("workspace", scope.WorkspaceId);
        Assert.Equal(2L, (long)(await query.ExecuteScalarAsync(token))!);
    }

    [Fact]
    public async Task Proposer_cannot_plan_paid_work_even_with_valid_scope()
    {
        using var source = Source();
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var access = EvaluationAccess.FromHost("proposer", scope, EvaluationRole.Proposer);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ExperimentCoordinator(source).PlanAsync(
            access, Guid.NewGuid(), Manifest(scope), [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cross_project_manifest_is_rejected_before_database()
    {
        using var source = Source();
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var access = EvaluationAccess.FromHost("evaluator", scope, EvaluationRole.Evaluator);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ExperimentCoordinator(source).PlanAsync(
            access, Guid.NewGuid(), Manifest(new(Guid.NewGuid(), Guid.NewGuid())), [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_schedule_cannot_create_experiment()
    {
        using var source = Source();
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var access = EvaluationAccess.FromHost("evaluator", scope, EvaluationRole.Evaluator);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ExperimentCoordinator(source).PlanAsync(
            access, Guid.NewGuid(), Manifest(scope), [], TestContext.Current.CancellationToken));
    }

    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
    private static ExperimentManifest Manifest(EvaluationScope scope)
    {
        var reference = new ScopedRevisionRef(scope, new(Guid.NewGuid(), new string('a', 64)));
        return new(scope, reference, reference, reference, reference, reference, [reference], 1, 1,
            CandidateOrder.Interleaved, new(10, 1000, 1000, 1), new(1, 1, null));
    }
}
