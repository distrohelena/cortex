using Filomena.Evaluation.Contracts;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Filomena.Evaluation.Execution;

public sealed record ExperimentPlanReceipt(Guid ExperimentId, string PlanSha256, int TrialCount);
public sealed record TrialClaim(Guid TrialId, Guid Owner, long Generation, ScopedRevisionRef Candidate,
    ScopedRevisionRef Case, int Repetition);

public sealed class ExperimentCoordinator(NpgsqlDataSource source)
{
    public async Task<ExperimentPlanReceipt> PlanAsync(EvaluationAccess access, Guid experimentId, ExperimentManifest manifest,
        IReadOnlyList<ScopedRevisionRef> cases, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        access.Demand(EvaluationOperation.Evaluate);
        access.DemandScope(manifest.Scope);
        if (experimentId == Guid.Empty) throw new InvalidDataException("Explicit experiment identity is required.");
        var schedule = ExperimentSchedule.Create(manifest, cases);
        var hash = RevisionHasher.Hash(new RevisionDocument(1, "experiment-plan", JsonSerializer.SerializeToElement(new { manifest, cases })));
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var insert = Command("""
            INSERT INTO evaluation.experiments
              (workspace_id,project_id,id,manifest,created_by,plan_sha256,
               target_revision_id,target_revision_sha256,baseline_revision_id,baseline_revision_sha256,
               dataset_revision_id,dataset_revision_sha256,evaluator_revision_id,evaluator_revision_sha256,
               policy_revision_id,policy_revision_sha256)
            VALUES (@workspace,@project,@experiment,@manifest,@principal,@hash,
                    @target_id,@target_sha,@baseline_id,@baseline_sha,@dataset_id,@dataset_sha,
                    @evaluator_id,@evaluator_sha,@policy_id,@policy_sha)
            ON CONFLICT (workspace_id,project_id,id) DO NOTHING RETURNING id
            """, connection, transaction, access, experimentId);
        insert.Parameters.AddWithValue("manifest", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        insert.Parameters.AddWithValue("principal", access.Principal);
        insert.Parameters.AddWithValue("hash", hash);
        AddRef(insert, "target", manifest.Target.Revision);
        AddRef(insert, "baseline", manifest.Baseline.Revision);
        AddRef(insert, "dataset", manifest.Dataset.Revision);
        AddRef(insert, "evaluator", manifest.Evaluator.Revision);
        AddRef(insert, "policy", manifest.Policy.Revision);
        if (await insert.ExecuteScalarAsync(token) is null)
        {
            await using var existing = Command("""
                SELECT plan_sha256 FROM evaluation.experiments
                WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
                """, connection, transaction, access, experimentId);
            if (await existing.ExecuteScalarAsync(token) is not string old || old.Trim() != hash)
                throw new InvalidOperationException("Experiment identity already belongs to a different or unplanned manifest.");
            await transaction.CommitAsync(token);
            return new(experimentId, hash, schedule.Count);
        }
        var candidates = new[] { manifest.Baseline }.Concat(manifest.Candidates).Distinct().ToArray();
        for (var index = 0; index < candidates.Length; index++)
        {
            await using var candidate = Command("""
                INSERT INTO evaluation.experiment_candidates
                  (workspace_id,project_id,experiment_id,revision_id,revision_sha256,ordinal)
                VALUES (@workspace,@project,@experiment,@candidate_id,@candidate_sha,@ordinal)
                """, connection, transaction, access, experimentId);
            AddRef(candidate, "candidate", candidates[index].Revision);
            candidate.Parameters.AddWithValue("ordinal", index);
            await candidate.ExecuteNonQueryAsync(token);
        }
        for (var index = 0; index < schedule.Count; index++)
        {
            var item = schedule[index];
            await using var trial = Command("""
                INSERT INTO evaluation.trials
                  (workspace_id,project_id,id,experiment_id,schedule_ordinal,trial_key,
                   candidate_revision_id,candidate_revision_sha256,case_revision_id,case_revision_sha256,repetition)
                VALUES (@workspace,@project,@trial,@experiment,@ordinal,@key,@candidate_id,@candidate_sha,@case_id,@case_sha,@repetition)
                """, connection, transaction, access, experimentId);
            trial.Parameters.AddWithValue("trial", Guid.NewGuid());
            trial.Parameters.AddWithValue("ordinal", index);
            trial.Parameters.AddWithValue("key", item.TrialKey);
            trial.Parameters.AddWithValue("repetition", item.Repetition);
            AddRef(trial, "candidate", item.Candidate.Revision);
            AddRef(trial, "case", item.Case.Revision);
            await trial.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return new(experimentId, hash, schedule.Count);
    }

    /// <summary>Claims pending work only. Running/uncertain work is never reclaimed automatically.</summary>
    public async Task<TrialClaim?> ClaimNextAsync(EvaluationAccess access, Guid experimentId, Guid owner, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        if (experimentId == Guid.Empty || owner == Guid.Empty) throw new InvalidDataException("Explicit experiment and owner are required.");
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var experiment = Command("""
            SELECT execution_state,cancel_requested,plan_sha256 FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """, connection, transaction, access, experimentId))
        {
            await using var reader = await experiment.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Scoped experiment is unavailable.");
            if (reader.GetString(0) is not ("planned" or "running") || reader.GetBoolean(1) || reader.IsDBNull(2))
                throw new InvalidOperationException("Experiment is not claimable.");
        }
        TrialClaim? claim = null;
        await using (var lookup = Command("""
            SELECT id,lease_generation,candidate_revision_id,candidate_revision_sha256,
              case_revision_id,case_revision_sha256,repetition
            FROM evaluation.trials WHERE workspace_id=@workspace AND project_id=@project AND experiment_id=@experiment
              AND state='pending' AND schedule_ordinal IS NOT NULL ORDER BY schedule_ordinal LIMIT 1 FOR UPDATE
            """, connection, transaction, access, experimentId))
        {
            await using var reader = await lookup.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) claim = new(reader.GetGuid(0), owner, checked(reader.GetInt64(1) + 1),
                new(access.Scope, new(reader.GetGuid(2), reader.GetString(3).Trim())),
                new(access.Scope, new(reader.GetGuid(4), reader.GetString(5).Trim())), reader.GetInt32(6));
        }
        if (claim is not null)
        {
            await using var update = Command("""
                UPDATE evaluation.trials SET state='running',lease_owner=@owner,lease_generation=@generation
                WHERE workspace_id=@workspace AND project_id=@project AND id=@trial AND experiment_id=@experiment;
                UPDATE evaluation.experiments SET execution_state='running'
                WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment
                """, connection, transaction, access, experimentId);
            update.Parameters.AddWithValue("trial", claim.TrialId);
            update.Parameters.AddWithValue("owner", claim.Owner);
            update.Parameters.AddWithValue("generation", claim.Generation);
            await update.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return claim;
    }

    public async Task CancelAsync(EvaluationAccess access, Guid experimentId, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        if (experimentId == Guid.Empty) throw new InvalidDataException("Explicit experiment is required.");
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = Command("""
            UPDATE evaluation.experiments SET cancel_requested=true,
              execution_state=CASE WHEN execution_state IN ('planned','running') THEN 'cancelled' ELSE execution_state END
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment
            """, connection, transaction, access, experimentId);
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new KeyNotFoundException("Scoped experiment is unavailable.");
        await transaction.CommitAsync(token);
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction,
        EvaluationAccess access, Guid experiment)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
        command.Parameters.AddWithValue("project", access.Scope.ProjectId);
        command.Parameters.AddWithValue("experiment", experiment);
        return command;
    }

    private static void AddRef(NpgsqlCommand command, string prefix, RevisionRef reference)
    {
        command.Parameters.AddWithValue(prefix + "_id", reference.Id);
        command.Parameters.AddWithValue(prefix + "_sha", reference.Sha256);
    }
}
