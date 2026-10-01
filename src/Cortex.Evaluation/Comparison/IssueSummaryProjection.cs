namespace Cortex.Evaluation.Comparison;

public sealed record IssueEvidence(Guid TrialId, Guid AssessmentId, string CaseRevision, int Repetition);
public sealed record IssueSummary(string CandidateRevision, string Criterion, int AssessedTrials,
    int FailedTrials, int FailedCases, int CriticalFailures, int UnscoredTrials, int UnresolvedTrials,
    double? FailureRate, int? MaximumFailureSeverity, decimal? EstimatedFailedTrialCost, string? Currency,
    IReadOnlyList<IssueEvidence> Evidence);
public sealed record IssueQueryResult(Guid ReportId, string ReportSha256, string Status, IReadOnlyList<IssueSummary> Items)
{
    public string Method => "critical-failures,cases,failures,severity,identity:v1";
}

public static class IssueSummaryProjection
{
    public static IssueQueryResult Create(StoredComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.ReportId == Guid.Empty || report.Sha256 is not { Length: 64 } ||
            report.Sha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)))
            throw new InvalidDataException("Invalid report identity.");
        if (report.Status is "evidence_unavailable" or "invalid_record")
            return new(report.ReportId, report.Sha256, report.Status, []);
        if (report.Status != "available" || report.Snapshot?.Report?.Trials is not { } trials)
            throw new InvalidDataException("Available comparison evidence required.");
        var trialIds = new HashSet<Guid>();
        var assessmentIds = new HashSet<Guid>();
        var cells = new HashSet<(string, EvaluationCell)>();
        var rows = new List<(TrialEvidenceGate Trial, ComparisonCriterionEvidence Criterion)>();
        foreach (var trial in trials)
        {
            if (trial is null || trial.TrialId == Guid.Empty || !trialIds.Add(trial.TrialId) ||
                string.IsNullOrWhiteSpace(trial.CandidateRevision) || trial.Cell is null ||
                string.IsNullOrWhiteSpace(trial.Cell.CaseRevision) || trial.Cell.Repetition < 0 ||
                !cells.Add((trial.CandidateRevision, trial.Cell)) || trial.Measurement is null ||
                trial.Measurement.TrialId != trial.TrialId || trial.Criteria is null || trial.AssessmentIds is null)
                throw new InvalidDataException("Invalid or duplicated issue trial.");
            var criteria = new HashSet<string>(StringComparer.Ordinal);
            foreach (var criterion in trial.Criteria)
            {
                if (criterion is null || string.IsNullOrWhiteSpace(criterion.Id) || !criteria.Add(criterion.Id) ||
                    criterion.Status is not ("resolved" or "assessment_missing" or "assessment_binding_mismatch" or "assessment_unresolved"))
                    throw new InvalidDataException("Invalid or duplicated issue criterion.");
                if (criterion.Status == "resolved")
                {
                    if (criterion.Outcome is not ("pass" or "fail" or "unscored") || criterion.Severity is < 0 or > 5 ||
                        criterion.AssessmentId is not Guid id || id == Guid.Empty || !assessmentIds.Add(id) || !trial.AssessmentIds.Contains(id))
                        throw new InvalidDataException("Invalid or reused assessment evidence.");
                }
                else if (criterion.Outcome is not null || criterion.AssessmentId is not null || criterion.Severity is not null)
                    throw new InvalidDataException("Unresolved evidence cannot carry a selected judgment.");
                rows.Add((trial, criterion));
            }
        }
        return new(report.ReportId, report.Sha256, "available", SummarizeRows(rows));
    }

    internal static IReadOnlyList<IssueSummary> SummarizeRows(
        IEnumerable<(TrialEvidenceGate Trial, ComparisonCriterionEvidence Criterion)> rows)
    {
        var items = rows.GroupBy(r => (r.Trial.CandidateRevision, r.Criterion.Id)).Select(group =>
        {
            if (group.Select(r => r.Criterion.Critical).Distinct().Count() != 1)
                throw new InvalidDataException("A criterion cannot have different criticality within one policy.");
            var failed = group.Where(r => r.Criterion.Outcome == "fail").ToArray();
            var assessed = group.Count(r => r.Criterion.Outcome is "pass" or "fail");
            var currencies = failed.Select(r => r.Trial.Measurement.Currency).Distinct(StringComparer.Ordinal).ToArray();
            decimal? cost = null;
            var currency = currencies.Length == 1 ? currencies[0] : null;
            if (failed.Length > 0 && currency is not null && failed.All(r => r.Trial.Measurement.EstimatedCost is not null))
            {
                try { cost = failed.Sum(r => r.Trial.Measurement.EstimatedCost!.Value); }
                catch (OverflowException) { /* Unknown is not zero or a partial sum. */ }
            }
            return new IssueSummary(group.Key.CandidateRevision, group.Key.Id, assessed, failed.Length,
                failed.Select(r => r.Trial.Cell.CaseRevision).Distinct(StringComparer.Ordinal).Count(),
                failed.Count(r => r.Criterion.Critical), group.Count(r => r.Criterion.Outcome == "unscored"),
                group.Count(r => r.Criterion.Status != "resolved"), assessed == 0 ? null : (double)failed.Length / assessed,
                failed.Select(r => r.Criterion.Severity).DefaultIfEmpty().Max(), cost, currency,
                Array.AsReadOnly(failed.Select(r => new IssueEvidence(r.Trial.TrialId, r.Criterion.AssessmentId!.Value,
                    r.Trial.Cell.CaseRevision, r.Trial.Cell.Repetition)).OrderBy(e => e.TrialId).ToArray()));
        }).OrderByDescending(i => i.CriticalFailures).ThenByDescending(i => i.FailedCases)
            .ThenByDescending(i => i.FailedTrials).ThenByDescending(i => i.MaximumFailureSeverity)
            .ThenBy(i => i.CandidateRevision, StringComparer.Ordinal).ThenBy(i => i.Criterion, StringComparer.Ordinal).ToArray();
        return Array.AsReadOnly(items);
    }
}
