using System.Data;
using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Npgsql;

namespace Cortex.Evaluation.Comparison;

public sealed record ExperimentComparisonSnapshot(Guid ExperimentId, string PlanSha256,
    IReadOnlyList<ComparisonInputBinding> Inputs, EvidenceComparisonReport Report)
{
    public IReadOnlyList<AttemptPriceProjectionEvidence> PriceProjections { get; init; } = Array.Empty<AttemptPriceProjectionEvidence>();
    public IReadOnlyList<EvidenceArtifactRef> SourceEvidence { get; init; } = Array.Empty<EvidenceArtifactRef>();
}
public sealed record AttemptPriceProjectionEvidence(Guid AttemptId, RevisionRef PriceRevision, CostEstimate Estimate)
{
    public string Origin => "evaluator_declaration";
}

/// <summary>One scoped database read snapshot. Does not persist a report, price unknown usage or authorize activation.</summary>
public sealed class ExperimentComparisonReader(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public async Task<ExperimentComparisonSnapshot?> ReadAsync(EvaluationAccess access,
        ExperimentComparisonRequest request, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        ComparisonPlan.Validate(request);
        // Snapshot caller-owned collections before the first await.
        request = request with
        {
            Inputs = Array.AsReadOnly(request.Inputs.ToArray()),
            Prices = Array.AsReadOnly(request.Prices.ToArray())
        };
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await mode.ExecuteNonQueryAsync(token);
        var snapshot = await ReadInSnapshotAsync(access, request, connection, transaction, token);
        await transaction.CommitAsync(token);
        return snapshot;
    }

    internal static async Task<ExperimentComparisonSnapshot?> ReadInSnapshotAsync(EvaluationAccess access,
        ExperimentComparisonRequest request, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        ComparisonPlan.Validate(request);
        ExperimentManifest manifest;
        string planHash;
        await using (var query = Command("""
            SELECT manifest::text,plan_sha256,evaluator_revision_id,evaluator_revision_sha256
            FROM evaluation.experiments WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment
            """))
        {
            await using var reader = await query.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            if (reader.IsDBNull(1)) throw new InvalidDataException("Unplanned experiment cannot be compared.");
            using var json = JsonDocument.Parse(reader.GetString(0));
            RevisionHasher.ValidateJson(json.RootElement, 0);
            manifest = json.RootElement.Deserialize<ExperimentManifest>(WebJson)
                ?? throw new InvalidDataException("Experiment manifest is unavailable.");
            planHash = reader.GetString(1).Trim();
            if (manifest.Evaluator is null || manifest.Evaluator.Revision != new RevisionRef(reader.GetGuid(2), reader.GetString(3).Trim()))
                throw new InvalidDataException("Experiment evaluator binding is inconsistent.");
        }
        access.DemandScope(manifest.Scope);
        TrialComparisonPolicy policy;
        await using (var query = Command("""
            SELECT document_bytes FROM evaluation.revisions
            WHERE workspace_id=@workspace AND project_id=@project AND id=@policy AND sha256=@hash
            """))
        {
            query.Parameters.AddWithValue("policy", request.PolicyRevision.Id);
            query.Parameters.AddWithValue("hash", request.PolicyRevision.Sha256);
            if (await query.ExecuteScalarAsync(token) is not byte[] bytes) return null;
            using var json = JsonDocument.Parse(bytes);
            RevisionHasher.ValidateJson(json.RootElement, 0);
            var root = json.RootElement;
            var document = new RevisionDocument(root.GetProperty("contractVersion").GetInt32(),
                root.GetProperty("kind").GetString()!, root.GetProperty("payload"));
            if (document.Kind != "trial-comparison-policy" || RevisionHasher.Hash(document) != request.PolicyRevision.Sha256)
                throw new InvalidDataException("Comparison policy is unavailable or invalid.");
            policy = document.Payload.Deserialize<TrialComparisonPolicy>()
                ?? throw new InvalidDataException("Comparison policy is unavailable.");
            if (policy.EvaluatorRevision != manifest.Evaluator.Revision)
                throw new InvalidDataException("Comparison policy must use the experiment's pinned evaluator.");
        }
        var rows = new List<StoredComparisonTrial>();
        await using (var query = Command("""
            SELECT id,schedule_ordinal,trial_key,candidate_revision_id,candidate_revision_sha256,
                   case_revision_id,case_revision_sha256,repetition
            FROM evaluation.trials WHERE workspace_id=@workspace AND project_id=@project AND experiment_id=@experiment
            ORDER BY schedule_ordinal,id LIMIT 100001
            """))
        {
            await using var reader = await query.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (Enumerable.Range(1, 7).Any(reader.IsDBNull))
                    throw new InvalidDataException("Trial has no complete pinned schedule.");
                rows.Add(new(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2).Trim(),
                    new(reader.GetGuid(3), reader.GetString(4).Trim()), new(reader.GetGuid(5), reader.GetString(6).Trim()), reader.GetInt32(7)));
            }
        }
        var schedule = ComparisonPlan.Resolve(access, request, manifest, planHash, rows);
        var candidates = schedule.Select(t => t.Candidate.Revision.Sha256).Distinct(StringComparer.Ordinal).ToArray();
        var cells = schedule.Select(t => new EvaluationCell(t.Case.Revision.Sha256, t.Repetition)).Distinct().ToArray();
        // Validate policy before reading any result payload or iterating required criteria.
        _ = TrialComparisonAssembler.Assemble(request.ExperimentId, candidates, cells, [], request.PolicyRevision, policy);
        var bindings = request.Inputs.ToDictionary(i => i.InputRevision, i => i.BindingId);
        var priceBindings = request.Prices.ToDictionary(p => p.AttemptId, p => p.PriceRevision);
        var priceTerms = new Dictionary<RevisionRef, AttemptPriceTerms>();
        foreach (var reference in request.Prices.Select(p => p.PriceRevision).Distinct())
        {
            await using var query = Command("""
                SELECT document_bytes FROM evaluation.revisions
                WHERE workspace_id=@workspace AND project_id=@project AND id=@price AND sha256=@hash
                """);
            query.Parameters.AddWithValue("price", reference.Id);
            query.Parameters.AddWithValue("hash", reference.Sha256);
            if (await query.ExecuteScalarAsync(token) is not byte[] bytes) return null;
            using var json = JsonDocument.Parse(bytes);
            RevisionHasher.ValidateJson(json.RootElement, 0);
            var root = json.RootElement;
            var document = new RevisionDocument(root.GetProperty("contractVersion").GetInt32(),
                root.GetProperty("kind").GetString()!, root.GetProperty("payload"));
            if (document.Kind != "attempt-price-terms" || RevisionHasher.Hash(document) != reference.Sha256)
                throw new InvalidDataException("Price revision is unavailable or invalid.");
            priceTerms.Add(reference, document.Payload.Deserialize<AttemptPriceTerms>()
                ?? throw new InvalidDataException("Price terms are unavailable."));
        }
        var priceEvidence = new List<AttemptPriceProjectionEvidence>();
        var trials = new List<ResolvedComparisonTrial>();
        foreach (var row in rows)
        {
            var measurement = await TrialMeasurementReader.ReadInSnapshotAsync(access, request.ExperimentId, row.TrialId,
                connection, transaction, token) ?? throw new InvalidDataException("Scheduled trial measurement unavailable.");
            var attempts = measurement.Attempts.Select(attempt =>
            {
                if (!priceBindings.TryGetValue(attempt.AttemptId, out var price)) return attempt;
                var estimate = AttemptPriceProjection.Calculate(attempt, price, priceTerms[price]);
                priceEvidence.Add(new(attempt.AttemptId, price, estimate));
                return attempt with { Cost = estimate };
            }).ToArray();
            measurement = measurement with
            {
                Attempts = Array.AsReadOnly(attempts),
                Measurement = TrialMeasurement.Aggregate(row.TrialId, measurement.Measurement.PipelineElapsed, attempts)
            };
            var assessments = new Dictionary<string, TrialAssessmentSnapshot>(StringComparer.Ordinal);
            foreach (var criterion in policy.Criteria)
            {
                var selection = await TrialAssessmentReader.ReadInSnapshotAsync(access,
                    new(request.ExperimentId, row.TrialId, bindings[row.Input], criterion.Id), connection, transaction, token);
                // A foreign/redacted/mismatched binding invalidates this scoped read; do not
                // turn inaccessible case evidence into an anonymous low-quality observation.
                if (selection is null) return null;
                assessments.Add(criterion.Id, selection);
            }
            trials.Add(new(row.TrialId, row.Candidate.Sha256, new(row.Input.Sha256, row.Repetition), measurement, assessments));
        }
        if (priceEvidence.Count != priceBindings.Count)
            throw new InvalidDataException("Price binding does not belong to this experiment's physical attempts.");
        var report = TrialComparisonAssembler.Assemble(request.ExperimentId, candidates, cells, trials, request.PolicyRevision, policy);
        var sourceEvidence = new List<EvidenceArtifactRef>();
        await using (var query = Command("""
            SELECT artifact_id,sha256 FROM (
              SELECT b.case_artifact_id AS artifact_id,b.case_artifact_sha256 AS sha256
              FROM evaluation.case_input_bindings b WHERE b.workspace_id=@workspace AND b.project_id=@project AND b.id=ANY(@bindings)
              UNION
              SELECT t.result_artifact_id,t.result_artifact_sha256 FROM evaluation.trials t
              WHERE t.workspace_id=@workspace AND t.project_id=@project AND t.experiment_id=@experiment AND t.result_artifact_id IS NOT NULL
              UNION
              SELECT ae.artifact_id,ae.artifact_sha256 FROM evaluation.assessment_evidence ae
              WHERE ae.workspace_id=@workspace AND ae.project_id=@project AND ae.assessment_id=ANY(@assessments)
            ) evidence ORDER BY artifact_id,sha256
            """))
        {
            query.Parameters.AddWithValue("bindings", request.Inputs.Select(i => i.BindingId).ToArray());
            query.Parameters.AddWithValue("assessments", trials.SelectMany(t => t.Assessments.Values)
                .SelectMany(a => a.AssessmentIds).Distinct().ToArray());
            await using var reader = await query.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) sourceEvidence.Add(new(reader.GetGuid(0), reader.GetString(1).Trim()));
        }
        return new(request.ExperimentId, planHash, request.Inputs, report)
        {
            PriceProjections = priceEvidence.AsReadOnly(),
            SourceEvidence = sourceEvidence.AsReadOnly()
        };

        NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            command.Parameters.AddWithValue("project", access.Scope.ProjectId);
            command.Parameters.AddWithValue("experiment", request.ExperimentId);
            return command;
        }
    }
}
