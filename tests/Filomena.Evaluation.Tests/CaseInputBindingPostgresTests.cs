using System.Text;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Storage;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class CaseInputBindingPostgresTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Explicit_binding_is_scoped_idempotent_immutable_and_never_inferred_for_another_case()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires isolated PostgreSQL with migration 005.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var token = TestContext.Current.CancellationToken;
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var evaluator = EvaluationAccess.FromHost("binding", scope, EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("binding", scope, EvaluationRole.Proposer);
        var store = new PostgresEvaluationStore(source);
        var fixture = CaseInputBindingTests.Fixture();
        var input = await store.PutRevisionAsync(proposer, fixture.Input, token);
        var provenance = await store.PutRevisionAsync(proposer, fixture.Provenance, token);
        var artifact = await store.RegisterArtifactAsync(evaluator, new("case", false, Encoding.UTF8.GetBytes("case one")), token);
        var caseId = await store.RegisterCaseAsync(evaluator, new("case-one", artifact), token);
        var wrongArtifact = await store.RegisterArtifactAsync(evaluator, new("case", false, Encoding.UTF8.GetBytes("case two")), token);
        var binding = new CaseInputBinding(Guid.NewGuid(), caseId, artifact, input, provenance);
        var bindings = new CaseInputBindingStore(source);
        await Assert.ThrowsAsync<InvalidDataException>(() => bindings.AddAsync(evaluator, binding with { CaseArtifact = wrongArtifact }, token));
        foreach (var foreign in new[] { new EvaluationScope(scope.WorkspaceId, Guid.NewGuid()), new EvaluationScope(Guid.NewGuid(), scope.ProjectId) })
            await Assert.ThrowsAsync<KeyNotFoundException>(() => bindings.AddAsync(EvaluationAccess.FromHost("binding", foreign,
                EvaluationRole.Evaluator), binding, token));
        var receipts = await Task.WhenAll(bindings.AddAsync(evaluator, binding, token), bindings.AddAsync(evaluator, binding, token));
        Assert.Single(receipts, receipt => !receipt.AlreadyRecorded);
        Assert.Single(receipts, receipt => receipt.AlreadyRecorded);
        Assert.All(receipts, receipt => Assert.Equal("evaluator_declaration", receipt.Origin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.AddAsync(evaluator, binding with { CaseArtifact = wrongArtifact }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.AddAsync(
            EvaluationAccess.FromHost("other-author", scope, EvaluationRole.Evaluator), binding, token));
        await store.RedactArtifactAsync(EvaluationAccess.FromHost("operator", scope, EvaluationRole.Operator), artifact.Id, "fixture", token);
        Assert.True((await bindings.AddAsync(evaluator, binding, token)).AlreadyRecorded); // Historical receipt only.
        await Assert.ThrowsAsync<InvalidDataException>(() => bindings.AddAsync(evaluator, binding with { BindingId = Guid.NewGuid() }, token));
        await using var query = source.CreateCommand("SELECT count(*) FROM evaluation.case_input_bindings WHERE workspace_id=@w AND project_id=@p");
        query.Parameters.AddWithValue("w", scope.WorkspaceId);
        query.Parameters.AddWithValue("p", scope.ProjectId);
        Assert.Equal(1L, await query.ExecuteScalarAsync(token));
        await using var mutation = source.CreateCommand("UPDATE evaluation.case_input_bindings SET origin='evaluator_declaration' WHERE workspace_id=@w AND project_id=@p");
        mutation.Parameters.AddWithValue("w", scope.WorkspaceId);
        mutation.Parameters.AddWithValue("p", scope.ProjectId);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync(token));
        Assert.Contains(rejected.SqlState, new[] { "42501", "P0001" }); // restricted role or immutable trigger
    }
}
