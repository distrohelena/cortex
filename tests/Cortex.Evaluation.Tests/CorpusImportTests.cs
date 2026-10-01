using System.Text;
using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Storage;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class CorpusImportTests
{
    [Theory]
    [InlineData("artifact.import", "{\"kind\":\"case\",\"isHoldout\":false,\"content\":\"eA==\"}", EvaluationRole.Operator, "unauthorized")]
    [InlineData("artifact.import", "{\"kind\":\"case\",\"isHoldout\":true,\"content\":\"eA==\"}", EvaluationRole.Proposer, "unauthorized")]
    [InlineData("artifact.import", "{\"kind\":\"case\",\"content\":\"eA==\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("artifact.import", "{\"kind\":\"case\",\"isHoldout\":false,\"content\":\"\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("artifact.import", "{\"kind\":\"case\",\"isHoldout\":false,\"content\":\"not-base64\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("artifact.import", "{\"kind\":\"\",\"isHoldout\":false,\"content\":\"eA==\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("artifact.import", "{\"kind\":\"case\",\"isHoldout\":false,\"filePath\":\"C:/private\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("case.import", "{}", EvaluationRole.Operator, "unauthorized")]
    [InlineData("case.import", "{\"caseKey\":\"x\",\"artifact\":null}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("case.import", "{\"caseKey\":\"x\",\"artifact\":{\"id\":\"11111111-1111-1111-1111-111111111111\",\"sha256\":\"bad\"}}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("case.import", "{\"caseKey\":\"x\",\"artifact\":{},\"projectId\":\"foreign\"}", EvaluationRole.Evaluator, "invalid_request")]
    public async Task Import_rejects_invalid_or_unauthorized_input_before_storage(string operation, string payload, EvaluationRole role, string error)
    {
        using var source = Source();
        var request = "{\"contractVersion\":1,\"operation\":\"" + operation + "\",\"payload\":" + payload + "}";
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(role), request, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal(error, result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task Direct_store_cannot_create_holdout_as_proposer()
    {
        // A denied request must not reach the datasource at all; no connection
        // object is provided, so even the pre-fix test cannot touch a database.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new PostgresEvaluationStore(null!)
            .RegisterArtifactAsync(Access(EvaluationRole.Proposer), new("case", true, [1]), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Imported_corpus_round_trips_without_cross_scope_or_holdout_reference_bypass()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires disposable migrated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var evaluator = Access(EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("corpus-proposer", evaluator.Scope, EvaluationRole.Proposer);
        var operational = EvaluationAccess.FromHost("corpus-operator", evaluator.Scope, EvaluationRole.Operator);
        var app = new EvaluationApplication(new PersistedEvaluationTools(source));
        var store = new PostgresEvaluationStore(source);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var token = TestContext.Current.CancellationToken;
        Task<PersistedToolResponse> Call(EvaluationAccess access, string operation, object payload) => app.InvokeAsync(access,
            JsonSerializer.Serialize(new { contractVersion = 1, operation, payload }, json), token);
        var payload = new { kind = "case", isHoldout = false, content = Encoding.UTF8.GetBytes("synthetic case content") };
        var imported = await Call(proposer, "artifact.import", payload);
        Assert.True(imported.Success, imported.ErrorCode);
        var artifact = imported.Result!.Value.Deserialize<EvidenceArtifactRef>(json)!;
        Assert.DoesNotContain("synthetic case content", imported.Result.Value.GetRawText());
        var repeated = await Call(proposer, "artifact.import", payload);
        Assert.True(repeated.Success, repeated.ErrorCode);
        Assert.Equal(artifact, repeated.Result!.Value.Deserialize<EvidenceArtifactRef>(json));
        var registered = await Call(proposer, "case.import", new EvidenceCaseDocument("synthetic", artifact));
        Assert.True(registered.Success, registered.ErrorCode);
        var id = registered.Result!.Value.GetProperty("caseId").GetGuid();
        Assert.Equal(artifact, (await store.GetCaseAsync(proposer, id, token)).Document.Artifact);
        Assert.Equal(payload.content, (await store.GetArtifactAsync(proposer, artifact, token)).Document.Content.ToArray());

        var held = await Call(evaluator, "artifact.import", new { kind = "case", isHoldout = true, content = new byte[] { 1, 2, 3 } });
        Assert.True(held.Success, held.ErrorCode);
        var heldRef = held.Result!.Value.Deserialize<EvidenceArtifactRef>(json)!;
        var denied = await Call(proposer, "case.import", new EvidenceCaseDocument("holdout-bypass", heldRef));
        Assert.False(denied.Success);
        Assert.Equal("unauthorized", denied.ErrorCode);
        foreach (var scope in new[] { new EvaluationScope(Guid.NewGuid(), evaluator.Scope.ProjectId), new(evaluator.Scope.WorkspaceId, Guid.NewGuid()) })
        {
            var foreign = EvaluationAccess.FromHost("foreign", scope, EvaluationRole.Evaluator);
            var rejected = await Call(foreign, "case.import", new EvidenceCaseDocument("foreign", artifact));
            Assert.False(rejected.Success);
            Assert.Null(rejected.Result);
        }
        var allowed = await Call(evaluator, "case.import", new EvidenceCaseDocument("holdout", heldRef));
        Assert.True(allowed.Success, allowed.ErrorCode);
        await store.RedactArtifactAsync(operational, heldRef.Id, "fixture", token);
        var redacted = await Call(evaluator, "case.import", new EvidenceCaseDocument("redacted", heldRef));
        Assert.False(redacted.Success);
        Assert.Single((await store.ListCasesAsync(evaluator, 100, null, token)).Items);
        await using var count = source.CreateCommand("SELECT count(*) FROM evaluation.cases WHERE workspace_id=@workspace AND project_id=@project");
        count.Parameters.AddWithValue("workspace", evaluator.Scope.WorkspaceId);
        count.Parameters.AddWithValue("project", evaluator.Scope.ProjectId);
        Assert.Equal(2L, (long)(await count.ExecuteScalarAsync(token))!);
    }

    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
    private static EvaluationAccess Access(EvaluationRole role) => EvaluationAccess.FromHost("corpus", new(Guid.NewGuid(), Guid.NewGuid()), role);
}
