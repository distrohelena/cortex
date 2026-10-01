using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Storage;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class CaseListingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Page_size_is_bounded_before_connecting(int limit)
    {
        using var source = Source();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new PostgresEvaluationStore(source)
            .ListCasesAsync(Access(EvaluationRole.Evaluator), limit, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Operational_role_does_not_gain_corpus_access()
    {
        using var source = Source();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new PostgresEvaluationStore(source)
            .ListCasesAsync(Access(EvaluationRole.Operator), 10, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_cursor_is_invalid_before_database()
    {
        using var source = Source();
        await Assert.ThrowsAsync<ArgumentException>(() => new PostgresEvaluationStore(source)
            .ListCasesAsync(Access(EvaluationRole.Proposer), 10, Guid.Empty, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"limit\":0}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"limit\":10,\"includeHoldout\":true}", EvaluationRole.Proposer, "invalid_request")]
    [InlineData("{\"limit\":10,\"projectId\":\"foreign\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"limit\":10}", EvaluationRole.Operator, "unauthorized")]
    public async Task Tool_uses_host_scope_and_roles_not_payload(string payload, EvaluationRole role, string error)
    {
        using var source = Source();
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(role),
            "{\"contractVersion\":1,\"operation\":\"case.list\",\"payload\":" + payload + "}", TestContext.Current.CancellationToken);
        Assert.Equal(error, result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Pagination_filters_scope_holdout_and_redaction_before_limiting()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires migrated isolated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var evaluator = Access(EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("case-list", evaluator.Scope, EvaluationRole.Proposer);
        var operational = EvaluationAccess.FromHost("case-list", evaluator.Scope, EvaluationRole.Operator);
        var store = new PostgresEvaluationStore(source);
        var token = TestContext.Current.CancellationToken;
        async Task<Guid> Add(EvaluationAccess access, string key, bool holdout, bool redact = false)
        {
            var artifact = await store.RegisterArtifactAsync(access, new("test", holdout, System.Text.Encoding.UTF8.GetBytes(key)), token);
            var id = await store.RegisterCaseAsync(access, new(key, artifact), token);
            if (redact) await store.RedactArtifactAsync(operational, artifact.Id, "fixture", token);
            return id;
        }
        var first = await Add(evaluator, "first", false);
        var second = await Add(evaluator, "second", false);
        var holdout = await Add(evaluator, "holdout", true);
        await Add(evaluator, "redacted", false, true);
        await Add(EvaluationAccess.FromHost("case-list", new(evaluator.Scope.WorkspaceId, Guid.NewGuid()), EvaluationRole.Evaluator),
            "foreign-project", false);
        await Add(EvaluationAccess.FromHost("case-list", new(Guid.NewGuid(), evaluator.Scope.ProjectId), EvaluationRole.Evaluator),
            "foreign-workspace", false);
        var page = await store.ListCasesAsync(proposer, 1, null, token);
        Assert.Single(page.Items);
        Assert.NotNull(page.NextAfter);
        var next = await store.ListCasesAsync(proposer, 1, page.NextAfter, token);
        Assert.Single(next.Items);
        Assert.Null(next.NextAfter);
        Assert.Equal(new[] { first, second }.Order(), page.Items.Concat(next.Items).Select(c => c.Id).Order());
        var allowed = await store.ListCasesAsync(evaluator, 100, null, token);
        Assert.Equal(3, allowed.Items.Count);
        Assert.Contains(allowed.Items, c => c.Id == holdout);
        Assert.Null(allowed.NextAfter);
        var tools = new PersistedEvaluationTools(source);
        var response = await tools.InvokeAsync(proposer,
            "{\"contractVersion\":1,\"operation\":\"case.list\",\"payload\":{\"limit\":100}}", token);
        Assert.True(response.Success, response.ErrorCode);
        Assert.Equal("read_only", response.PersistenceStatus);
        Assert.Equal(2, response.Result!.Value.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("holdout", response.Result.Value.GetRawText());
        Assert.DoesNotContain("redacted", response.Result.Value.GetRawText());
        Assert.DoesNotContain("foreign", response.Result.Value.GetRawText());
    }

    private static EvaluationAccess Access(EvaluationRole role) => EvaluationAccess.FromHost("case-list", new(Guid.NewGuid(), Guid.NewGuid()), role);
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
}
