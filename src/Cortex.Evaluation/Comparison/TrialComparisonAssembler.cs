using System.Text.Json;
using Cortex.Evaluation.Contracts;

namespace Cortex.Evaluation.Comparison;

public sealed record RequiredComparisonCriterion(string Id, bool Critical);
public sealed record TrialComparisonPolicy(int ContractVersion, RevisionRef EvaluatorRevision,
    IReadOnlyList<RequiredComparisonCriterion> Criteria, PairedBootstrapPolicy? Paired = null)
{
    public RevisionDocument ToRevisionDocument() => new(ContractVersion, "trial-comparison-policy", JsonSerializer.SerializeToElement(this));
}
public sealed record ResolvedComparisonTrial(Guid TrialId, string CandidateRevision, EvaluationCell Cell,
    TrialMeasurementSnapshot Measurement, IReadOnlyDictionary<string, TrialAssessmentSnapshot> Assessments);
public sealed record ComparisonCriterionEvidence(string Id, bool Critical, string Status,
    Guid? AssessmentId, string? Outcome, int? Severity);
public sealed record TrialEvidenceGate(Guid TrialId, string CandidateRevision, EvaluationCell Cell,
    IReadOnlyList<string> Failures, IReadOnlyList<Guid> AssessmentIds, IReadOnlyList<Guid> AttemptIds,
    IReadOnlyList<ComparisonCriterionEvidence> Criteria, TrialMeasurement Measurement);
public sealed record EvidenceComparisonReport(RevisionRef PolicyRevision, ComparisonReport Statistics, IReadOnlyList<TrialEvidenceGate> Trials);

