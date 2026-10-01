using Cortex.Evaluation.Contracts;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Security.Cryptography;

namespace Cortex.Evaluation.Execution;

public sealed record ExperimentPlanReceipt(Guid ExperimentId, string PlanSha256, int TrialCount);
public sealed record TrialClaim(Guid TrialId, Guid Owner, long Generation, ScopedRevisionRef Candidate,
    ScopedRevisionRef Case, int Repetition);

public sealed class ExperimentCoordinator(NpgsqlDataSource source)
{
    public async Task StopFailedTrialAsync(EvaluationAccess access, Guid experimentId, TrialClaim claim,
        string reason, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        access.DemandScope(claim.Candidate.Scope);
        access.DemandScope(claim.Case.Scope);
        if (experimentId == Guid.Empty || claim.TrialId == Guid.Empty || claim.Owner == Guid.Empty ||
            claim.Generation < 1 || reason is not ("execution_failed" or "execution_cancelled"))
            throw new InvalidDataException("Invalid failed trial reference or diagnostic code.");
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var parent = Command("""
            SELECT id FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """, connection, transaction, access, experimentId))
            if (await parent.ExecuteScalarAsync(token) is not Guid) throw new KeyNotFoundException("Scoped experiment is unavailable.");
        await using (var lookup = Command("""
            SELECT state,lease_owner,lease_generation,candidate_revision_id,candidate_revision_sha256,
              case_revision_id,case_revision_sha256
            FROM evaluation.trials WHERE workspace_id=@workspace AND project_id=@project
              AND id=@trial AND experiment_id=@experiment FOR UPDATE
            """, connection, transaction, access, experimentId))
        {
            lookup.Parameters.AddWithValue("trial", claim.TrialId);
            await using var reader = await lookup.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) || reader.IsDBNull(1) || reader.IsDBNull(3) || reader.IsDBNull(5) ||
                reader.GetGuid(1) != claim.Owner || reader.GetInt64(2) != claim.Generation ||
                reader.GetGuid(3) != claim.Candidate.Revision.Id || reader.GetString(4).Trim() != claim.Candidate.Revision.Sha256 ||
                reader.GetGuid(5) != claim.Case.Revision.Id || reader.GetString(6).Trim() != claim.Case.Revision.Sha256 ||
                reader.GetString(0) is not ("running" or "indeterminate"))
                throw new InvalidOperationException("Failure belongs to an obsolete, completed or mismatched claim.");
            if (reader.GetString(0) == "indeterminate") return;
        }
        await using var update = Command("""
            UPDATE evaluation.trials SET state='indeterminate'
            WHERE workspace_id=@workspace AND project_id=@project AND id=@trial AND experiment_id=@experiment;
            UPDATE evaluation.experiments SET execution_state='indeterminate',cancel_requested=true
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment;
            INSERT INTO evaluation.audit_log (workspace_id,project_id,id,actor,action,reason)
            VALUES (@workspace,@project,@audit,@actor,'trial_execution_stopped',@reason)
            """, connection, transaction, access, experimentId);
        update.Parameters.AddWithValue("trial", claim.TrialId);
        update.Parameters.AddWithValue("audit", Guid.NewGuid());
        update.Parameters.AddWithValue("actor", access.Principal);
        update.Parameters.AddWithValue("reason", $"{claim.TrialId:D}:{reason}");
        await update.ExecuteNonQueryAsync(token);
        // Neither attempts nor reservations are changed. Failure cannot establish that a call was free.
        await transaction.CommitAsync(token);
    }

    public async Task CompleteAsync(EvaluationAccess access, Guid experimentId, TrialClaim claim,
        CompletionEvaluationResult result, EvidenceArtifactRef artifact, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        access.DemandScope(claim.Candidate.Scope);
        access.DemandScope(claim.Case.Scope);
        if (experimentId == Guid.Empty || claim.TrialId == Guid.Empty || claim.Owner == Guid.Empty || claim.Generation < 1 ||
            result.ProfileSha256 != claim.Candidate.Revision.Sha256 || result.InputSha256 != claim.Case.Revision.Sha256 ||
            result.Elapsed < TimeSpan.Zero || result.Output is null || artifact.Id == Guid.Empty ||
            Convert.ToHexStringLower(SHA256.HashData(result.ToArtifact(false).Content.Span)) != artifact.Sha256)
            throw new InvalidDataException("Invalid completed trial evidence binding.");
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var parent = Command("""
            SELECT id FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """, connection, transaction, access, experimentId))
            if (await parent.ExecuteScalarAsync(token) is not Guid) throw new KeyNotFoundException("Scoped experiment is unavailable.");
        bool alreadyCompleted;
        await using (var lookup = Command("""
            SELECT state,lease_owner,lease_generation,candidate_revision_id,candidate_revision_sha256,
              case_revision_id,case_revision_sha256,result_artifact_id,result_artifact_sha256
            FROM evaluation.trials WHERE workspace_id=@workspace AND project_id=@project AND id=@trial AND experiment_id=@experiment FOR UPDATE
            """, connection, transaction, access, experimentId))
        {
            lookup.Parameters.AddWithValue("trial", claim.TrialId);
            await using var reader = await lookup.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) || reader.IsDBNull(1) || reader.IsDBNull(3) || reader.IsDBNull(5) ||
                reader.GetGuid(1) != claim.Owner || reader.GetInt64(2) != claim.Generation ||
                reader.GetGuid(3) != claim.Candidate.Revision.Id || reader.GetString(4).Trim() != result.ProfileSha256 ||
                reader.GetGuid(5) != claim.Case.Revision.Id || reader.GetString(6).Trim() != result.InputSha256 ||
                reader.GetString(0) is not ("running" or "completed"))
                throw new InvalidOperationException("Trial result belongs to an obsolete or mismatched claim.");
            alreadyCompleted = reader.GetString(0) == "completed";
            if (alreadyCompleted && (reader.IsDBNull(7) || reader.GetGuid(7) != artifact.Id || reader.GetString(8).Trim() != artifact.Sha256))
                throw new InvalidOperationException("Completed trial already has different evidence.");
        }
        await using (var evidence = Command("""
            SELECT payload,is_holdout FROM evaluation.artifacts WHERE workspace_id=@workspace AND project_id=@project
              AND id=@artifact AND sha256=@sha AND kind='completion-result' AND redacted_at IS NULL
            """, connection, transaction, access, experimentId))
        {
            evidence.Parameters.AddWithValue("artifact", artifact.Id);
            evidence.Parameters.AddWithValue("sha", artifact.Sha256);
            await using var reader = await evidence.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidDataException("Result artifact is unavailable.");
            access.DemandArtifactRead(reader.GetBoolean(1));
            if (Convert.ToHexStringLower(SHA256.HashData(reader.GetFieldValue<byte[]>(0))) != artifact.Sha256)
                throw new InvalidDataException("Result artifact integrity failure.");
            // Completion records past evidence, not a retention promise. Operator redaction may
            // subsequently remove bytes; do not grant the evaluator artifact UPDATE to lock them.
        }
        await using (var attempts = Command("""
            SELECT count(*),count(*) FILTER (WHERE state <> 'observed') FROM evaluation.attempts
            WHERE workspace_id=@workspace AND project_id=@project AND trial_id=@trial
            """, connection, transaction, access, experimentId))
        {
            attempts.Parameters.AddWithValue("trial", claim.TrialId);
            await using var reader = await attempts.ExecuteReaderAsync(token);
            await reader.ReadAsync(token);
            if (reader.GetInt64(0) == 0 || reader.GetInt64(1) != 0)
                throw new InvalidOperationException("Unresolved physical attempts cannot produce a completed trial.");
        }
        if (!alreadyCompleted)
        {
            await using var update = Command("""
                UPDATE evaluation.trials SET state='completed',result_artifact_id=@artifact,result_artifact_sha256=@sha
                WHERE workspace_id=@workspace AND project_id=@project AND id=@trial AND experiment_id=@experiment;
                UPDATE evaluation.experiments SET execution_state='completed'
                WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment AND execution_state='running'
                  AND NOT EXISTS (SELECT 1 FROM evaluation.trials WHERE workspace_id=@workspace AND project_id=@project
                    AND experiment_id=@experiment AND state <> 'completed')
                """, connection, transaction, access, experimentId);
            update.Parameters.AddWithValue("trial", claim.TrialId);
            update.Parameters.AddWithValue("artifact", artifact.Id);
            update.Parameters.AddWithValue("sha", artifact.Sha256);
            await update.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

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
        access.Demand(EvaluationOperation.CancelExperiment);
        if (experimentId == Guid.Empty) throw new InvalidDataException("Explicit experiment is required.");
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        // Serializes with claim/admission. Existing physical attempts and all their
        // reservations remain untouched; cancellation is not a refund or unsend.
        await using (var lookup = Command("""
            SELECT cancel_requested FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """, connection, transaction, access, experimentId))
        {
            var cancelled = await lookup.ExecuteScalarAsync(token);
            if (cancelled is null) throw new KeyNotFoundException("Scoped experiment is unavailable.");
            if (cancelled is true)
            {
                await transaction.CommitAsync(token);
                return;
            }
        }
        await using var command = Command("""
            UPDATE evaluation.experiments SET cancel_requested=true,
              execution_state=CASE WHEN execution_state IN ('planned','running') THEN 'cancelled' ELSE execution_state END
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment
            """, connection, transaction, access, experimentId);
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new KeyNotFoundException("Scoped experiment is unavailable.");
        await using (var audit = Command("""
            INSERT INTO evaluation.audit_log (workspace_id,project_id,id,actor,action,reason)
            VALUES (@workspace,@project,@audit,@actor,'experiment.cancel',@reason)
            """, connection, transaction, access, experimentId))
        {
            audit.Parameters.AddWithValue("audit", Guid.NewGuid());
            audit.Parameters.AddWithValue("actor", access.Principal);
            audit.Parameters.AddWithValue("reason", JsonSerializer.Serialize(new { experimentId, reason = "host_cancel_requested" }));
            await audit.ExecuteNonQueryAsync(token);
        }
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
