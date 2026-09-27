using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Npgsql;
using Xunit;
using System.Text.Json;
using Filomena.Evaluation.Storage;

namespace Filomena.Evaluation.Tests;

public sealed class ExperimentCoordinatorTests
{
    [Theory]
    [InlineData("profile")]
    [InlineData("input")]
    [InlineData("artifact")]
    [InlineData("owner")]
    public async Task Invalid_result_binding_is_rejected_before_database(string mutation)
    {
        using var source = Source();
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var access = EvaluationAccess.FromHost("evaluator", scope, EvaluationRole.Evaluator);
        var reference = new ScopedRevisionRef(scope, new(Guid.NewGuid(), new string('a', 64)));
        var claim = new TrialClaim(Guid.NewGuid(), Guid.NewGuid(), 1, reference, reference, 0);
        var result = new CompletionEvaluationResult(reference.Revision.Sha256, reference.Revision.Sha256, "output", TimeSpan.Zero);
        if (mutation == "profile") result = result with { ProfileSha256 = new string('b', 64) };
        if (mutation == "input") result = result with { InputSha256 = new string('b', 64) };
        if (mutation == "owner") claim = claim with { Owner = Guid.Empty };
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(result.ToArtifact(false).Content.Span));
        var artifact = new EvidenceArtifactRef(Guid.NewGuid(), mutation == "artifact" ? new string('0', 64) : hash);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ExperimentCoordinator(source).CompleteAsync(access,
            Guid.NewGuid(), claim, result, artifact, TestContext.Current.CancellationToken));
    }

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
        var claimed = claims[0]!;
        var result = new CompletionEvaluationResult(claimed.Candidate.Revision.Sha256,
            claimed.Case.Revision.Sha256, "controlled output", TimeSpan.FromMilliseconds(5));
        var artifact = await store.RegisterArtifactAsync(evaluator, result.ToArtifact(false), token);
        // Saving an output alone cannot certify execution without an observed physical attempt.
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CompleteAsync(evaluator, id, claimed, result, artifact, token));
        var ledger = new AttemptLedger(source, evaluator, id, claimed.TrialId, claimed.Owner, claimed.Generation, new(100, 100, 0.01m));
        var attempt = await ledger.BeginAsync(new("openai", new string('a', 64), 20), token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CompleteAsync(evaluator, id, claimed, result, artifact, token));
        await ledger.FinishAsync(attempt, new("response_received", TimeSpan.FromMilliseconds(5),
            new(200, "controlled-model", null, "completed", "available", new(10, 10, 0, 0), null), null), token);
        await coordinator.CompleteAsync(evaluator, id, claimed, result, artifact, token);
        await coordinator.CompleteAsync(evaluator, id, claimed, result, artifact, token);
        var changed = result with { Output = "different output" };
        var changedArtifact = await store.RegisterArtifactAsync(evaluator, changed.ToArtifact(false), token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CompleteAsync(evaluator, id, claimed, changed, changedArtifact, token));
        await coordinator.CancelAsync(evaluator, id, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ClaimNextAsync(evaluator, id, Guid.NewGuid(), token));
        // Running work is retained, not reset to pending by another caller or cancellation.
        await using var query = source.CreateCommand("SELECT count(*) FROM evaluation.trials WHERE workspace_id=@workspace AND state='running'");
        query.Parameters.AddWithValue("workspace", scope.WorkspaceId);
        Assert.Equal(1L, (long)(await query.ExecuteScalarAsync(token))!);
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
