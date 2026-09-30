using System.Security.Cryptography;
using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Filomena.Evaluation.Storage;

/// <summary>Scope-filtered PostgreSQL persistence for evaluation evidence.</summary>
public sealed class PostgresEvaluationStore(NpgsqlDataSource dataSource)
{
    private const int MaximumArtifactBytes = 1_048_576;

    public async Task<RevisionRef> PutRevisionAsync(
        EvaluationAccess access, RevisionDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(document);
        access.Demand(EvaluationOperation.Propose);
        var hash = RevisionHasher.Hash(document);
        var json = JsonSerializer.Serialize(new { contractVersion = document.ContractVersion, kind = document.Kind, payload = document.Payload });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var scope = access.Scope;
        var id = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand(
                         "INSERT INTO evaluation.revisions (workspace_id, project_id, id, sha256, kind, document, document_bytes) " +
                         "VALUES (@workspace, @project, @id, @hash, @kind, @document::jsonb, @document_bytes) " +
                         "ON CONFLICT (workspace_id, project_id, sha256) DO NOTHING RETURNING id",
                         connection, transaction))
        {
            AddScope(insert, scope);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("hash", hash);
            insert.Parameters.AddWithValue("kind", document.Kind);
            insert.Parameters.AddWithValue("document", json);
            insert.Parameters.AddWithValue("document_bytes", bytes);
            var inserted = await insert.ExecuteScalarAsync(cancellationToken);
            if (inserted is Guid created)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(created, hash);
            }
        }

        await using var find = new NpgsqlCommand(
            "SELECT id FROM evaluation.revisions WHERE workspace_id = @workspace AND project_id = @project AND sha256 = @hash",
            connection, transaction);
        AddScope(find, scope);
        find.Parameters.AddWithValue("hash", hash);
        var existing = await find.ExecuteScalarAsync(cancellationToken);
        if (existing is not Guid existingId)
            throw new InvalidOperationException("Revision was not available after a duplicate-safe insert.");
        await transaction.CommitAsync(cancellationToken);
        return new(existingId, hash);
    }

    public async Task<RevisionDocument?> GetRevisionAsync(
        EvaluationAccess access, RevisionRef reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(reference);
        access.Demand(EvaluationOperation.ReadRevision);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT convert_from(document_bytes, 'UTF8') FROM evaluation.revisions " +
            "WHERE workspace_id = @workspace AND project_id = @project AND id = @id AND sha256 = @hash",
            connection);
        AddScope(command, access.Scope);
        command.Parameters.AddWithValue("id", reference.Id);
        command.Parameters.AddWithValue("hash", reference.Sha256);
        var json = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (json is null)
            return null;
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        var document = new RevisionDocument(root.GetProperty("contractVersion").GetInt32(),
            root.GetProperty("kind").GetString()!, root.GetProperty("payload"));
        if (!string.Equals(RevisionHasher.Hash(document), reference.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stored revision failed its content hash check.");
        return document;
    }

    public async Task<EvidenceArtifactRef> RegisterArtifactAsync(
        EvaluationAccess access, EvidenceArtifactDocument document, CancellationToken cancellationToken = default)
    {
        ValidateArtifactImport(access, document);
        var content = document.Content.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var id = Guid.NewGuid();
        var scope = access.Scope;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand(
                         "INSERT INTO evaluation.artifacts (workspace_id, project_id, id, sha256, kind, is_holdout, payload) " +
                         "VALUES (@workspace, @project, @id, @hash, @kind, @holdout, @payload) " +
                         "ON CONFLICT (workspace_id, project_id, sha256) DO NOTHING RETURNING id",
                         connection))
        {
            AddScope(insert, scope);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("hash", hash);
            insert.Parameters.AddWithValue("kind", document.Kind);
            insert.Parameters.AddWithValue("holdout", document.IsHoldout);
            insert.Parameters.AddWithValue("payload", content);
            var inserted = await insert.ExecuteScalarAsync(cancellationToken);
            if (inserted is Guid created)
                return new(created, hash);
        }

        await using var find = new NpgsqlCommand(
            "SELECT id, kind, is_holdout FROM evaluation.artifacts " +
            "WHERE workspace_id = @workspace AND project_id = @project AND sha256 = @hash AND redacted_at IS NULL",
            connection);
        AddScope(find, scope);
        find.Parameters.AddWithValue("hash", hash);
        await using var reader = await find.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetString(1) != document.Kind || reader.GetBoolean(2) != document.IsHoldout)
            throw new InvalidOperationException("Artifact content already exists with different or redacted metadata.");
        return new(reader.GetGuid(0), hash);
    }

    public async Task<StoredEvidenceArtifact> GetArtifactAsync(
        EvaluationAccess access, EvidenceArtifactRef reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(reference);
        access.Demand(EvaluationOperation.ReadArtifact);
        ValidateHash(reference.Sha256);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var metadata = new NpgsqlCommand(
            "SELECT is_holdout, redacted_at FROM evaluation.artifacts " +
            "WHERE workspace_id = @workspace AND project_id = @project AND id = @id AND sha256 = @hash",
            connection);
        AddScope(metadata, access.Scope);
        metadata.Parameters.AddWithValue("id", reference.Id);
        metadata.Parameters.AddWithValue("hash", reference.Sha256);
        await using (var reader = await metadata.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Artifact is not available in this scope.");
            access.DemandArtifactRead(reader.GetBoolean(0));
            if (!reader.IsDBNull(1))
                throw new UnauthorizedAccessException("Artifact is not available to this principal.");
        }

        await using var command = new NpgsqlCommand(
            "SELECT kind, is_holdout, payload FROM evaluation.artifacts " +
            "WHERE workspace_id = @workspace AND project_id = @project AND id = @id AND sha256 = @hash AND redacted_at IS NULL",
            connection);
        AddScope(command, access.Scope);
        command.Parameters.AddWithValue("id", reference.Id);
        command.Parameters.AddWithValue("hash", reference.Sha256);
        await using var artifactReader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await artifactReader.ReadAsync(cancellationToken))
            throw new UnauthorizedAccessException("Artifact is not available to this principal.");
        var content = (byte[])artifactReader[2];
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), reference.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stored artifact failed its content hash check.");
        return new(reference, new EvidenceArtifactDocument(artifactReader.GetString(0), artifactReader.GetBoolean(1), content));
    }

    public async Task<Guid> RegisterCaseAsync(
        EvaluationAccess access, EvidenceCaseDocument document, CancellationToken cancellationToken = default)
    {
        ValidateCaseImport(access, document);
        var scope = access.Scope;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var verify = new NpgsqlCommand(
                         "SELECT is_holdout FROM evaluation.artifacts WHERE workspace_id = @workspace AND project_id = @project " +
                         "AND id = @artifact AND sha256 = @hash AND redacted_at IS NULL",
                         connection, transaction))
        {
            AddScope(verify, scope);
            verify.Parameters.AddWithValue("artifact", document.Artifact.Id);
            verify.Parameters.AddWithValue("hash", document.Artifact.Sha256);
            if (await verify.ExecuteScalarAsync(cancellationToken) is not bool isHoldout)
                throw new KeyNotFoundException("Artifact is not available in this scope.");
            access.DemandArtifactRead(isHoldout);
        }

        var id = Guid.NewGuid();
        await using var insert = new NpgsqlCommand(
            "INSERT INTO evaluation.cases (workspace_id, project_id, id, case_key, artifact_id, artifact_sha256) " +
            "VALUES (@workspace, @project, @id, @key, @artifact, @hash)", connection, transaction);
        AddScope(insert, scope);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("key", document.CaseKey);
        insert.Parameters.AddWithValue("artifact", document.Artifact.Id);
        insert.Parameters.AddWithValue("hash", document.Artifact.Sha256);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<StoredEvidenceCase> GetCaseAsync(
        EvaluationAccess access, Guid caseId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.ReadArtifact);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT c.case_key, a.id, a.sha256, a.is_holdout, a.redacted_at " +
            "FROM evaluation.cases c JOIN evaluation.artifacts a " +
            "ON a.workspace_id = c.workspace_id AND a.project_id = c.project_id AND a.id = c.artifact_id " +
            "WHERE c.workspace_id = @workspace AND c.project_id = @project AND c.id = @id",
            connection);
        AddScope(command, access.Scope);
        command.Parameters.AddWithValue("id", caseId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new KeyNotFoundException("Case is not available in this scope.");
        access.DemandArtifactRead(reader.GetBoolean(3));
        if (!reader.IsDBNull(4))
            throw new UnauthorizedAccessException("Case is not available to this principal.");
        var reference = new EvidenceArtifactRef(reader.GetGuid(1), reader.GetString(2).Trim());
        return new(caseId, new EvidenceCaseDocument(reader.GetString(0), reference));
    }

    public async Task<EvidenceCasePage> ListCasesAsync(EvaluationAccess access, int limit, Guid? after,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.ReadArtifact);
        ValidateCasePage(limit, after);
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.id,c.case_key,c.artifact_id,c.artifact_sha256
            FROM evaluation.cases c JOIN evaluation.artifacts a
              ON a.workspace_id=c.workspace_id AND a.project_id=c.project_id
              AND a.id=c.artifact_id AND a.sha256=c.artifact_sha256
            WHERE c.workspace_id=@workspace AND c.project_id=@project
              AND a.redacted_at IS NULL AND (@holdout OR NOT a.is_holdout)
              AND (@after IS NULL OR c.id > @after)
            ORDER BY c.id LIMIT @limit
            """, connection);
        AddScope(command, access.Scope);
        command.Parameters.AddWithValue("holdout", access.CanReadHoldout);
        command.Parameters.Add("after", NpgsqlDbType.Uuid).Value = (object?)after ?? DBNull.Value;
        command.Parameters.AddWithValue("limit", limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<StoredEvidenceCase>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetGuid(0), new(reader.GetString(1), new(reader.GetGuid(2), reader.GetString(3).Trim()))));
        Guid? next = null;
        if (items.Count > limit)
        {
            items.RemoveAt(limit);
            next = items[^1].Id;
        }
        return new(items.AsReadOnly(), next);
    }

    internal static void ValidateCasePage(int limit, Guid? after)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (after == Guid.Empty) throw new ArgumentException("Invalid case cursor.", nameof(after));
    }

    public async Task<Guid> AppendAssessmentAsync(
        EvaluationAccess access, Assessment assessment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(assessment);
        access.Demand(EvaluationOperation.AppendAssessment);
        assessment = ValidateAssessment(assessment);
        var scope = access.Scope;
        var id = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand(
                         "INSERT INTO evaluation.assessments (workspace_id, project_id, id, case_id, trial_id, evaluator_principal, evaluator_revision_id, evaluator_revision_sha256, criterion, author_kind, outcome, severity, supersedes_assessment_id) " +
                         "VALUES (@workspace, @project, @id, @case, @trial, @evaluator, @evaluator_revision, @evaluator_hash, @criterion, @author_kind, @outcome, @severity, @supersedes)",
                         connection, transaction))
        {
            AddScope(insert, scope);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("case", assessment.CaseId);
            insert.Parameters.Add("trial", NpgsqlDbType.Uuid).Value = (object?)assessment.TrialId ?? DBNull.Value;
            insert.Parameters.AddWithValue("evaluator", access.Principal);
            insert.Parameters.AddWithValue("evaluator_revision", assessment.EvaluatorRevision.Id);
            insert.Parameters.AddWithValue("evaluator_hash", assessment.EvaluatorRevision.Sha256);
            insert.Parameters.AddWithValue("criterion", assessment.Criterion);
            insert.Parameters.AddWithValue("author_kind", assessment.AuthorKind);
            insert.Parameters.AddWithValue("outcome", assessment.Outcome.ToString().ToLowerInvariant());
            insert.Parameters.Add("severity", NpgsqlDbType.Integer).Value = (object?)assessment.Severity ?? DBNull.Value;
            insert.Parameters.Add("supersedes", NpgsqlDbType.Uuid).Value = (object?)assessment.SupersedesAssessmentId ?? DBNull.Value;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var evidence in assessment.Evidence)
        {
            await using var link = new NpgsqlCommand(
                "INSERT INTO evaluation.assessment_evidence (workspace_id, project_id, assessment_id, artifact_id, artifact_sha256) " +
                "VALUES (@workspace, @project, @assessment, @artifact, @hash)", connection, transaction);
            AddScope(link, scope);
            link.Parameters.AddWithValue("assessment", id);
            link.Parameters.AddWithValue("artifact", evidence.Id);
            link.Parameters.AddWithValue("hash", evidence.Sha256);
            await link.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    internal static Assessment ValidateAssessment(Assessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(assessment.EvaluatorRevision);
        if (assessment.CaseId == Guid.Empty || assessment.EvaluatorRevision.Id == Guid.Empty ||
            assessment.TrialId == Guid.Empty || assessment.SupersedesAssessmentId == Guid.Empty ||
            !Enum.IsDefined(assessment.Outcome) || assessment.Severity is < 0 or > 5 || assessment.Evidence is null ||
            string.IsNullOrWhiteSpace(assessment.Criterion) || assessment.Criterion.Length > 256 ||
            assessment.Criterion.Any(char.IsControl) || string.IsNullOrWhiteSpace(assessment.AuthorKind) ||
            assessment.AuthorKind.Length > 64 || assessment.AuthorKind.Any(char.IsControl))
            throw new ArgumentException("Assessment fields are invalid.", nameof(assessment));
        ValidateHash(assessment.EvaluatorRevision.Sha256);
        var evidence = assessment.Evidence.Distinct().ToArray();
        foreach (var item in evidence)
        {
            if (item is null || item.Id == Guid.Empty)
                throw new ArgumentException("Assessment evidence reference is invalid.", nameof(assessment));
            ValidateHash(item.Sha256);
        }
        // Freeze caller-owned collections before the first await/connection.
        return assessment with { Evidence = evidence };
    }

    public async Task RedactArtifactAsync(
        EvaluationAccess access, Guid artifactId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.RedactArtifact);
        if (artifactId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024 || reason.Any(char.IsControl))
            throw new ArgumentException("A valid artifact and redaction reason are required.", nameof(reason));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT evaluation.redact_artifact(@workspace, @project, @artifact, @actor, @reason)", connection);
        AddScope(command, access.Scope);
        command.Parameters.AddWithValue("artifact", artifactId);
        command.Parameters.AddWithValue("actor", access.Principal);
        command.Parameters.AddWithValue("reason", reason);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.MessageText == "artifact unavailable for redaction")
        {
            throw new KeyNotFoundException("Artifact is not available for redaction.");
        }
    }

    private static void AddScope(NpgsqlCommand command, EvaluationScope scope)
    {
        command.Parameters.AddWithValue("workspace", scope.WorkspaceId);
        command.Parameters.AddWithValue("project", scope.ProjectId);
    }

    internal static void ValidateArtifactImport(EvaluationAccess access, EvidenceArtifactDocument document)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(document);
        access.Demand(EvaluationOperation.RegisterCase);
        access.DemandArtifactRead(document.IsHoldout);
        ValidateKind(document.Kind);
        if (document.Content.Length is <= 0 or > MaximumArtifactBytes)
            throw new ArgumentException("Artifact payload must be between 1 byte and 1 MiB.", nameof(document));
    }

    internal static void ValidateCaseImport(EvaluationAccess access, EvidenceCaseDocument document)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(document);
        access.Demand(EvaluationOperation.RegisterCase);
        if (string.IsNullOrWhiteSpace(document.CaseKey) || document.CaseKey.Length > 256 || document.CaseKey.Any(char.IsControl))
            throw new ArgumentException("Case key is invalid.", nameof(document));
        ArgumentNullException.ThrowIfNull(document.Artifact);
        if (document.Artifact.Id == Guid.Empty)
            throw new ArgumentException("Artifact identity is required.", nameof(document));
        ValidateHash(document.Artifact.Sha256);
    }

    private static void ValidateKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > 128 || kind.Any(char.IsControl))
            throw new ArgumentException("Artifact kind is invalid.", nameof(kind));
    }

    private static void ValidateHash(string hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Artifact hash is invalid.", nameof(hash));
    }
}