public static class TrialComparisonAssembler
{
    public static EvidenceComparisonReport Assemble(Guid experimentId, IReadOnlyList<string> candidates,
        IReadOnlyList<EvaluationCell> cells, IReadOnlyList<ResolvedComparisonTrial> trials,
        RevisionRef policyRevision, TrialComparisonPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(trials);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policyRevision);
        if (experimentId == Guid.Empty || policyRevision.Id == Guid.Empty || policy.ContractVersion != 1 ||
            policy.Criteria is null || policy.Criteria.Count == 0 || policy.EvaluatorRevision is null ||
            policy.EvaluatorRevision.Id == Guid.Empty || policy.EvaluatorRevision.Sha256 is not { Length: 64 } ||
            policy.EvaluatorRevision.Sha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)) ||
            policy.Criteria.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 256 || c.Id.Any(char.IsControl)) ||
            policy.Criteria.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != policy.Criteria.Count ||
            RevisionHasher.Hash(policy.ToRevisionDocument()) != policyRevision.Sha256)
            throw new InvalidDataException("Invalid or unpinned comparison policy.");
        _ = ExperimentComparer.Compare(candidates, cells, [], policy.Paired); // Validate the entire expected schedule, including missing trials.
        var logical = new HashSet<Guid>();
        var physical = new HashSet<Guid>();
        var judgments = new HashSet<Guid>();
        var observedCells = new HashSet<(string, EvaluationCell)>();
        var rows = new List<ComparisonTrial>();
        var gates = new List<TrialEvidenceGate>();
        foreach (var trial in trials)
        {
            if (trial is null || trial.TrialId == Guid.Empty || !logical.Add(trial.TrialId) ||
                !candidates.Contains(trial.CandidateRevision, StringComparer.Ordinal) || !cells.Contains(trial.Cell) ||
                !observedCells.Add((trial.CandidateRevision, trial.Cell)) || trial.Measurement is null ||
                trial.Measurement.ExperimentId != experimentId || trial.Measurement.Measurement.TrialId != trial.TrialId ||
                trial.Assessments is null || trial.Assessments.Keys.Any(id => !policy.Criteria.Any(c => c.Id == id)))
                throw new InvalidDataException("Comparison evidence does not match its scheduled trial.");
            // Recalculate from all physical rows, not a caller-provided summarized cost/count.
            var measurement = TrialMeasurement.Aggregate(trial.TrialId, trial.Measurement.Measurement.PipelineElapsed,
                trial.Measurement.Attempts);
            if (!measurement.AttemptIds.SequenceEqual(trial.Measurement.Measurement.AttemptIds) ||
                measurement.AttemptIds.Any(id => !physical.Add(id)))
                throw new InvalidDataException("Physical evidence is missing or reused.");
            var failures = new List<string>();
            if (trial.Measurement.TrialState != "completed") failures.Add("execution_incomplete");
            if (trial.Measurement.ResultEvidenceStatus != "available" || measurement.PipelineElapsed is null) failures.Add("result_unavailable");
            if (measurement.PhysicalCalls == 0 || measurement.HasUnresolvedAttempts) failures.Add("accounting_incomplete");
            var selected = new List<(RequiredComparisonCriterion Policy, TrialAssessmentSelection Selection)>();
            var criterionEvidence = new List<ComparisonCriterionEvidence>();
            Guid? binding = null;
            Guid? caseId = null;
            foreach (var criterion in policy.Criteria)
            {
                if (!trial.Assessments.TryGetValue(criterion.Id, out var assessment) || assessment is null)
                {
                    failures.Add($"assessment_missing:{criterion.Id}");
                    criterionEvidence.Add(new(criterion.Id, criterion.Critical, "assessment_missing", null, null, null));
                    continue;
                }
                if (assessment.Request is not { } request || request.ExperimentId != experimentId || request.TrialId != trial.TrialId ||
                    request.Criterion != criterion.Id || request.BindingId != assessment.BindingId ||
                    assessment.BindingId == Guid.Empty || assessment.CaseId == Guid.Empty || assessment.BindingOrigin != "evaluator_declaration" ||
                    binding is not null && binding != assessment.BindingId || caseId is not null && caseId != assessment.CaseId ||
                    assessment.EvaluatorRevision != policy.EvaluatorRevision)
                {
                    failures.Add($"assessment_binding_mismatch:{criterion.Id}");
                    criterionEvidence.Add(new(criterion.Id, criterion.Critical, "assessment_binding_mismatch", null, null, null));
                    continue;
                }
                binding = assessment.BindingId;
                caseId = assessment.CaseId;
                var result = assessment.Selection;
                if (result is null || result.Status != "resolved" || result.AssessmentId is null || result.AssessmentId == Guid.Empty ||
                    assessment.AssessmentIds is null || !assessment.AssessmentIds.Contains(result.AssessmentId.Value) ||
                    result.Outcome is not ("pass" or "fail" or "unscored"))
                {
                    failures.Add($"assessment_unresolved:{criterion.Id}");
                    criterionEvidence.Add(new(criterion.Id, criterion.Critical, "assessment_unresolved", null, null, null));
                }
                else
                {
                    if (!judgments.Add(result.AssessmentId.Value))
                        throw new InvalidDataException("Assessment evidence is reused across criteria or trials.");
                    selected.Add((criterion, result));
                    criterionEvidence.Add(new(criterion.Id, criterion.Critical, "resolved", result.AssessmentId, result.Outcome, result.Severity));
                }
            }
            var assessmentIds = selected.Select(s => s.Selection.AssessmentId!.Value).ToArray();
            gates.Add(new(trial.TrialId, trial.CandidateRevision, trial.Cell, failures.AsReadOnly(),
                Array.AsReadOnly(assessmentIds), measurement.AttemptIds, criterionEvidence.AsReadOnly(), measurement));
            if (failures.Count != 0) continue;
            var outcome = selected.Any(s => s.Selection.Outcome == "unscored") ? "unscored"
                : selected.Any(s => s.Selection.Outcome == "fail") ? "fail" : "pass";
            rows.Add(new(trial.CandidateRevision, trial.Cell, measurement.AttemptIds[0], outcome,
                selected.Any(s => s.Policy.Critical && s.Selection.Outcome == "fail"), measurement.PipelineElapsed!.Value.TotalMilliseconds,
                measurement.EstimatedCost, measurement.Currency)
            {
                TrialId = trial.TrialId,
                PhysicalAttempts = measurement.AttemptIds
            });
        }
        // Gated trials remain in the expected schedule and diagnostic list; omission from
        // statistics is incompleteness, not a zero-latency success or cheaper experiment.
        return new(policyRevision, ExperimentComparer.Compare(candidates, cells, rows, policy.Paired), gates.AsReadOnly());
    }
}
