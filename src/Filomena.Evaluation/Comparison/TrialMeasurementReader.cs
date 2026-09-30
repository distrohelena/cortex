using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Npgsql;

namespace Filomena.Evaluation.Comparison;

public sealed record TrialMeasurementSnapshot(Guid ExperimentId, string TrialState, string ResultEvidenceStatus,
    TrialMeasurement Measurement, IReadOnlyList<PhysicalAttemptMeasurement> Attempts);

/// <summary>Read-only evidence snapshot. No lease claim, model invocation, price invention or quality approval.</summary>
public sealed class TrialMeasurementReader(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TrialMeasurementSnapshot?> ReadAsync(EvaluationAccess access, Guid experimentId, Guid trialId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.Evaluate);
        if (experimentId == Guid.Empty || trialId == Guid.Empty) throw new ArgumentException("Explicit experiment and trial required.");
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await mode.ExecuteNonQueryAsync(token);
        var snapshot = await ReadInSnapshotAsync(access, experimentId, trialId, connection, transaction, token);
        await transaction.CommitAsync(token);
        return snapshot;
    }

    internal static async Task<TrialMeasurementSnapshot?> ReadInSnapshotAsync(EvaluationAccess access, Guid experimentId,
        Guid trialId, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        if (experimentId == Guid.Empty || trialId == Guid.Empty) throw new ArgumentException("Explicit experiment and trial required.");
        string state;
        var resultStatus = "unavailable";
        TimeSpan? elapsed = null;
        await using (var query = Command("""
            SELECT t.state,t.candidate_revision_sha256,t.case_revision_sha256,
                   t.result_artifact_id,t.result_artifact_sha256,a.kind,a.payload,a.is_holdout
            FROM evaluation.trials t LEFT JOIN evaluation.artifacts a
              ON a.workspace_id=t.workspace_id AND a.project_id=t.project_id
              AND a.id=t.result_artifact_id AND a.sha256=t.result_artifact_sha256 AND a.redacted_at IS NULL
            WHERE t.workspace_id=@workspace AND t.project_id=@project AND t.experiment_id=@experiment AND t.id=@trial
            """))
        {
            await using var reader = await query.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            state = reader.GetString(0);
            if (state is not ("pending" or "running" or "completed" or "indeterminate" or "cancelled"))
                throw new InvalidDataException("Unsupported trial state.");
            if (reader.IsDBNull(3)) resultStatus = "not_recorded";
            else if (!reader.IsDBNull(6))
            {
                access.DemandArtifactRead(reader.GetBoolean(7));
                if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(4) || reader.GetString(5) != "completion-result")
                    throw new InvalidDataException("Trial result binding unavailable.");
                var validatedElapsed = ReadElapsed(reader.GetFieldValue<byte[]>(6), reader.GetString(4).Trim(),
                    reader.GetString(1).Trim(), reader.GetString(2).Trim());
                resultStatus = "available";
                // A result artifact alone cannot complete an unfinished trial.
                if (state == "completed") elapsed = validatedElapsed;
            }
        }
        var attempts = new List<PhysicalAttemptMeasurement>();
        await using (var query = Command("""
            SELECT a.id,a.state,a.outcome::text,a.provider,a.request_sha256,a.started_at FROM evaluation.attempts a
            JOIN evaluation.trials t ON t.workspace_id=a.workspace_id AND t.project_id=a.project_id AND t.id=a.trial_id
            WHERE a.workspace_id=@workspace AND a.project_id=@project AND a.trial_id=@trial AND t.experiment_id=@experiment
            ORDER BY a.attempt_number,a.id
            """))
        {
            await using var reader = await query.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                PhysicalCallOutcome? outcome = null;
                if (!reader.IsDBNull(2))
                {
                    using var parsed = JsonDocument.Parse(reader.GetString(2));
                    RevisionHasher.ValidateJson(parsed.RootElement, 0);
                    if (parsed.RootElement.ValueKind != JsonValueKind.Object || !parsed.RootElement.TryGetProperty("elapsed", out _))
                        throw new InvalidDataException("Attempt elapsed observation is missing.");
                    outcome = parsed.RootElement.Deserialize<PhysicalCallOutcome>(Json)
                        ?? throw new InvalidDataException("Attempt outcome unavailable.");
                }
                // Reservations are not prices. A separate pinned price projection is
                // required before producing an estimate; none is inferred here.
                attempts.Add(new(reader.GetGuid(0), trialId, reader.GetString(1), outcome, null)
                {
                    Provider = reader.GetString(3),
                    RequestSha256 = reader.GetString(4).Trim(),
                    StartedAt = reader.GetFieldValue<DateTimeOffset>(5)
                });
            }
        }
        var measurement = TrialMeasurement.Aggregate(trialId, elapsed, attempts);
        if (state == "completed" && (measurement.PhysicalCalls == 0 || measurement.HasUnresolvedAttempts))
            throw new InvalidDataException("Completed trial has incomplete physical-call accounting.");
        return new(experimentId, state, resultStatus, measurement, attempts.AsReadOnly());

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("experiment", experimentId);
            command.Parameters.AddWithValue("trial", trialId);
            return command;
        }
    }

    public static TimeSpan ReadElapsed(byte[] bytes, string artifactHash, string candidateHash, string inputHash)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > 1_048_576 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != artifactHash)
            throw new InvalidDataException("Result artifact integrity failure.");
        using var parsed = JsonDocument.Parse(bytes);
        RevisionHasher.ValidateJson(parsed.RootElement, 0);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object || !parsed.RootElement.TryGetProperty("elapsed", out _))
            throw new InvalidDataException("Result elapsed observation is missing.");
        var result = parsed.RootElement.Deserialize<CompletionEvaluationResult>(Json)
            ?? throw new InvalidDataException("Result artifact unavailable.");
        if (result.ProfileSha256 != candidateHash || result.InputSha256 != inputHash || result.Elapsed < TimeSpan.Zero || result.Output is null)
            throw new InvalidDataException("Result artifact does not match trial revisions.");
        return result.Elapsed;
    }
}
