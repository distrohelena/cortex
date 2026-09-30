using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Npgsql;

namespace Filomena.Evaluation.Storage;

public sealed record CaseInputBinding(Guid BindingId, Guid CaseId, EvidenceArtifactRef CaseArtifact,
    RevisionRef InputRevision, RevisionRef ProvenanceRevision);
public sealed record CaseInputBindingReceipt(Guid BindingId, bool AlreadyRecorded, string Origin);

/// <summary>An explicit evaluator declaration, not proof that a transformation was executed.</summary>
public sealed class CaseInputBindingStore(NpgsqlDataSource source)
{
    public static void Validate(CaseInputBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.BindingId == Guid.Empty || binding.CaseId == Guid.Empty || binding.CaseArtifact is null ||
            binding.InputRevision is null || binding.ProvenanceRevision is null ||
            !ValidRef(binding.CaseArtifact.Id, binding.CaseArtifact.Sha256) ||
            !ValidRef(binding.InputRevision.Id, binding.InputRevision.Sha256) ||
            !ValidRef(binding.ProvenanceRevision.Id, binding.ProvenanceRevision.Sha256))
            throw new ArgumentException("Invalid case input binding.");
    }

    public static void ValidateDocuments(CaseInputBinding binding, RevisionDocument input, RevisionDocument provenance)
    {
        Validate(binding);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(provenance);
        if (input.Kind != "completion-input" || input.Payload.ValueKind != JsonValueKind.Object ||
            provenance.Kind != "completion-input-provenance" || provenance.Payload.ValueKind != JsonValueKind.Object ||
            RevisionHasher.Hash(input) != binding.InputRevision.Sha256 ||
            RevisionHasher.Hash(provenance) != binding.ProvenanceRevision.Sha256 ||
            !provenance.Payload.TryGetProperty("inputSha256", out var hash) || hash.ValueKind != JsonValueKind.String ||
            hash.GetString() != binding.InputRevision.Sha256)
            throw new InvalidDataException("Prepared input provenance does not match its revision.");
    }

    public async Task<CaseInputBindingReceipt> AddAsync(EvaluationAccess access, CaseInputBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.Evaluate);
        Validate(binding);
        token.ThrowIfCancellationRequested();
        var store = new PostgresEvaluationStore(source);
        var input = await store.GetRevisionAsync(access, binding.InputRevision, token)
            ?? throw new KeyNotFoundException("Scoped input revision unavailable.");
        var provenance = await store.GetRevisionAsync(access, binding.ProvenanceRevision, token)
            ?? throw new KeyNotFoundException("Scoped provenance revision unavailable.");
        ValidateDocuments(binding, input, provenance);
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        // Revisions are immutable. Case/artifact availability is observed, not leased
        // against future redaction; consumers must revalidate before using the binding.
        const string matchSql = """
            SELECT case_id=@case AND case_artifact_id=@artifact AND case_artifact_sha256=@artifactHash
              AND input_revision_id=@input AND input_revision_sha256=@inputHash
              AND provenance_revision_id=@provenance AND provenance_revision_sha256=@provenanceHash
              AND declared_by=@actor AND origin='evaluator_declaration' AND contract_version=1
            FROM evaluation.case_input_bindings WHERE workspace_id=@workspace AND project_id=@project AND id=@id
            """;
        await using (var existing = Command(matchSql))
        {
            var match = await existing.ExecuteScalarAsync(token);
            if (match is false) throw new InvalidOperationException("Binding identity has different content or author.");
            if (match is true) return new(binding.BindingId, true, "evaluator_declaration");
        }
        await using (var evidence = Command("""
            SELECT c.id FROM evaluation.cases c JOIN evaluation.artifacts a
              ON a.workspace_id=c.workspace_id AND a.project_id=c.project_id
              AND a.id=c.artifact_id AND a.sha256=c.artifact_sha256
            WHERE c.workspace_id=@workspace AND c.project_id=@project AND c.id=@case
              AND a.id=@artifact AND a.sha256=@artifactHash AND a.redacted_at IS NULL
            """))
            if (await evidence.ExecuteScalarAsync(token) is not Guid)
                throw new InvalidDataException("Case artifact binding unavailable.");
        bool inserted;
        await using (var insert = Command("""
            INSERT INTO evaluation.case_input_bindings
              (workspace_id,project_id,id,contract_version,case_id,case_artifact_id,case_artifact_sha256,
               input_revision_id,input_revision_sha256,provenance_revision_id,provenance_revision_sha256,origin,declared_by)
            VALUES (@workspace,@project,@id,1,@case,@artifact,@artifactHash,@input,@inputHash,
                    @provenance,@provenanceHash,'evaluator_declaration',@actor)
            ON CONFLICT (workspace_id,project_id,id) DO NOTHING RETURNING id
            """))
            inserted = await insert.ExecuteScalarAsync(token) is Guid;
        if (!inserted)
        {
            // Under READ COMMITTED this statement observes the competing committed
            // insert after ON CONFLICT waits; different content is never overwritten.
            await using var existing = Command(matchSql);
            if (await existing.ExecuteScalarAsync(token) is not true)
                throw new InvalidOperationException("Binding identity has different content or author.");
        }
        await transaction.CommitAsync(token);
        return new(binding.BindingId, !inserted, "evaluator_declaration");

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("id", binding.BindingId);
            command.Parameters.AddWithValue("case", binding.CaseId);
            command.Parameters.AddWithValue("artifact", binding.CaseArtifact.Id);
            command.Parameters.AddWithValue("artifactHash", binding.CaseArtifact.Sha256);
            command.Parameters.AddWithValue("input", binding.InputRevision.Id);
            command.Parameters.AddWithValue("inputHash", binding.InputRevision.Sha256);
            command.Parameters.AddWithValue("provenance", binding.ProvenanceRevision.Id);
            command.Parameters.AddWithValue("provenanceHash", binding.ProvenanceRevision.Sha256);
            command.Parameters.AddWithValue("actor", access.Principal);
            return command;
        }
    }

    private static bool ValidRef(Guid id, string? hash) => id != Guid.Empty && hash is { Length: 64 } &&
        hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
