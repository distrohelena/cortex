using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Cortex.Evaluation.Tools;
using Npgsql;
using System.Text.Json;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class RecoveryQuarantineTests
{
    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Evaluator)]
    public async Task Recovery_requires_operator_authority_before_connection(EvaluationRole role)
    {
        using var source = Source();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ExperimentRecovery(source).QuarantineAsync(
            Access(role), Request(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("experiment")]
    [InlineData("trial")]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("overflow")]
    [InlineData("decision")]
    [InlineData("evidence")]
    [InlineData("hash")]
    public async Task Invalid_recovery_reference_cannot_touch_store(string mutation)
    {
        using var source = Source();
        var request = Request();
        request = mutation switch
        {
            "experiment" => request with { ExperimentId = Guid.Empty },
            "trial" => request with { TrialId = Guid.Empty },
            "owner" => request with { ExpectedOwner = Guid.Empty },
            "generation" => request with { ExpectedGeneration = 0 },
            "overflow" => request with { ExpectedGeneration = long.MaxValue },
            "decision" => request with { DecisionId = Guid.Empty },
            "evidence" => request with { Evidence = null! },
            "hash" => request with { Evidence = new(Guid.NewGuid(), "invalid") },
            _ => throw new ArgumentException()
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => new ExperimentRecovery(source).QuarantineAsync(
            Access(EvaluationRole.Operator), request, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("running", "running", true)]
    [InlineData("cancelled", "running", true)]
    [InlineData("indeterminate", "indeterminate", true)]
    [InlineData("completed", "completed", false)]
    [InlineData("running", "pending", false)]
    [InlineData("future", "running", false)]
    public void Only_an_existing_started_owner_can_be_fenced(string experiment, string trial, bool allowed)
    {
        var owner = Guid.NewGuid();
        if (allowed) Assert.Equal(8, RecoveryQuarantine.NextGeneration(experiment, trial, owner, 7, owner, 7));
        else Assert.Throws<InvalidOperationException>(() => RecoveryQuarantine.NextGeneration(experiment, trial, owner, 7, owner, 7));
        Assert.Throws<InvalidOperationException>(() => RecoveryQuarantine.NextGeneration(experiment, trial, Guid.NewGuid(), 7, owner, 7));
        Assert.Throws<InvalidOperationException>(() => RecoveryQuarantine.NextGeneration(experiment, trial, owner, 8, owner, 7));
    }

    [Fact]
    public async Task Tool_cannot_claim_operator_role_from_payload()
    {
        using var source = Source();
        var json = JsonSerializer.Serialize(new { contractVersion = 1, operation = "experiment.quarantine", payload = Request() },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(EvaluationRole.Evaluator), json, TestContext.Current.CancellationToken);
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static RecoveryQuarantineRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        Guid.NewGuid(), new(Guid.NewGuid(), new string('a', 64)));
    private static EvaluationAccess Access(EvaluationRole role) => EvaluationAccess.FromHost("recovery-test", new(Guid.NewGuid(), Guid.NewGuid()), role);
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
}
