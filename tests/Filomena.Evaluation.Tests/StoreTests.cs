using System.Text;
using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Storage;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class StoreTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Revision_round_trips_immutably_and_duplicate_put_is_safe()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithStore(async (store, access, dataSource, token) =>
        {
            var revision = Document("{\"prompt\":\"hello\",\"number\":1e1}");
            var reference = await store.PutRevisionAsync(access, revision, token);
            var duplicate = await store.PutRevisionAsync(access, revision, token);
            var loaded = await store.GetRevisionAsync(access, reference, token);

            Assert.Equal(reference, duplicate);
            Assert.NotNull(loaded);
            Assert.Equal(RevisionHasher.Hash(revision), RevisionHasher.Hash(loaded));

            await using var connection = await dataSource.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand(
                "UPDATE evaluation.revisions SET kind = 'mutated' WHERE workspace_id = @workspace AND project_id = @project AND id = @id",
                connection);
            AddScope(command, access.Scope);
            command.Parameters.AddWithValue("id", reference.Id);
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        }, EvaluationRole.Proposer, cancellationToken);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Scoped_case_registration_rejects_artifact_from_another_scope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithStore(async (store, access, dataSource, token) =>
        {
            var otherAccess = HostAccess(NewScope(), EvaluationRole.Evaluator);
            var artifact = await store.RegisterArtifactAsync(otherAccess, Artifact("case-data", false, "payload"), token);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RegisterCaseAsync(access,
                new EvidenceCaseDocument("foreign", artifact), token));

            await using var connection = await dataSource.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM evaluation.cases WHERE workspace_id = @workspace AND project_id = @project", connection);
            AddScope(command, access.Scope);
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(token))!);
        }, cancellationToken: cancellationToken);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Assessment_supersession_appends_and_preserves_previous_record()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithStore(async (store, access, dataSource, token) =>
        {
            var artifact = await store.RegisterArtifactAsync(access, Artifact("test-case", false, "case"), token);
            var evidenceCase = await store.RegisterCaseAsync(access, new EvidenceCaseDocument("case-1", artifact), token);
            var evaluatorRevision = await store.PutRevisionAsync(
                HostAccess(access.Scope, EvaluationRole.Proposer), Document("{\"rubric\":\"v1\"}"), token);
            var original = await store.AppendAssessmentAsync(access,
                AssessmentFor(evidenceCase, evaluatorRevision, "accuracy", null, AssessmentOutcome.Fail, 3, null), token);
            var replacement = await store.AppendAssessmentAsync(access,
                AssessmentFor(evidenceCase, evaluatorRevision, "accuracy", null, AssessmentOutcome.Pass, 0, original), token);

            await using var connection = await dataSource.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM evaluation.assessments WHERE workspace_id = @workspace AND project_id = @project AND id = ANY(@ids)", connection);
            AddScope(command, access.Scope);
            command.Parameters.AddWithValue("ids", new[] { original, replacement });
            Assert.Equal(2L, (long)(await command.ExecuteScalarAsync(token))!);
        }, cancellationToken: cancellationToken);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Assessment_cannot_supersede_an_unrelated_criterion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithStore(async (store, access, _, token) =>
        {
            var artifact = await store.RegisterArtifactAsync(access, Artifact("test-case", false, "case"), token);
            var evidenceCase = await store.RegisterCaseAsync(access, new EvidenceCaseDocument("case-1", artifact), token);
            var evaluatorRevision = await store.PutRevisionAsync(
                HostAccess(access.Scope, EvaluationRole.Proposer), Document("{\"rubric\":\"v1\"}"), token);
            var original = await store.AppendAssessmentAsync(access,
                AssessmentFor(evidenceCase, evaluatorRevision, "accuracy", null, AssessmentOutcome.Fail, 3, null), token);

            await Assert.ThrowsAsync<PostgresException>(() => store.AppendAssessmentAsync(access,
                AssessmentFor(evidenceCase, evaluatorRevision, "helpfulness", null, AssessmentOutcome.Pass, 0, original), token));
        }, cancellationToken: cancellationToken);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Proposer_cannot_read_holdout_artifact_registered_by_evaluator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithStore(async (store, proposer, _, token) =>
        {
            var evaluator = HostAccess(proposer.Scope, EvaluationRole.Evaluator);
            var artifact = await store.RegisterArtifactAsync(evaluator, Artifact("holdout", true, "secret case bytes"), token);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetArtifactAsync(proposer, artifact, token));
            var readable = await store.GetArtifactAsync(evaluator, artifact, token);
            Assert.Equal("secret case bytes", Encoding.UTF8.GetString(readable.Document.Content.Span));
        }, cancellationToken: cancellationToken);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Operator_redaction_is_audited_and_artifact_cannot_be_read_afterward()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithStore(async (store, access, dataSource, token) =>
        {
            var artifact = await store.RegisterArtifactAsync(access, Artifact("private", false, "sensitive bytes"), token);
            var operatorAccess = HostAccess(access.Scope, EvaluationRole.Operator);
            await store.RedactArtifactAsync(operatorAccess, artifact.Id, "retention request", token);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetArtifactAsync(access, artifact, token));
            await using var connection = await dataSource.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand(
                "SELECT a.payload IS NULL AND a.redacted_at IS NOT NULL AND (SELECT count(*) FROM evaluation.audit_log l WHERE l.workspace_id = a.workspace_id AND l.project_id = a.project_id AND l.artifact_id = a.id AND l.actor = @actor AND l.reason = @reason) = 1 FROM evaluation.artifacts a WHERE a.workspace_id = @workspace AND a.project_id = @project AND a.id = @id",
                connection);
            AddScope(command, access.Scope);
            command.Parameters.AddWithValue("actor", operatorAccess.Principal);
            command.Parameters.AddWithValue("reason", "retention request");
            command.Parameters.AddWithValue("id", artifact.Id);
            Assert.True((bool)(await command.ExecuteScalarAsync(token))!);
        }, cancellationToken: cancellationToken);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Application_connection_cannot_create_schema_objects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = RequiredConnectionString();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("CREATE TABLE evaluation.store_test_ddl_forbidden (id integer)", connection);

        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(cancellationToken));
    }

    private static async Task WithStore(
        Func<PostgresEvaluationStore, EvaluationAccess, NpgsqlDataSource, CancellationToken, Task> test,
        EvaluationRole role = EvaluationRole.Evaluator,
        CancellationToken cancellationToken = default)
    {
        var connectionString = RequiredConnectionString();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var access = HostAccess(NewScope(), role);
        await test(new PostgresEvaluationStore(dataSource), access, dataSource, cancellationToken);
    }

    private static string RequiredConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Skip("Set EVALUATION_TEST_DB to an isolated evaluation test database to run PostgreSQL store tests.");
        return connectionString!;
    }

    private static EvaluationScope NewScope() => new(Guid.NewGuid(), Guid.NewGuid());
    private static EvaluationAccess HostAccess(EvaluationScope scope, EvaluationRole role) =>
        EvaluationAccess.FromHost($"store-tests-{Guid.NewGuid():N}", scope, role);

    private static EvidenceArtifactDocument Artifact(string kind, bool holdout, string content) =>
        new(kind, holdout, Encoding.UTF8.GetBytes(content));

    private static RevisionDocument Document(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        return new(1, "prompt", parsed.RootElement);
    }

    private static Assessment AssessmentFor(Guid caseId, RevisionRef evaluatorRevision, string criterion,
        Guid? trial, AssessmentOutcome outcome, int? severity, Guid? supersedes) =>
        new(caseId, trial, criterion, evaluatorRevision, "human", outcome, severity, [], supersedes);

    private static void AddScope(NpgsqlCommand command, EvaluationScope scope)
    {
        command.Parameters.AddWithValue("workspace", scope.WorkspaceId);
        command.Parameters.AddWithValue("project", scope.ProjectId);
    }
}
