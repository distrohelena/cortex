using System.Text;
using System.Text.Json;
using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Storage;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class InvestigationHistoryPostgresTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Decision_is_scoped_append_only_idempotent_and_does_not_activate_or_change_experiment()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires migrated isolated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var token = TestContext.Current.CancellationToken;
        await using (var privileges = source.CreateCommand("""
            SELECT current_user='evaluation_app',
                   has_table_privilege(current_user,'evaluation.artifacts','UPDATE')
            """))
        {
            await using var roles = await privileges.ExecuteReaderAsync(token);
            Assert.True(await roles.ReadAsync(token));
            Assert.True(roles.GetBoolean(0), "Run this acceptance using the restricted evaluation_app role.");
            Assert.False(roles.GetBoolean(1), "Artifact mutation must not be granted to make evidence reads work.");
        }
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var evaluator = EvaluationAccess.FromHost("decision-fixture", scope, EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("decision-fixture", scope, EvaluationRole.Proposer);
        var store = new PostgresEvaluationStore(source);
        var reference = new ScopedRevisionRef(scope, await store.PutRevisionAsync(proposer,
            new(1, "test", JsonSerializer.SerializeToElement(new { value = 1 })), token));
        var manifest = new ExperimentManifest(scope, reference, reference, reference, reference, reference,
            [reference], 1, 1, CandidateOrder.Interleaved, new(4, 1000, 1000, 1), new(1, 2, null));
        var experiment = Guid.NewGuid();
        await new ExperimentCoordinator(source).PlanAsync(evaluator, experiment, manifest, [reference], token);
        var before = await new ExperimentStatusReader(source).ReadAsync(evaluator, experiment, token);
        var artifact = await store.RegisterArtifactAsync(evaluator, new("decision-evidence", true,
            Encoding.UTF8.GetBytes("controlled private evidence")), token);
        var decision = new InvestigationDecision(1, Guid.NewGuid(), experiment, "no_change",
            "Coverage remains insufficient.", "Independent holdout results become available.", [artifact]);
        var history = new InvestigationHistory(source);
        foreach (var foreign in new[] { new EvaluationScope(scope.WorkspaceId, Guid.NewGuid()), new EvaluationScope(Guid.NewGuid(), scope.ProjectId) })
            await Assert.ThrowsAsync<KeyNotFoundException>(() => history.AddAsync(
                EvaluationAccess.FromHost("decision-fixture", foreign, EvaluationRole.Evaluator), decision, token));
        await Assert.ThrowsAsync<InvalidDataException>(() => history.AddAsync(evaluator,
            decision with { Evidence = [new(artifact.Id, new string('f', 64))] }, token));
        var receipts = await Task.WhenAll(history.AddAsync(evaluator, decision, token), history.AddAsync(evaluator, decision, token));
        Assert.Single(receipts, receipt => !receipt.AlreadyRecorded);
        Assert.Single(receipts, receipt => receipt.AlreadyRecorded);
        var historyReader = new DecisionHistoryReader(source);
        var visible = await historyReader.ReadAsync(evaluator, 10, null, experiment, token);
        var visibleEntry = Assert.Single(visible.Items);
        Assert.Equal("available", visibleEntry.EvidenceStatus);
        Assert.Equal(decision.Rationale, visibleEntry.Decision!.Rationale);
        Assert.Equal(decision.ReopenCondition, visibleEntry.Decision.ReopenCondition);
        Assert.Null(visible.NextAfter);
        Assert.Empty((await historyReader.ReadAsync(evaluator, 10, null, Guid.NewGuid(), token)).Items);
        foreach (var foreign in new[] { new EvaluationScope(scope.WorkspaceId, Guid.NewGuid()), new EvaluationScope(Guid.NewGuid(), scope.ProjectId) })
            Assert.Empty((await historyReader.ReadAsync(EvaluationAccess.FromHost("reader", foreign, EvaluationRole.Evaluator),
                10, null, null, token)).Items);
        await using var reopened = NpgsqlDataSource.Create(connection!);
        Assert.True((await new InvestigationHistory(reopened).AddAsync(evaluator, decision, token)).AlreadyRecorded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => history.AddAsync(evaluator,
            decision with { Rationale = "Different claim." }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => history.AddAsync(
            EvaluationAccess.FromHost("another-evaluator", scope, EvaluationRole.Evaluator), decision, token));
        Assert.Equal(before, await new ExperimentStatusReader(source).ReadAsync(evaluator, experiment, token));
        await store.RedactArtifactAsync(EvaluationAccess.FromHost("redactor", scope, EvaluationRole.Operator), artifact.Id, "test", token);
        var unavailable = Assert.Single((await historyReader.ReadAsync(evaluator, 10, null, null, token)).Items);
        Assert.Equal(decision.DecisionId, unavailable.DecisionId);
        Assert.Equal("unavailable", unavailable.EvidenceStatus);
        Assert.Null(unavailable.Decision); // No rationale, reopening condition or evidence refs leak after redaction.
        await Assert.ThrowsAsync<InvalidDataException>(() => history.AddAsync(evaluator,
            decision with { DecisionId = Guid.NewGuid() }, token));
        // Repeating an existing decision returns its historical receipt, not renewed evidence validity.
        Assert.True((await history.AddAsync(evaluator, decision, token)).AlreadyRecorded);
        await using var command = source.CreateCommand("""
            SELECT (SELECT count(*) FROM evaluation.decisions WHERE workspace_id=@w AND project_id=@p),
                   (SELECT count(*) FROM evaluation.audit_log WHERE workspace_id=@w AND project_id=@p AND action='investigation_decision'),
                   (SELECT count(*) FROM evaluation.activation_records WHERE workspace_id=@w AND project_id=@p)
            """);
        command.Parameters.AddWithValue("w", scope.WorkspaceId);
        command.Parameters.AddWithValue("p", scope.ProjectId);
        await using var reader = await command.ExecuteReaderAsync(token);
        Assert.True(await reader.ReadAsync(token));
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(0L, reader.GetInt64(2));
        await reader.DisposeAsync();
        var secondEvidence = await store.RegisterArtifactAsync(evaluator,
            new("decision-evidence", false, Encoding.UTF8.GetBytes("second fixture")), token);
        var second = decision with { DecisionId = Guid.NewGuid(), Evidence = [secondEvidence] };
        await history.AddAsync(evaluator, second, token);
        var page = await historyReader.ReadAsync(evaluator, 1, null, null, token);
        Assert.Single(page.Items);
        Assert.NotNull(page.NextAfter);
        var next = await historyReader.ReadAsync(evaluator, 1, page.NextAfter, null, token);
        Assert.Single(next.Items);
        Assert.Null(next.NextAfter);
        Assert.Equal(new[] { decision.DecisionId, second.DecisionId }.Order(),
            page.Items.Concat(next.Items).Select(item => item.DecisionId).Order());
        Assert.Single(page.Items.Concat(next.Items), item => item.Decision is not null);
    }
}
