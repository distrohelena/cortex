using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Filomena.Evaluation.Comparison;

public sealed record InvestigationDecision(int ContractVersion, Guid DecisionId, Guid ExperimentId,
    string Outcome, string Rationale, string ReopenCondition, IReadOnlyList<EvidenceArtifactRef> Evidence);
public sealed record DecisionReceipt(Guid DecisionId, bool AlreadyRecorded);

/// <summary>Append-only investigative decisions. Never activation, settlement or execution authority.</summary>
public sealed class InvestigationHistory(NpgsqlDataSource source)
{
    public static InvestigationDecision Validate(InvestigationDecision input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ContractVersion != 1 || input.DecisionId == Guid.Empty || input.ExperimentId == Guid.Empty ||
            input.Outcome is not ("no_change" or "investigate" or "recommend_candidate") ||
            !ValidText(input.Rationale) || !ValidText(input.ReopenCondition) ||
            input.Evidence is null || input.Evidence.Count is < 1 or > 64)
            throw new ArgumentException("Invalid investigation decision.");
        var evidence = input.Evidence.ToArray();
        var ids = new HashSet<Guid>();
        foreach (var item in evidence)
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.Sha256 is null ||
                item.Sha256.Length != 64 || item.Sha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new ArgumentException("Invalid decision evidence.");
        // Evidence is a set; normalize order for idempotent decision content.
        return input with { Evidence = evidence.OrderBy(e => e.Id).ToArray() };
    }

    public async Task<DecisionReceipt> AddAsync(EvaluationAccess access, InvestigationDecision input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.Evaluate);
        var decision = Validate(input);
        token.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(decision);
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var parent = Command("""
            SELECT id FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """))
            if (await parent.ExecuteScalarAsync(token) is not Guid)
                throw new KeyNotFoundException("Scoped experiment is unavailable.");

        await using (var existing = Command("""
            SELECT experiment_id=@experiment AND decided_by=@actor AND decision=@body
            FROM evaluation.decisions WHERE workspace_id=@workspace AND project_id=@project AND id=@decision
            """))
        {
            var match = await existing.ExecuteScalarAsync(token);
            if (match is false) throw new InvalidOperationException("Decision identity has different content or author.");
            // Historical receipt only: not renewed evidence availability or permission to activate.
            if (match is true) return new(decision.DecisionId, true);
        }

        foreach (var reference in decision.Evidence)
        {
            await using var evidence = Command("""
                SELECT id FROM evaluation.artifacts WHERE workspace_id=@workspace AND project_id=@project
                AND id=@artifact AND sha256=@hash AND redacted_at IS NULL
                """);
            evidence.Parameters.AddWithValue("artifact", reference.Id);
            evidence.Parameters.AddWithValue("hash", reference.Sha256);
            if (await evidence.ExecuteScalarAsync(token) is not Guid)
                throw new InvalidDataException("Scoped decision evidence is unavailable.");
        }
        // Evidence was readable at validation, not leased against future redaction.
        // Row locks would require artifact UPDATE permission, which the app must not
        // receive. History reads recheck availability before exposing derived content.
        await using (var insert = Command("""
            INSERT INTO evaluation.decisions (workspace_id,project_id,id,experiment_id,decision,decided_by)
            VALUES (@workspace,@project,@decision,@experiment,@body,@actor);
            INSERT INTO evaluation.audit_log (workspace_id,project_id,id,actor,action,reason)
            VALUES (@workspace,@project,@audit,@actor,'investigation_decision',@reason)
            """))
        {
            insert.Parameters.AddWithValue("audit", Guid.NewGuid());
            insert.Parameters.AddWithValue("reason", decision.DecisionId.ToString("D"));
            await insert.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return new(decision.DecisionId, false);

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("experiment", decision.ExperimentId);
            command.Parameters.AddWithValue("decision", decision.DecisionId);
            command.Parameters.AddWithValue("actor", access.Principal);
            command.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, json);
            return command;
        }
    }

    private static bool ValidText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 &&
        !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
}
