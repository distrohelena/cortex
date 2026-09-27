using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Storage;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Filomena.Evaluation.Tests;

// Requires an isolated database already migrated by its migration-owner account.
// Never selected by the offline suite; these tests have not been executed during host migration.
[Trait("Category", "Postgres")]
public sealed class AttemptLedgerPostgresTests
{
    private static readonly PhysicalCallIntent Intent = new("openai", new string('a', 64), 20);

    [Fact]
    public async Task Concurrent_workers_cannot_both_spend_the_last_call()
    {
        await using var fixture = await Create(1);
        var token = TestContext.Current.CancellationToken;
        var first = fixture.Ledger(0);
        var second = fixture.Ledger(1);
        var results = await Task.WhenAll(Record.ExceptionAsync(() => first.BeginAsync(Intent, token)).AsTask(),
            Record.ExceptionAsync(() => second.BeginAsync(Intent, token)).AsTask());
        Assert.Single(results, error => error is null);
        Assert.Single(results, error => error is InvalidOperationException);
        Assert.Equal(1L, await fixture.Scalar("SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace"));
        Assert.Equal(1L, await fixture.Scalar("SELECT reserved_calls FROM evaluation.experiments WHERE workspace_id=@workspace"));
    }

    [Fact]
    public async Task Uncertain_attempt_survives_new_ledger_and_does_not_dispatch_again()
    {
        await using var fixture = await Create(3);
        var token = TestContext.Current.CancellationToken;
        var attempt = await fixture.Ledger(0).BeginAsync(Intent, token);
        // Simulates losing the caller after Begin: no Finish and no attempt identity in the new object.
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Ledger(0).BeginAsync(Intent, token));
        await fixture.Ledger(0).FinishAsync(attempt, new("indeterminate", TimeSpan.FromSeconds(1), null, "transport_failure"), token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Ledger(1).BeginAsync(Intent, token));
        Assert.Equal(1L, await fixture.Scalar("SELECT reserved_calls FROM evaluation.experiments WHERE workspace_id=@workspace"));
        Assert.Equal(1L, await fixture.Scalar("SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace AND state='indeterminate'"));
    }

    [Fact]
    public async Task Cancellation_does_not_suppress_accounting_but_duplicate_finish_is_rejected()
    {
        await using var fixture = await Create(2);
        var token = TestContext.Current.CancellationToken;
        var ledger = fixture.Ledger(0);
        var attempt = await ledger.BeginAsync(Intent, token);
        await fixture.Execute("UPDATE evaluation.experiments SET cancel_requested=true WHERE workspace_id=@workspace");
        var outcome = new PhysicalCallOutcome("response_received", TimeSpan.FromSeconds(1),
            new(200, "test-model", null, "completed", "available", new(10, 10, 0, 0), null), null);
        await ledger.FinishAsync(attempt, outcome, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.FinishAsync(attempt, outcome, token));
        Assert.Equal(1L, await fixture.Scalar("SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace AND state='observed'"));
        Assert.Equal(1L, await fixture.Scalar("SELECT reserved_calls FROM evaluation.experiments WHERE workspace_id=@workspace"));
    }

    [Fact]
    public async Task Obsolete_generation_cannot_finalize_the_new_owners_work()
    {
        await using var fixture = await Create(2);
        var token = TestContext.Current.CancellationToken;
        var ledger = fixture.Ledger(0);
        var attempt = await ledger.BeginAsync(Intent, token);
        await fixture.Execute("UPDATE evaluation.trials SET lease_generation=2 WHERE workspace_id=@workspace");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.FinishAsync(attempt,
            new("indeterminate", TimeSpan.Zero, null, "transport_failure"), token));
        Assert.Equal(1L, await fixture.Scalar("SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace AND state='started' AND outcome IS NULL"));
    }

    private static async Task<Fixture> Create(int calls)
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires an explicitly configured isolated PostgreSQL test database.");
        var source = NpgsqlDataSource.Create(connection!);
        try
        {
            var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
            var access = EvaluationAccess.FromHost("ledger-tests", scope, EvaluationRole.Evaluator);
            var store = new PostgresEvaluationStore(source);
            var proposer = EvaluationAccess.FromHost("ledger-tests-proposer", scope, EvaluationRole.Proposer);
            var baseline = await store.PutRevisionAsync(proposer, new(1, "test", JsonSerializer.SerializeToElement(new { value = 1 })), TestContext.Current.CancellationToken);
            var candidate = await store.PutRevisionAsync(proposer, new(1, "test", JsonSerializer.SerializeToElement(new { value = 2 })), TestContext.Current.CancellationToken);
            var reference = new ScopedRevisionRef(scope, baseline);
            var manifest = new ExperimentManifest(scope, reference, reference, reference, reference, reference,
                [new(scope, candidate)], 1, 1, CandidateOrder.Interleaved, new(calls, 1000, 1000, 1), new(1, 1, null));
            var fixture = new Fixture(source, access, Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()], Guid.NewGuid());
            await using var command = source.CreateCommand("""
                INSERT INTO evaluation.experiments
                  (workspace_id,project_id,id,manifest,created_by,target_revision_id,target_revision_sha256,
                   baseline_revision_id,baseline_revision_sha256,dataset_revision_id,dataset_revision_sha256,
                   evaluator_revision_id,evaluator_revision_sha256,policy_revision_id,policy_revision_sha256,execution_state)
                VALUES (@workspace,@project,@id,@manifest,'ledger-tests',@revision,@hash,@revision,@hash,
                        @revision,@hash,@revision,@hash,@revision,@hash,'running');
                INSERT INTO evaluation.trials (workspace_id,project_id,id,experiment_id,state,lease_owner,lease_generation)
                VALUES (@workspace,@project,@first,@id,'running',@owner,1),(@workspace,@project,@second,@id,'running',@owner,1)
                """);
            command.Parameters.AddWithValue("workspace", scope.WorkspaceId);
            command.Parameters.AddWithValue("project", scope.ProjectId);
            command.Parameters.AddWithValue("id", fixture.Experiment);
            command.Parameters.AddWithValue("revision", baseline.Id);
            command.Parameters.AddWithValue("hash", baseline.Sha256);
            command.Parameters.AddWithValue("manifest", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            command.Parameters.AddWithValue("first", fixture.Trials[0]);
            command.Parameters.AddWithValue("second", fixture.Trials[1]);
            command.Parameters.AddWithValue("owner", fixture.Owner);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            return fixture;
        }
        catch { await source.DisposeAsync(); throw; }
    }

    private sealed record Fixture(NpgsqlDataSource Source, EvaluationAccess Access, Guid Experiment, Guid[] Trials, Guid Owner) : IAsyncDisposable
    {
        public AttemptLedger Ledger(int index) => new(Source, Access, Experiment, Trials[index], Owner, 1, new(100, 100, 0.01m));
        public async Task<long> Scalar(string sql)
        {
            await using var command = Source.CreateCommand(sql);
            command.Parameters.AddWithValue("workspace", Access.Scope.WorkspaceId);
            return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }
        public async Task Execute(string sql)
        {
            await using var command = Source.CreateCommand(sql);
            command.Parameters.AddWithValue("workspace", Access.Scope.WorkspaceId);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }
}
