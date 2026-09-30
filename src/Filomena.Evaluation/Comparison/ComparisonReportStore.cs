using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Npgsql;

namespace Filomena.Evaluation.Comparison;

public sealed record ComparisonReportSaveRequest(Guid ReportId, ExperimentComparisonRequest Comparison);
public sealed record ComparisonReportReceipt(Guid ReportId, string Sha256, bool AlreadyRecorded);
public sealed record StoredComparisonReport(Guid ReportId, string Sha256, string Status, ExperimentComparisonSnapshot? Snapshot);
public sealed record ComparisonReportEnvelope(int ContractVersion, string Method, ExperimentComparisonSnapshot Snapshot);

/// <summary>Scoped append-only derived evidence, never an activation or settlement operation.</summary>
public sealed class ComparisonReportStore(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ComparisonReportSaveRequest Validate(ComparisonReportSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReportId == Guid.Empty || request.Comparison is null) throw new ArgumentException("Explicit report and comparison required.");
        ComparisonPlan.Validate(request.Comparison);
        return request with
        {
            Comparison = request.Comparison with
            {
                Inputs = Array.AsReadOnly(request.Comparison.Inputs.ToArray()),
                Prices = Array.AsReadOnly(request.Comparison.Prices.OrderBy(p => p.AttemptId).ToArray())
            }
        };
    }

    public async Task<ComparisonReportReceipt> SaveAsync(EvaluationAccess access, ComparisonReportSaveRequest request, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        request = Validate(request);
        var requestHash = RevisionHasher.Hash(new RevisionDocument(1, "comparison-report-request", JsonSerializer.SerializeToElement(request.Comparison)));
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using (var existing = Command("""
            SELECT request_sha256,created_by,report_sha256 FROM evaluation.comparison_reports
            WHERE workspace_id=@workspace AND project_id=@project AND id=@report
            """))
        {
            await using var reader = await existing.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                if (reader.GetString(0).Trim() != requestHash || reader.GetString(1) != access.Principal)
                    throw new InvalidOperationException("Report identity already has a different request or author.");
                // Historical receipt only; content access still requires ShowAsync.
                return new(request.ReportId, reader.GetString(2).Trim(), true);
            }
        }
        var snapshot = await ExperimentComparisonReader.ReadInSnapshotAsync(access, request.Comparison, connection, transaction, token)
            ?? throw new KeyNotFoundException("Scoped comparison evidence unavailable.");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ComparisonReportEnvelope(1, "experiment-comparison:v1", snapshot), Json);
        if (payload.Length > 1_048_576 || snapshot.SourceEvidence.Count == 0)
            throw new InvalidDataException("Comparison report exceeds its evidence or storage contract.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(payload));
        await using (var insert = Command("""
            INSERT INTO evaluation.comparison_reports
              (workspace_id,project_id,id,experiment_id,policy_revision_id,policy_revision_sha256,request_sha256,report_sha256,payload,created_by)
            VALUES (@workspace,@project,@report,@experiment,@policy,@policy_hash,@request_hash,@hash,@payload,@actor)
            """))
        {
            insert.Parameters.AddWithValue("experiment", request.Comparison.ExperimentId);
            insert.Parameters.AddWithValue("policy", request.Comparison.PolicyRevision.Id);
            insert.Parameters.AddWithValue("policy_hash", request.Comparison.PolicyRevision.Sha256);
            insert.Parameters.AddWithValue("request_hash", requestHash);
            insert.Parameters.AddWithValue("hash", hash);
            insert.Parameters.AddWithValue("payload", payload);
            insert.Parameters.AddWithValue("actor", access.Principal);
            await insert.ExecuteNonQueryAsync(token);
        }
        foreach (var evidence in snapshot.SourceEvidence)
        {
            await using var insert = Command("""
                INSERT INTO evaluation.comparison_report_evidence (workspace_id,project_id,report_id,artifact_id,artifact_sha256)
                VALUES (@workspace,@project,@report,@artifact,@hash)
                """);
            insert.Parameters.AddWithValue("artifact", evidence.Id);
            insert.Parameters.AddWithValue("hash", evidence.Sha256);
            await insert.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return new(request.ReportId, hash, false);

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("report", request.ReportId);
            return command;
        }
    }

    public async Task<StoredComparisonReport?> ShowAsync(EvaluationAccess access, Guid reportId, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        if (reportId == Guid.Empty) throw new ArgumentException("Explicit report required.");
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        return await ReadInSnapshotAsync(access, reportId, connection, null, token);
    }

    internal static async Task<StoredComparisonReport?> ReadInSnapshotAsync(EvaluationAccess access, Guid reportId,
        NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        if (reportId == Guid.Empty) throw new ArgumentException("Explicit report required.");
        await using var query = new NpgsqlCommand("""
            SELECT r.report_sha256,r.experiment_id,r.policy_revision_id,r.policy_revision_sha256,
              CASE WHEN EXISTS (SELECT 1 FROM evaluation.comparison_report_evidence e
                  WHERE e.workspace_id=r.workspace_id AND e.project_id=r.project_id AND e.report_id=r.id)
                AND NOT EXISTS (SELECT 1 FROM evaluation.comparison_report_evidence e LEFT JOIN evaluation.artifacts a
                  ON a.workspace_id=e.workspace_id AND a.project_id=e.project_id AND a.id=e.artifact_id
                    AND a.sha256=e.artifact_sha256 AND a.redacted_at IS NULL
                  WHERE e.workspace_id=r.workspace_id AND e.project_id=r.project_id AND e.report_id=r.id AND a.id IS NULL)
                THEN r.payload ELSE NULL END,
              (SELECT jsonb_agg(jsonb_build_object('id',e.artifact_id,'sha256',trim(e.artifact_sha256)) ORDER BY e.artifact_id)::text
                FROM evaluation.comparison_report_evidence e WHERE e.workspace_id=r.workspace_id AND e.project_id=r.project_id AND e.report_id=r.id)
            FROM evaluation.comparison_reports r WHERE r.workspace_id=@workspace AND r.project_id=@project AND r.id=@report
            """, connection, transaction);
        query.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
        query.Parameters.AddWithValue("project", access.Scope.ProjectId);
        query.Parameters.AddWithValue("report", reportId);
        await using var reader = await query.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var hash = reader.GetString(0).Trim();
        if (reader.IsDBNull(4)) return new(reportId, hash, "evidence_unavailable", null);
        var references = JsonSerializer.Deserialize<EvidenceArtifactRef[]>(reader.GetString(5), Json)!;
        try
        {
            var snapshot = Decode(reader.GetFieldValue<byte[]>(4), hash, reader.GetGuid(1),
                new(reader.GetGuid(2), reader.GetString(3).Trim()), references);
            return new(reportId, hash, "available", snapshot);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        {
            return new(reportId, hash, "invalid_record", null);
        }
    }

    public static ExperimentComparisonSnapshot Decode(byte[] payload, string hash, Guid experimentId,
        RevisionRef policyRevision, IReadOnlyList<EvidenceArtifactRef> sourceEvidence)
    {
        if (payload.Length is < 1 or > 1_048_576 || Convert.ToHexStringLower(SHA256.HashData(payload)) != hash)
            throw new InvalidDataException("Report integrity failure.");
        using var parsed = JsonDocument.Parse(payload);
        RevisionHasher.ValidateJson(parsed.RootElement, 0);
        var envelope = parsed.RootElement.Deserialize<ComparisonReportEnvelope>(Json);
        if (envelope is not { ContractVersion: 1, Method: "experiment-comparison:v1", Snapshot: { } snapshot } ||
            snapshot.ExperimentId != experimentId || snapshot.Report?.PolicyRevision != policyRevision ||
            snapshot.PlanSha256 is not { Length: 64 } || snapshot.PlanSha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)) ||
            snapshot.Inputs is null || snapshot.Inputs.Count == 0 || snapshot.Report.Statistics is null ||
            snapshot.Report.Statistics.Candidates is null || snapshot.Report.Statistics.Candidates.Count == 0 ||
            snapshot.Report.Statistics.ParetoCandidates is null || snapshot.Report.Trials is null || snapshot.PriceProjections is null ||
            snapshot.SourceEvidence is null || sourceEvidence.Count == 0 ||
            !snapshot.SourceEvidence.SequenceEqual(sourceEvidence))
            throw new InvalidDataException("Report provenance does not match stored references.");
        ComparisonPlan.Validate(new(snapshot.ExperimentId, snapshot.Report.PolicyRevision, snapshot.Inputs));
        return snapshot;
    }
}
