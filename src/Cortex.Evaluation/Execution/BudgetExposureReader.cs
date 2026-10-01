using System.Data;
using System.Text.Json;
using Cortex.Evaluation.Comparison;
using Cortex.Evaluation.Contracts;
using Npgsql;

namespace Cortex.Evaluation.Execution;

public sealed record BudgetExposureSnapshot(Guid ExperimentId, string ExperimentState, bool CancelRequested,
    BudgetExposureReport Accounting, IReadOnlyList<AttemptBudgetEvidence> Attempts)
{
    public ChargeAccountingReport? Billing { get; init; }
    public IReadOnlyList<ChargeAccountingEntry> Charges { get; init; } = [];
}

/// <summary>Reconciles reservation/usage evidence without releasing capacity, settling billing or resuming work.</summary>
public sealed class BudgetExposureReader(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BudgetExposureSnapshot?> ReadAsync(EvaluationAccess access, Guid experimentId, CancellationToken token)
    {
        access.Demand(EvaluationOperation.ReadExperiment);
        if (experimentId == Guid.Empty) throw new ArgumentException("Explicit experiment required.");
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await mode.ExecuteNonQueryAsync(token);
        BudgetConsumption parent;
        string state;
        bool cancelled;
        await using (var query = Command("""
            SELECT execution_state,cancel_requested,reserved_calls,reserved_input_tokens,reserved_output_tokens,reserved_cost
            FROM evaluation.experiments WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment
            """))
        {
            await using var reader = await query.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            state = reader.GetString(0);
            cancelled = reader.GetBoolean(1);
            parent = new(reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetDecimal(5));
        }
        var attempts = new List<AttemptBudgetEvidence>();
        var charges = new List<ChargeAccountingEntry>();
        var approval = await MonetaryBudgetStore.ReadInTransactionAsync(access.Scope, experimentId, connection, transaction, token);
        await using (var query = Command("""
            SELECT a.id,a.trial_id,a.state,a.reserved_input_tokens,a.reserved_output_tokens,a.reserved_cost,a.outcome::text,
              a.lease_owner,a.lease_generation,a.monetary_approval_id,a.reservation_currency,d.id,
              CASE WHEN e.id IS NOT NULL AND e.redacted_at IS NULL AND e.kind='billing-evidence'
                THEN d.declaration::text ELSE NULL END,t.state,t.lease_generation,d.evidence_id,d.evidence_sha256
            FROM evaluation.attempts a JOIN evaluation.trials t
              ON t.workspace_id=a.workspace_id AND t.project_id=a.project_id AND t.id=a.trial_id
            LEFT JOIN evaluation.attempt_charge_declarations d ON d.workspace_id=a.workspace_id AND d.project_id=a.project_id
              AND d.attempt_id=a.id AND d.trial_id=t.id AND d.experiment_id=t.experiment_id
            LEFT JOIN evaluation.artifacts e ON e.workspace_id=d.workspace_id AND e.project_id=d.project_id
              AND e.id=d.evidence_id AND e.sha256=d.evidence_sha256
            WHERE a.workspace_id=@workspace AND a.project_id=@project AND t.experiment_id=@experiment
            ORDER BY a.id LIMIT 100001
            """))
        {
            await using var reader = await query.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                PhysicalCallOutcome? outcome = null;
                if (!reader.IsDBNull(6))
                {
                    using var parsed = JsonDocument.Parse(reader.GetString(6));
                    RevisionHasher.ValidateJson(parsed.RootElement, 0);
                    outcome = parsed.RootElement.Deserialize<PhysicalCallOutcome>(Json)
                        ?? throw new InvalidDataException("Attempt outcome unavailable.");
                }
                var id = reader.GetGuid(0);
                var attemptState = reader.GetString(2);
                // Reuse real outcome/usage validity rules; no output content is loaded.
                _ = TrialMeasurement.Aggregate(reader.GetGuid(1), null,
                    [new(id, reader.GetGuid(1), attemptState, outcome, null)]);
                attempts.Add(new(id, attemptState, new(reader.GetInt64(3), reader.GetInt64(4), reader.GetDecimal(5)),
                    outcome?.Observation?.Usage?.InputTokens, outcome?.Observation?.Usage?.OutputTokens));
                if (reader.IsDBNull(11))
                    charges.Add(new(id, null, reader.GetDecimal(5), "missing", null, null));
                else if (reader.IsDBNull(12))
                    charges.Add(new(id, reader.GetGuid(11), reader.GetDecimal(5), "evidence_unavailable", null, null));
                else
                {
                    using var parsed = JsonDocument.Parse(reader.GetString(12));
                    RevisionHasher.ValidateJson(parsed.RootElement, 0);
                    var declaration = parsed.RootElement.Deserialize<AttemptChargeDeclaration>(Json)
                        ?? throw new InvalidDataException("Billing declaration unavailable.");
                    if (declaration.DecisionId != reader.GetGuid(11) || approval is null ||
                        declaration.ApprovalId != approval.ApprovalId || declaration.Currency != approval.Currency ||
                        declaration.Evidence is null || declaration.Evidence.Id != reader.GetGuid(15) ||
                        declaration.Evidence.Sha256 != reader.GetString(16))
                        throw new InvalidDataException("Billing approval binding differs from persisted evidence.");
                    _ = AttemptChargePolicy.Evaluate(declaration, new(experimentId, reader.GetGuid(1), id,
                        reader.GetGuid(7), reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetGuid(9),
                        reader.IsDBNull(10) ? null : reader.GetString(10), attemptState, reader.GetDecimal(5))
                    { ExecutionQuarantined = cancelled && reader.GetString(13) == "indeterminate" && reader.GetInt64(14) > reader.GetInt64(8) });
                    charges.Add(new(id, declaration.DecisionId, reader.GetDecimal(5), "declared", declaration.Currency, declaration.Charge));
                }
            }
        }
        if (attempts.Count > 100_000) throw new InvalidDataException("Accounting bound exceeded; no partial totals allowed.");
        var accounting = BudgetExposure.Reconcile(attempts, parent);
        var billing = ChargeAccounting.Project(charges, approval?.Currency, parent.Cost);
        accounting = accounting with
        {
            ConservativeExposure = accounting.ConservativeExposure with
            { Cost = Math.Max(accounting.ConservativeExposure.Cost, billing.RetainedExposureLowerBound) }
        };
        await transaction.CommitAsync(token);
        return new(experimentId, state, cancelled, accounting, attempts.AsReadOnly())
        { Billing = billing, Charges = charges.AsReadOnly() };

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("experiment", experimentId);
            return command;
        }
    }
}
