using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Npgsql;
using Xunit;
using Cortex.Evaluation.Storage;
using System.Text.Json;

namespace Cortex.Evaluation.Tests;

public sealed class FailedTrialTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Executor_failure_stops_experiment_without_attempt_or_automatic_reclaim()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires migrated isolated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var access = Access();
        var proposer = EvaluationAccess.FromHost("test", access.Scope, EvaluationRole.Proposer);
        var store = new PostgresEvaluationStore(source);
        var token = TestContext.Current.CancellationToken;
        // Persisted but wrong-kind material must fail, not get sent to a provider.
        var reference = new ScopedRevisionRef(access.Scope, await store.PutRevisionAsync(proposer,
            new(1, "test", JsonSerializer.SerializeToElement(new { value = 1 })), token));
        var manifest = new ExperimentManifest(access.Scope, reference, reference, reference, reference, reference,
            [reference], 2, 1, CandidateOrder.Interleaved, new(4, 1000, 1000, 1), new(1, 2, null));
        var id = Guid.NewGuid();
        var coordinator = new ExperimentCoordinator(source);
        await coordinator.PlanAsync(access, id, manifest, [reference], token);
        var error = await Assert.ThrowsAsync<CompletionTrialExecutionException>(() =>
            new CompletionTrialExecutor(source).ExecuteNextAsync(access, id, [], token));
        Assert.Equal("execution_failed", error.Code);
        Assert.True(error.StopRecorded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ClaimNextAsync(access, id, Guid.NewGuid(), token));
        await using var query = source.CreateCommand("""
            SELECT (SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace),
              (SELECT count(*) FROM evaluation.trials WHERE workspace_id=@workspace AND state='indeterminate'),
              (SELECT count(*) FROM evaluation.trials WHERE workspace_id=@workspace AND state='pending'),
              (SELECT reserved_calls FROM evaluation.experiments WHERE workspace_id=@workspace AND id=@experiment)
            """);
        query.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
        query.Parameters.AddWithValue("experiment", id);
        await using var reader = await query.ExecuteReaderAsync(token);
        Assert.True(await reader.ReadAsync(token));
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(0L, reader.GetInt64(3));
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("experiment")]
    [InlineData("owner")]
    [InlineData("generation")]
    public async Task Invalid_failure_report_is_rejected_before_database(string mutation)
    {
        using var source = Source();
        var access = Access();
        var claim = Claim(access.Scope);
        if (mutation == "owner") claim = claim with { Owner = Guid.Empty };
        if (mutation == "generation") claim = claim with { Generation = 0 };
        await Assert.ThrowsAsync<InvalidDataException>(() => new ExperimentCoordinator(source).StopFailedTrialAsync(
            access, mutation == "experiment" ? Guid.Empty : Guid.NewGuid(), claim,
            mutation == "reason" ? "exception text with private data" : "execution_failed", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("candidate")]
    [InlineData("case")]
    public async Task Unauthorized_failure_report_is_rejected_before_database(string mutation)
    {
        using var source = Source();
        var access = Access(mutation == "role" ? EvaluationRole.Proposer : EvaluationRole.Evaluator);
        var claim = Claim(access.Scope);
        var foreign = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        if (mutation == "candidate") claim = claim with { Candidate = claim.Candidate with { Scope = foreign } };
        if (mutation == "case") claim = claim with { Case = claim.Case with { Scope = foreign } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ExperimentCoordinator(source).StopFailedTrialAsync(
            access, Guid.NewGuid(), claim, "execution_failed", TestContext.Current.CancellationToken));
    }

    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
    private static EvaluationAccess Access(EvaluationRole role = EvaluationRole.Evaluator) =>
        EvaluationAccess.FromHost("test", new(Guid.NewGuid(), Guid.NewGuid()), role);
    private static TrialClaim Claim(EvaluationScope scope)
    {
        var reference = new ScopedRevisionRef(scope, new(Guid.NewGuid(), new string('a', 64)));
        return new(Guid.NewGuid(), Guid.NewGuid(), 1, reference, reference, 0);
    }
}
