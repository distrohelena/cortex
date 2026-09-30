using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class ExperimentStatusTests
{
    [Fact]
    public void Completed_requires_consistent_accounting_not_only_stored_state()
    {
        var status = Summarize("completed", new(0, 0, 2, 0, 0), new(0, 2, 0, 0));
        Assert.True(status.AccountingComplete);
        Assert.False(status.ReconciliationRequired);
        Assert.Equal("not_assessed", status.QualityStatus);
        Assert.Null(status.ObservedCost);
        Assert.Equal(0.02m, status.Reserved.Cost);
    }

    [Theory]
    [InlineData("started")]
    [InlineData("indeterminate")]
    [InlineData("unknown")]
    [InlineData("missing")]
    public void Completed_with_unresolved_or_missing_attempts_never_certifies_completion(string kind)
    {
        var attempts = kind switch
        {
            "started" => new AttemptStateCounts(1, 0, 0, 0),
            "indeterminate" => new(0, 0, 1, 0),
            "unknown" => new(0, 0, 0, 1),
            _ => new(0, 0, 0, 0)
        };
        var status = Summarize("completed", new(0, 0, 1, 0, 0), attempts);
        Assert.False(status.AccountingComplete);
        Assert.True(status.ReconciliationRequired);
    }

    [Fact]
    public void Cancelled_work_keeps_pending_accounting_and_reservations_visible()
    {
        var status = Summarize("cancelled", new(1, 1, 0, 0, 0), new(1, 0, 0, 0), true);
        Assert.Equal("cancelled", status.StoredState);
        Assert.Equal(1, status.Attempts.Started);
        Assert.Equal(1, status.Trials.Pending);
        Assert.True(status.ReconciliationRequired);
        Assert.False(status.AccountingComplete);
        Assert.Equal(2, status.Reserved.Calls);
        Assert.Null(status.ObservedCost);
    }

    [Fact]
    public void Multiple_attempts_on_one_trial_cannot_cover_another_trial_without_evidence()
    {
        var status = Summarize("completed", new(0, 0, 2, 0, 0) { CompletedWithoutObservedAttempt = 1 }, new(0, 2, 0, 0));
        Assert.False(status.AccountingComplete);
        Assert.True(status.ReconciliationRequired);
    }

    [Fact]
    public void Running_call_is_pending_not_automatically_a_crash()
    {
        var status = Summarize("running", new(0, 1, 0, 0, 0), new(1, 0, 0, 0));
        Assert.False(status.AccountingComplete);
        Assert.False(status.ReconciliationRequired);
        Assert.True(status.AccountingPending);
    }

    [Fact]
    public void Future_state_remains_visible_and_requires_reconciliation()
    {
        var status = Summarize("future", new(0, 0, 0, 0, 1), new(0, 0, 0, 1));
        Assert.Equal("future", status.StoredState);
        Assert.True(status.ReconciliationRequired);
        Assert.False(status.AccountingComplete);
    }

    [Theory]
    [InlineData("trials")]
    [InlineData("attempts")]
    [InlineData("reservation")]
    public void Invalid_counts_are_not_reported_as_valid_status(string kind)
    {
        Assert.Throws<InvalidDataException>(() => ExperimentStatus.Create(Guid.NewGuid(), "planned", false,
            kind == "trials" ? new(-1, 0, 0, 0, 0) : new(1, 0, 0, 0, 0),
            kind == "attempts" ? new(-1, 0, 0, 0) : new(0, 0, 0, 0),
            kind == "reservation" ? new(-1, 0, 0, 0) : new(0, 0, 0, 0)));
    }

    [Fact]
    public async Task Proposer_cannot_read_experiment_status_or_holdout_schedule()
    {
        using var source = Source();
        var access = Access(EvaluationRole.Proposer);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ExperimentStatusReader(source)
            .ReadAsync(access, Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(EvaluationRole.Evaluator)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Empty_id_rejected_before_connection_for_authorized_roles(EvaluationRole role)
    {
        using var source = Source();
        await Assert.ThrowsAsync<InvalidDataException>(() => new ExperimentStatusReader(source)
            .ReadAsync(Access(role), Guid.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Tool_status_requires_host_authority_before_database()
    {
        using var source = Source();
        var request = System.Text.Json.JsonSerializer.Serialize(new { contractVersion = 1, operation = "experiment.status", payload = new { experimentId = Guid.NewGuid() } });
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(EvaluationRole.Proposer),
            request, TestContext.Current.CancellationToken);
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Fact]
    public async Task Tool_status_rejects_payload_scope_instead_of_treating_it_as_authority()
    {
        using var source = Source();
        var request = System.Text.Json.JsonSerializer.Serialize(new { contractVersion = 1, operation = "experiment.status", payload = new { experimentId = Guid.NewGuid(), projectId = Guid.NewGuid() } });
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(EvaluationRole.Evaluator),
            request, TestContext.Current.CancellationToken);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static ExperimentStatus Summarize(string state, TrialStateCounts trials, AttemptStateCounts attempts,
        bool cancelled = false) => ExperimentStatus.Create(Guid.NewGuid(), state, cancelled, trials, attempts, new(2, 100, 200, 0.02m));
    private static EvaluationAccess Access(EvaluationRole role) => EvaluationAccess.FromHost("status-test", new(Guid.NewGuid(), Guid.NewGuid()), role);
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
}
