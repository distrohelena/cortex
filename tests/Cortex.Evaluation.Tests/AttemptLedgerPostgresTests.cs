using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Cortex.Evaluation.Storage;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Cortex.Evaluation.Tests;

// Requires an isolated database already migrated by its migration-owner account.
// Never selected by the offline suite; these tests have not been executed during host migration.
[Trait("Category", "Postgres")]
public sealed class AttemptLedgerPostgresTests
{
    private static readonly PhysicalCallIntent Intent = new("openai", new string('a', 64), 20);

    [Fact]
    public async Task Operator_charge_attestation_is_idempotent_preserves_raw_evidence_and_stops_overrun()
    {
        await using var fixture = await Create(3);
        var token = TestContext.Current.CancellationToken;
        var ledger = fixture.Ledger(0);
        var attempt = await ledger.BeginAsync(Intent, token);
        await ledger.FinishAsync(attempt, new("response_received", TimeSpan.FromMilliseconds(2),
            new(200, "fixture", null, "completed", "available", new(10, 10, 0, 0), null), null), token);
        var authority = EvaluationAccess.FromHost("billing-operator", fixture.Access.Scope, EvaluationRole.Operator);
        var approval = (await new MonetaryBudgetStore(fixture.Source).ReadAsync(authority, fixture.Experiment, token))!;
        var artifacts = new PostgresEvaluationStore(fixture.Source);
        var evidence = await artifacts.RegisterArtifactAsync(fixture.Access,
            new("billing-evidence", true, System.Text.Encoding.UTF8.GetBytes("synthetic receipt; not a real provider charge")), token);
        var request = new AttemptChargeDeclaration(1, Guid.NewGuid(), fixture.Experiment, fixture.Trials[0], attempt,
            fixture.Owner, 1, approval.ApprovalId, "BRL", 0.02m, evidence);
        var charges = new AttemptChargeStore(fixture.Source);
        await Assert.ThrowsAsync<InvalidDataException>(() => charges.DeclareAsync(authority,
            request with { Evidence = evidence with { Id = Guid.NewGuid() } }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => charges.DeclareAsync(authority, request with { Currency = "USD" }, token));
        var results = await Task.WhenAll(charges.DeclareAsync(authority, request, token), charges.DeclareAsync(authority, request, token));
        Assert.Single(results, r => !r.AlreadyRecorded);
        Assert.Single(results, r => r.AlreadyRecorded);
        var accountingReader = new BudgetExposureReader(fixture.Source);
        var accounting = await accountingReader.ReadAsync(authority, fixture.Experiment, token);
        Assert.NotNull(accounting);
        Assert.Equal(0.02m, accounting.Billing!.DeclaredTotal);
        Assert.Equal("BRL", accounting.Billing.Currency);
        Assert.Equal(0.02m, accounting.Accounting.ConservativeExposure.Cost);
        Assert.Equal(0.01m, accounting.Accounting.ReservedCost);
        Assert.Equal(1L, await fixture.Scalar("SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace AND state='observed' AND reserved_cost=0.01"));
        Assert.Equal(1L, await fixture.Scalar("SELECT count(*) FROM evaluation.experiments WHERE workspace_id=@workspace AND cancel_requested AND execution_state='indeterminate' AND reserved_cost=0.01"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Ledger(1).BeginAsync(Intent, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => charges.DeclareAsync(authority, request with { Charge = 0m }, token));
        await artifacts.RedactArtifactAsync(authority, evidence.Id, "fixture", token);
        Assert.True((await charges.DeclareAsync(authority, request, token)).AlreadyRecorded); // Historical receipt only.
        var withheld = (await accountingReader.ReadAsync(authority, fixture.Experiment, token))!;
        Assert.Null(withheld.Billing!.DeclaredTotal);
        Assert.Equal(1, withheld.Billing.UnavailableEvidence);
        Assert.Equal(0m, withheld.Billing.KnownDeclaredSubtotal);
        Assert.Null(Assert.Single(withheld.Charges).Charge);
        Assert.True(withheld.CancelRequested);
    }

    [Fact]
    public async Task An_unapproved_legacy_plan_cannot_start_new_physical_attempts()
    {
        await using var fixture = await Create(2, approved: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Ledger(0).BeginAsync(Intent, TestContext.Current.CancellationToken));
        Assert.Equal(0L, await fixture.Scalar("SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace"));
        Assert.Equal(0L, await fixture.Scalar("SELECT reserved_calls FROM evaluation.experiments WHERE workspace_id=@workspace"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("USD")]
    public async Task Missing_or_different_currency_never_creates_an_attempt_or_reservation(string? currency)
    {
        await using var fixture = await Create(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Ledger(0, currency).BeginAsync(Intent, TestContext.Current.CancellationToken));
        Assert.Equal(0L, await fixture.Scalar("SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace"));
        Assert.Equal(0L, await fixture.Scalar("SELECT reserved_calls FROM evaluation.experiments WHERE workspace_id=@workspace"));
        await fixture.Ledger(0).BeginAsync(Intent, TestContext.Current.CancellationToken);
        Assert.Equal(1L, await fixture.Scalar("SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace AND monetary_approval_id IS NOT NULL AND reservation_currency='BRL'"));
    }

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
        var coordinator = new ExperimentCoordinator(fixture.Source);
        var authority = EvaluationAccess.FromHost("stop-operator", fixture.Access.Scope, EvaluationRole.Operator);
        foreach (var foreign in new[]
        {
            new EvaluationScope(fixture.Access.Scope.WorkspaceId, Guid.NewGuid()),
            new EvaluationScope(Guid.NewGuid(), fixture.Access.Scope.ProjectId)
        })
            await Assert.ThrowsAsync<KeyNotFoundException>(() => coordinator.CancelAsync(
                EvaluationAccess.FromHost("stop-operator", foreign, EvaluationRole.Operator), fixture.Experiment, token));
        Assert.Equal(0L, await fixture.Scalar("SELECT count(*) FROM evaluation.audit_log WHERE workspace_id=@workspace AND action='experiment.cancel'"));
        await Task.WhenAll(coordinator.CancelAsync(authority, fixture.Experiment, token),
            coordinator.CancelAsync(authority, fixture.Experiment, token));
        Assert.Equal(1L, await fixture.Scalar("SELECT count(*) FROM evaluation.audit_log WHERE workspace_id=@workspace AND action='experiment.cancel' AND actor='stop-operator'"));
        Assert.NotNull(await new MonetaryBudgetStore(fixture.Source).ReadAsync(authority, fixture.Experiment, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Ledger(1).BeginAsync(Intent, token));
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

    private static async Task<Fixture> Create(int calls, bool approved = true)
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
                   evaluator_revision_id,evaluator_revision_sha256,policy_revision_id,policy_revision_sha256,execution_state,plan_sha256)
                VALUES (@workspace,@project,@id,@manifest,'ledger-tests',@revision,@hash,@revision,@hash,
                        @revision,@hash,@revision,@hash,@revision,@hash,'planned',@plan);
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
            command.Parameters.AddWithValue("plan", new string('a', 64));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            if (approved)
                await MonetaryTestApproval.GrantAsync(source, scope, new(fixture.Experiment, new string('a', 64), 2),
                    manifest.Budgets.MaxCost, TestContext.Current.CancellationToken);
            await fixture.Execute("UPDATE evaluation.experiments SET execution_state='running' WHERE workspace_id=@workspace");
            return fixture;
        }
        catch { await source.DisposeAsync(); throw; }
    }

    private sealed record Fixture(NpgsqlDataSource Source, EvaluationAccess Access, Guid Experiment, Guid[] Trials, Guid Owner) : IAsyncDisposable
    {
        public AttemptLedger Ledger(int index, string? currency = "BRL") =>
            new(Source, Access, Experiment, Trials[index], Owner, 1, new(100, 100, 0.01m), currency);
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
