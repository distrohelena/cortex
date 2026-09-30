using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Filomena.Evaluation.Execution;

public sealed record AttemptChargeReceipt(Guid DecisionId, bool AlreadyRecorded, string Origin);

/// <summary>Append-only operator billing attestation; no refund, output recovery or retry authorization.</summary>
public sealed class AttemptChargeStore(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AttemptChargeReceipt> DeclareAsync(EvaluationAccess access, AttemptChargeDeclaration request, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Operate);
        AttemptChargePolicy.Validate(request);
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        bool cancelled;
        await using (var parent = Command("""
            SELECT cancel_requested FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """))
            cancelled = await parent.ExecuteScalarAsync(token) is bool value ? value
                : throw new KeyNotFoundException("Scoped experiment unavailable.");
        await using (var existing = Command("""
            SELECT id, declaration::text, declared_by FROM evaluation.attempt_charge_declarations
            WHERE workspace_id=@workspace AND project_id=@project AND (id=@decision OR attempt_id=@attempt)
            """))
        {
            await using var reader = await existing.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                if (reader.GetGuid(0) != request.DecisionId || reader.GetString(2) != access.Principal ||
                    JsonSerializer.Deserialize<AttemptChargeDeclaration>(reader.GetString(1), Json) != request)
                    throw new InvalidOperationException("Billing declaration conflicts with immutable history.");
                return new(request.DecisionId, true, "operator_billing_attestation");
            }
        }
        long currentGeneration;
        string trialState;
        await using (var trial = Command("""
            SELECT state,lease_generation FROM evaluation.trials
            WHERE workspace_id=@workspace AND project_id=@project AND id=@trial AND experiment_id=@experiment FOR UPDATE
            """))
        {
            await using var reader = await trial.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Scoped trial unavailable.");
            trialState = reader.GetString(0);
            currentGeneration = reader.GetInt64(1);
        }
        ChargeAttemptBinding binding;
        await using (var attempt = Command("""
            SELECT lease_owner,lease_generation,monetary_approval_id,reservation_currency,state,reserved_cost
            FROM evaluation.attempts
            WHERE workspace_id=@workspace AND project_id=@project AND id=@attempt AND trial_id=@trial FOR UPDATE
            """))
        {
            await using var reader = await attempt.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Scoped attempt unavailable.");
            binding = new(request.ExperimentId, request.TrialId, request.AttemptId, reader.GetGuid(0), reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetDecimal(5))
            { ExecutionQuarantined = cancelled && trialState == "indeterminate" && currentGeneration > reader.GetInt64(1) };
        }
        var effect = AttemptChargePolicy.Evaluate(request, binding);
        // Metadata check only: the operator attests the billing interpretation.
        // Do not label this a machine-verified receipt or expose private evidence bytes.
        await using (var evidence = Command("""
            SELECT id FROM evaluation.artifacts WHERE workspace_id=@workspace AND project_id=@project
              AND id=@evidence AND sha256=@hash AND kind='billing-evidence' AND redacted_at IS NULL
            """))
            if (await evidence.ExecuteScalarAsync(token) is not Guid)
                throw new InvalidDataException("Scoped billing evidence unavailable.");
        await using (var insert = Command("""
            INSERT INTO evaluation.attempt_charge_declarations
              (workspace_id,project_id,id,experiment_id,trial_id,attempt_id,declaration,evidence_id,evidence_sha256,declared_by)
            VALUES (@workspace,@project,@decision,@experiment,@trial,@attempt,@declaration,@evidence,@hash,@actor)
            """))
        {
            insert.Parameters.AddWithValue("declaration", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request, Json));
            insert.Parameters.AddWithValue("actor", access.Principal);
            await insert.ExecuteNonQueryAsync(token);
        }
        if (effect.ExceedsReservation)
        {
            await using var stop = Command("""
                UPDATE evaluation.experiments SET cancel_requested=true,execution_state='indeterminate'
                WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment
                """);
            await stop.ExecuteNonQueryAsync(token);
        }
        // Raw reservations/outcomes are immutable evidence here. Do not subtract
        // declared charges or release the uncertain difference to the scheduler.
        await transaction.CommitAsync(token);
        return new(request.DecisionId, false, effect.Origin);

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("experiment", request.ExperimentId);
            command.Parameters.AddWithValue("trial", request.TrialId);
            command.Parameters.AddWithValue("attempt", request.AttemptId);
            command.Parameters.AddWithValue("decision", request.DecisionId);
            command.Parameters.AddWithValue("evidence", request.Evidence.Id);
            command.Parameters.AddWithValue("hash", request.Evidence.Sha256);
            return command;
        }
    }
}
