using System.Text.Json;
using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Storage;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class TrialMeasurementPostgresTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Reader_includes_all_attempts_preserves_unknown_cost_and_redaction_without_mutation()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires migrated isolated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var token = TestContext.Current.CancellationToken;
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var access = EvaluationAccess.FromHost("measure", scope, EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("measure", scope, EvaluationRole.Proposer);
        var store = new PostgresEvaluationStore(source);
        var reference = new ScopedRevisionRef(scope, await store.PutRevisionAsync(proposer,
            new(1, "test", JsonSerializer.SerializeToElement(new { value = 1 })), token));
        var manifest = new ExperimentManifest(scope, reference, reference, reference, reference, reference,
            [reference], 1, 1, CandidateOrder.Interleaved, new(3, 1000, 1000, 1), new(1, 1, null));
        var experiment = Guid.NewGuid();
        var coordinator = new ExperimentCoordinator(source);
        var plan = await coordinator.PlanAsync(access, experiment, manifest, [reference], token);
        await MonetaryTestApproval.GrantAsync(source, scope, plan, manifest.Budgets.MaxCost, token);
        var claim = await coordinator.ClaimNextAsync(access, experiment, Guid.NewGuid(), token);
        Assert.NotNull(claim);
        var ledger = new AttemptLedger(source, access, experiment, claim.TrialId, claim.Owner, claim.Generation, new(100, 100, 0.1m), "BRL");
        var reader = new TrialMeasurementReader(source);
        var empty = await reader.ReadAsync(access, experiment, claim.TrialId, token);
        Assert.NotNull(empty);
        Assert.Equal(0, empty.Measurement.PhysicalCalls);
        Assert.Null(empty.Measurement.InputTokens);
        Assert.Null(empty.Measurement.EstimatedCost);
        var first = await ledger.BeginAsync(new("jev", new string('a', 64), 20), token);
        await ledger.FinishAsync(first, Outcome(), token);
        var second = await ledger.BeginAsync(new("lmstudio", new string('b', 64), 20), token);
        var pending = await reader.ReadAsync(access, experiment, claim.TrialId, token);
        Assert.True(pending!.Measurement.HasUnresolvedAttempts);
        Assert.Equal(2, pending.Measurement.PhysicalCalls);
        Assert.Null(pending.Measurement.InputTokens);
        await ledger.FinishAsync(second, Outcome(), token);
        var result = new CompletionEvaluationResult(claim.Candidate.Revision.Sha256, claim.Case.Revision.Sha256,
            "private response must not appear in tool output", TimeSpan.FromMilliseconds(150));
        var artifact = await store.RegisterArtifactAsync(access, result.ToArtifact(true), token);
        await coordinator.CompleteAsync(access, experiment, claim, result, artifact, token);
        var statusReader = new ExperimentStatusReader(source);
        var before = await statusReader.ReadAsync(access, experiment, token);
        var snapshot = await reader.ReadAsync(access, experiment, claim.TrialId, token);
        Assert.NotNull(snapshot);
        Assert.Equal("available", snapshot.ResultEvidenceStatus);
        Assert.Equal(TimeSpan.FromMilliseconds(150), snapshot.Measurement.PipelineElapsed);
        Assert.Equal(new[] { first, second }, snapshot.Measurement.AttemptIds);
        Assert.Equal(20, snapshot.Measurement.InputTokens);
        Assert.Null(snapshot.Measurement.EstimatedCost); // Reserved 0.2 is not an observed price.
        Assert.Equal(before, await statusReader.ReadAsync(access, experiment, token));
        Assert.Null(await reader.ReadAsync(access, Guid.NewGuid(), claim.TrialId, token));
        foreach (var foreign in new[] { new EvaluationScope(scope.WorkspaceId, Guid.NewGuid()), new EvaluationScope(Guid.NewGuid(), scope.ProjectId) })
            Assert.Null(await reader.ReadAsync(EvaluationAccess.FromHost("measure", foreign, EvaluationRole.Evaluator), experiment, claim.TrialId, token));
        var response = await new PersistedEvaluationTools(source).InvokeAsync(access, JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            operation = "trial.measurement",
            payload = new { experimentId = experiment, trialId = claim.TrialId }
        }), token);
        Assert.True(response.Success, response.ErrorCode);
        Assert.Equal("read_only", response.PersistenceStatus);
        Assert.DoesNotContain("private response", response.Result!.Value.GetRawText());
        await store.RedactArtifactAsync(EvaluationAccess.FromHost("operator", scope, EvaluationRole.Operator), artifact.Id, "fixture", token);
        var redacted = await reader.ReadAsync(access, experiment, claim.TrialId, token);
        Assert.Equal("unavailable", redacted!.ResultEvidenceStatus);
        Assert.Null(redacted.Measurement.PipelineElapsed);
        Assert.Equal(2, redacted.Measurement.PhysicalCalls);
    }

    private static PhysicalCallOutcome Outcome() => new("response_received", TimeSpan.FromMilliseconds(100),
        new(200, "test-model", null, null, "available", new(10, 10, 0, 0), null), null);
}
