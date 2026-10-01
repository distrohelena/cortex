using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Npgsql;

namespace Cortex.Evaluation.Execution;

public sealed record MonetaryBudgetReceipt(MonetaryBudgetApproval Approval, bool AlreadyRecorded)
{
    public string Meaning => "historical_approval_not_execution_permission";
}

public sealed class MonetaryBudgetStore(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MonetaryBudgetReceipt> ApproveAsync(EvaluationAccess access,
        MonetaryBudgetApprovalRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.Operate);
        MonetaryBudgetContract.Validate(request);
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        // Same first lock as ClaimNext/AttemptLedger: approval cannot race admission.
        UnstartedMonetaryPlan plan;
        await using (var query = Command("""
            SELECT manifest::text, plan_sha256, execution_state, cancel_requested,
              reserved_calls, reserved_input_tokens, reserved_output_tokens, reserved_cost
            FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """, connection, transaction, access.Scope, request.ExperimentId))
        {
            await using var reader = await query.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Scoped experiment unavailable.");
            var manifest = JsonSerializer.Deserialize<ExperimentManifest>(reader.GetString(0), Json)
                ?? throw new InvalidDataException("Stored manifest unavailable.");
            if (ManifestValidator.Validate(manifest).Count != 0) throw new InvalidDataException("Invalid stored manifest.");
            access.DemandScope(manifest.Scope);
            if (reader.IsDBNull(1)) throw new InvalidDataException("Stored plan revision unavailable.");
            plan = new(manifest.Scope, request.ExperimentId, reader.GetString(1),
                reader.GetBoolean(3) ? "cancelled" : reader.GetString(2), 0,
                new(reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetDecimal(7)), manifest.Budgets.MaxCost);
        }
        var existing = await ReadInTransactionAsync(access.Scope, request.ExperimentId, connection, transaction, token);
        if (existing is not null)
        {
            if (existing.ApprovalId != request.ApprovalId || existing.PlanSha256 != request.PlanSha256 ||
                existing.Currency != request.Currency || existing.MaximumCost != request.MaximumCost ||
                existing.ApprovedBy != access.Principal)
                throw new InvalidOperationException("Existing approval differs in identity, content or author.");
            // A historical receipt is valid after execution/cancellation, never a new grant.
            await transaction.CommitAsync(token);
            return new(existing, true);
        }
        await using (var attempts = Command("""
            SELECT count(*) FROM evaluation.attempts a JOIN evaluation.trials t
              ON t.workspace_id=a.workspace_id AND t.project_id=a.project_id AND t.id=a.trial_id
            WHERE t.workspace_id=@workspace AND t.project_id=@project AND t.experiment_id=@experiment
            """, connection, transaction, access.Scope, request.ExperimentId))
            plan = plan with { ExistingAttempts = (long)(await attempts.ExecuteScalarAsync(token))! };
        var approved = MonetaryBudgetContract.Approve(access, request, plan);
        await using (var insert = Command("""
            INSERT INTO evaluation.monetary_budget_approvals
              (workspace_id,project_id,id,experiment_id,contract_version,plan_sha256,currency,maximum_cost,approved_by)
            VALUES (@workspace,@project,@id,@experiment,1,@hash,@currency,@cost,@actor)
            """, connection, transaction, access.Scope, request.ExperimentId))
        {
            insert.Parameters.AddWithValue("id", approved.ApprovalId);
            insert.Parameters.AddWithValue("hash", approved.PlanSha256);
            insert.Parameters.AddWithValue("currency", approved.Currency);
            insert.Parameters.AddWithValue("cost", approved.MaximumCost);
            insert.Parameters.AddWithValue("actor", approved.ApprovedBy);
            await insert.ExecuteNonQueryAsync(token);
        }
        // An ambiguous commit is not retried here. Reconcile via the scoped read or same ID.
        await transaction.CommitAsync(token);
        return new(approved, false);
    }

    public async Task<MonetaryBudgetApproval?> ReadAsync(EvaluationAccess access, Guid experiment, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.ReadExperiment);
        if (experiment == Guid.Empty) throw new InvalidDataException("Explicit experiment required.");
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        return await ReadInTransactionAsync(access.Scope, experiment, connection, null, token);
    }

    internal static async Task<MonetaryBudgetApproval?> ReadInTransactionAsync(EvaluationScope scope, Guid experiment,
        NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken token)
    {
        await using var query = Command("""
            SELECT id, plan_sha256, currency, maximum_cost, approved_by
            FROM evaluation.monetary_budget_approvals
            WHERE workspace_id=@workspace AND project_id=@project AND experiment_id=@experiment
            """, connection, transaction, scope, experiment);
        await using var reader = await query.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? new(reader.GetGuid(0), scope, experiment, reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetString(4))
            : null;
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction? transaction,
        EvaluationScope scope, Guid experiment)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("workspace", scope.WorkspaceId);
        command.Parameters.AddWithValue("project", scope.ProjectId);
        command.Parameters.AddWithValue("experiment", experiment);
        return command;
    }
}
