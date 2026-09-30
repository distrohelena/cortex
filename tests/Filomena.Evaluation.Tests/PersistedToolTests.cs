using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;
using System.Text.Json;

namespace Filomena.Evaluation.Tests;

public sealed class PersistedToolTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Persisted_tools_import_show_plan_and_cancel_without_provider_execution()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires migrated isolated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var tools = new PersistedEvaluationTools(source);
        var evaluator = Access();
        var proposer = EvaluationAccess.FromHost("test", evaluator.Scope, EvaluationRole.Proposer);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Task<PersistedToolResponse> Call(EvaluationAccess access, string operation, object payload) => tools.InvokeAsync(access,
            JsonSerializer.Serialize(new { contractVersion = 1, operation, payload }, json), TestContext.Current.CancellationToken);
        var document = new RevisionDocument(1, "test", JsonSerializer.SerializeToElement(new { value = 1 }));
        var imported = await Call(proposer, "revision.import", document);
        Assert.True(imported.Success, imported.ErrorCode);
        Assert.Equal("confirmed", imported.PersistenceStatus);
        var reference = imported.Result!.Value.Deserialize<RevisionRef>(json)!;
        var repeated = await Call(proposer, "revision.import", document);
        Assert.True(repeated.Success, repeated.ErrorCode);
        Assert.Equal(reference, repeated.Result!.Value.Deserialize<RevisionRef>(json));
        var shown = await Call(evaluator, "revision.show", reference);
        Assert.True(shown.Success, shown.ErrorCode);
        Assert.Equal("read_only", shown.PersistenceStatus);
        Assert.Equal(reference.Sha256, RevisionHasher.Hash(shown.Result!.Value.Deserialize<RevisionDocument>(json)!));
        var scoped = new ScopedRevisionRef(evaluator.Scope, reference);
        var manifest = new ExperimentManifest(evaluator.Scope, scoped, scoped, scoped, scoped, scoped, [scoped],
            1, 1, CandidateOrder.Interleaved, new(1, 1000, 1000, 1), new(1, 1, null));
        var experimentId = Guid.NewGuid();
        var plan = await Call(evaluator, "experiment.plan", new { experimentId, manifest, cases = new[] { scoped } });
        Assert.True(plan.Success, plan.ErrorCode);
        Assert.Equal(1, plan.Result!.Value.GetProperty("trialCount").GetInt32());
        var store = new Filomena.Evaluation.Storage.PostgresEvaluationStore(source);
        var artifact = await store.RegisterArtifactAsync(evaluator,
            new("test-evidence", false, System.Text.Encoding.UTF8.GetBytes("controlled evidence")), TestContext.Current.CancellationToken);
        var caseId = await store.RegisterCaseAsync(evaluator, new("controlled-case", artifact), TestContext.Current.CancellationToken);
        var assessment = new Assessment(caseId, null, "participation", reference, "human", AssessmentOutcome.Unscored,
            null, [artifact], null);
        var added = await Call(evaluator, "assessment.add", assessment);
        Assert.True(added.Success, added.ErrorCode);
        Assert.Equal("confirmed", added.PersistenceStatus);
        var assessmentId = added.Result!.Value.GetProperty("assessmentId").GetGuid();
        Assert.NotEqual(Guid.Empty, assessmentId);
        await using (var query = source.CreateCommand("SELECT evaluator_principal,outcome FROM evaluation.assessments WHERE workspace_id=@workspace AND project_id=@project AND id=@id"))
        {
            query.Parameters.AddWithValue("workspace", evaluator.Scope.WorkspaceId);
            query.Parameters.AddWithValue("project", evaluator.Scope.ProjectId);
            query.Parameters.AddWithValue("id", assessmentId);
            await using var reader = await query.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(evaluator.Principal, reader.GetString(0));
            Assert.Equal("unscored", reader.GetString(1));
        }
        var foreign = EvaluationAccess.FromHost("foreign", new(evaluator.Scope.WorkspaceId, Guid.NewGuid()), EvaluationRole.Evaluator);
        var crossScope = await Call(foreign, "assessment.add", assessment);
        Assert.False(crossScope.Success);
        Assert.Null(crossScope.Result);
        var cancelled = await Call(evaluator, "experiment.cancel", new { experimentId });
        Assert.True(cancelled.Success, cancelled.ErrorCode);
        Assert.Equal("confirmed", cancelled.PersistenceStatus);
    }

    [Theory]
    [InlineData("experiment.run")]
    [InlineData("promote")]
    [InlineData("deploy")]
    [InlineData("exec")]
    public async Task Unsupported_operations_never_attempt_persistence_or_execution(string operation)
    {
        using var source = Source();
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(),
            System.Text.Json.JsonSerializer.Serialize(new { contractVersion = 1, operation, payload = new { } }),
            TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("unsupported_operation", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("""{"contractVersion":1,"operation":"revision.import","role":"operator","payload":{}}""")]
    [InlineData("""{"contractVersion":1,"operation":"revision.import","payload":{"kind":"x","kind":"y"}}""")]
    [InlineData("""{"contractVersion":1,"operation":"experiment.cancel","payload":{"experimentId":"00000000-0000-0000-0000-000000000000"}}""")]
    public async Task Invalid_requests_are_rejected_without_database(string request)
    {
        using var source = Source();
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(), request, TestContext.Current.CancellationToken);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Fact]
    public async Task Evaluator_cannot_import_candidate_revision()
    {
        using var source = Source();
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(),
            """{"contractVersion":1,"operation":"revision.import","payload":{"contractVersion":1,"kind":"test","payload":{}}}""",
            TestContext.Current.CancellationToken);
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static EvaluationAccess Access() => EvaluationAccess.FromHost("test", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator);
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
}
