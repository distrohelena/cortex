using Filomena.Evaluation.Contracts;
using Npgsql;

namespace Filomena.Evaluation.Execution;

/// <summary>Explicit operator quarantine only. Does not settle costs, complete work or authorize retry.</summary>
public sealed class ExperimentRecovery(NpgsqlDataSource source)
{
    public async Task<RecoveryQuarantineReceipt> QuarantineAsync(EvaluationAccess access,
        RecoveryQuarantineRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.Operate);
        RecoveryQuarantine.Validate(request);
        token.ThrowIfCancellationRequested();
        var reason = $"{request.ExperimentId:D}:{request.TrialId:D}:{request.ExpectedOwner:D}:{request.ExpectedGeneration}:{request.Evidence.Id:D}:{request.Evidence.Sha256}";
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        // Same lock order as attempts and completion: parent, trial, then audit insertion.
        string parentState;
        await using (var parent = Command("""
            SELECT execution_state FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """))
            parentState = await parent.ExecuteScalarAsync(token) as string
                ?? throw new KeyNotFoundException("Scoped experiment is unavailable.");

        await using (var previous = Command("""
            SELECT actor,action,reason,artifact_id FROM evaluation.audit_log
            WHERE workspace_id=@workspace AND project_id=@project AND id=@decision
            """))
        {
            await using var reader = await previous.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                if (reader.GetString(0) != access.Principal || reader.GetString(1) != "recovery_quarantined" ||
                    reader.GetString(2) != reason || reader.IsDBNull(3) || reader.GetGuid(3) != request.Evidence.Id)
                    throw new InvalidOperationException("Recovery decision identity already has different evidence.");
                // Receipt of an earlier decision, not a claim about current execution state.
                return new(request.DecisionId, request.TrialId, request.ExpectedGeneration + 1, true);
            }
        }

        long generation;
        await using (var trial = Command("""
            SELECT state,lease_owner,lease_generation FROM evaluation.trials
            WHERE workspace_id=@workspace AND project_id=@project AND id=@trial AND experiment_id=@experiment FOR UPDATE
            """))
        {
            await using var reader = await trial.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Scoped trial is unavailable.");
            generation = RecoveryQuarantine.NextGeneration(parentState, reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetInt64(2), request.ExpectedOwner, request.ExpectedGeneration);
        }

        // Operator may reference evidence metadata, not read holdout content. This is an
        // operator decision to fence work, not validation of any claimed remote outcome.
        await using (var evidence = Command("""
            SELECT id FROM evaluation.artifacts WHERE workspace_id=@workspace AND project_id=@project
              AND id=@artifact AND sha256=@hash AND kind='recovery-evidence' AND redacted_at IS NULL
            """))
            if (await evidence.ExecuteScalarAsync(token) is not Guid)
                throw new InvalidDataException("Recovery evidence is unavailable.");

        await using (var update = Command("""
            UPDATE evaluation.trials SET state='indeterminate',lease_owner=NULL,lease_generation=@next
            WHERE workspace_id=@workspace AND project_id=@project AND id=@trial AND experiment_id=@experiment;
            UPDATE evaluation.experiments SET execution_state='indeterminate',cancel_requested=true
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment;
            INSERT INTO evaluation.audit_log (workspace_id,project_id,id,actor,action,artifact_id,reason)
            VALUES (@workspace,@project,@decision,@actor,'recovery_quarantined',@artifact,@reason)
            """))
        {
            update.Parameters.AddWithValue("next", generation);
            update.Parameters.AddWithValue("actor", access.Principal);
            update.Parameters.AddWithValue("reason", reason);
            await update.ExecuteNonQueryAsync(token);
        }
        // Attempts, outcomes, outputs and every reservation remain untouched, including
        // a possibly still-running remote request. Stale workers cannot finish or re-dispatch.
        await transaction.CommitAsync(token);
        return new(request.DecisionId, request.TrialId, generation, false);

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("experiment", request.ExperimentId);
            command.Parameters.AddWithValue("trial", request.TrialId);
            command.Parameters.AddWithValue("decision", request.DecisionId);
            command.Parameters.AddWithValue("artifact", request.Evidence.Id);
            command.Parameters.AddWithValue("hash", request.Evidence.Sha256);
            return command;
        }
    }
}
