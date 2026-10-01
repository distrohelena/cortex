using System.Data;
using Cortex.Evaluation.Contracts;
using Npgsql;

namespace Cortex.Evaluation.Comparison;

public sealed record TrialAssessmentRequest(Guid ExperimentId, Guid TrialId, Guid BindingId, string Criterion);
public sealed record TrialAssessmentSnapshot(Guid BindingId, string BindingOrigin, Guid CaseId,
    RevisionRef EvaluatorRevision, TrialAssessmentSelection Selection, IReadOnlyList<Guid> AssessmentIds)
{
    public TrialAssessmentRequest? Request { get; init; }
}

/// <summary>Scoped, explicit binding and pinned-evaluator selection. Not a full comparison or quality certification.</summary>
public sealed class TrialAssessmentReader(NpgsqlDataSource source)
{
    public static void Validate(TrialAssessmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExperimentId == Guid.Empty || request.TrialId == Guid.Empty || request.BindingId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Criterion) || request.Criterion.Length > 256 || request.Criterion.Any(char.IsControl))
            throw new ArgumentException("Explicit assessment context required.");
    }

    public async Task<TrialAssessmentSnapshot?> ReadAsync(EvaluationAccess access, TrialAssessmentRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.Evaluate);
        Validate(request);
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await mode.ExecuteNonQueryAsync(token);
        var snapshot = await ReadInSnapshotAsync(access, request, connection, transaction, token);
        await transaction.CommitAsync(token);
        return snapshot;
    }

    internal static async Task<TrialAssessmentSnapshot?> ReadInSnapshotAsync(EvaluationAccess access,
        TrialAssessmentRequest request, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        Validate(request);
        Guid caseId;
        RevisionRef evaluator;
        await using (var context = Command("""
            SELECT c.id,e.evaluator_revision_id,e.evaluator_revision_sha256
            FROM evaluation.trials t JOIN evaluation.experiments e
              ON e.workspace_id=t.workspace_id AND e.project_id=t.project_id AND e.id=t.experiment_id
            JOIN evaluation.case_input_bindings b ON b.workspace_id=t.workspace_id AND b.project_id=t.project_id
              AND b.id=@binding AND b.input_revision_id=t.case_revision_id AND b.input_revision_sha256=t.case_revision_sha256
              AND b.contract_version=1 AND b.origin='evaluator_declaration'
            JOIN evaluation.cases c ON c.workspace_id=b.workspace_id AND c.project_id=b.project_id AND c.id=b.case_id
              AND c.artifact_id=b.case_artifact_id AND c.artifact_sha256=b.case_artifact_sha256
            JOIN evaluation.artifacts a ON a.workspace_id=c.workspace_id AND a.project_id=c.project_id
              AND a.id=c.artifact_id AND a.sha256=c.artifact_sha256 AND a.redacted_at IS NULL
            WHERE t.workspace_id=@workspace AND t.project_id=@project AND t.id=@trial AND t.experiment_id=@experiment
            """))
        {
            await using var reader = await context.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            caseId = reader.GetGuid(0);
            evaluator = new(reader.GetGuid(1), reader.GetString(2).Trim());
        }
        var evidence = new List<TrialAssessmentEvidence>();
        await using (var query = Command("""
            SELECT s.id,s.evaluator_revision_id,s.evaluator_revision_sha256,s.outcome,s.severity,s.supersedes_assessment_id,
              EXISTS (SELECT 1 FROM evaluation.assessment_evidence ae JOIN evaluation.trials t
                ON t.workspace_id=ae.workspace_id AND t.project_id=ae.project_id AND t.id=s.trial_id
                WHERE ae.workspace_id=s.workspace_id AND ae.project_id=s.project_id AND ae.assessment_id=s.id
                  AND ae.artifact_id=t.result_artifact_id AND ae.artifact_sha256=t.result_artifact_sha256)
              AND NOT EXISTS (SELECT 1 FROM evaluation.assessment_evidence ae LEFT JOIN evaluation.artifacts a
                ON a.workspace_id=ae.workspace_id AND a.project_id=ae.project_id AND a.id=ae.artifact_id
                  AND a.sha256=ae.artifact_sha256 AND a.redacted_at IS NULL
                WHERE ae.workspace_id=s.workspace_id AND ae.project_id=s.project_id AND ae.assessment_id=s.id AND a.id IS NULL)
            FROM evaluation.assessments s WHERE s.workspace_id=@workspace AND s.project_id=@project
              AND s.case_id=@case AND s.trial_id=@trial AND s.criterion=@criterion
            ORDER BY s.id LIMIT 10001
            """))
        {
            query.Parameters.AddWithValue("case", caseId);
            query.Parameters.AddWithValue("criterion", request.Criterion);
            await using var reader = await query.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                evidence.Add(new(reader.GetGuid(0), caseId, request.TrialId, request.Criterion,
                    new(reader.GetGuid(1), reader.GetString(2).Trim()), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetBoolean(6)));
        }
        if (evidence.Count > 10000) throw new InvalidDataException("Assessment history exceeds supported selection bound; no partial selection allowed.");
        var selection = TrialAssessmentSelection.Resolve(caseId, request.TrialId, request.Criterion, evaluator, evidence);
        return new(request.BindingId, "evaluator_declaration", caseId, evaluator, selection,
            Array.AsReadOnly(evidence.Select(e => e.Id).ToArray())) { Request = request };

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("experiment", request.ExperimentId);
            command.Parameters.AddWithValue("trial", request.TrialId);
            command.Parameters.AddWithValue("binding", request.BindingId);
            return command;
        }
    }
}
