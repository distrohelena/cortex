using System.Text.Json;
using Filomena.Evaluation.Contracts;

namespace Filomena.Evaluation.Comparison;

public sealed record IssueHistoryReport(Guid ReportId, string Sha256, string Status);
public sealed record IssueHistoryGroup(string CohortSha256, string Status, IReadOnlyList<Guid> ReportIds,
    int UniqueTrials, IReadOnlyList<IssueSummary> Items);
public sealed record IssueHistoryResult(string Status, IReadOnlyList<IssueHistoryReport> Reports,
    IReadOnlyList<IssueHistoryGroup> Groups)
{
    public string Method => "same-plan-policy-inputs,unique-trial,conflicts-withheld:v1";
}

public static class IssueHistoryProjection
{
    // Pure projection over reports resolved by the scoped host reader. This is
    // not a substitute for payload integrity or current source-access checks.
    public static IssueHistoryResult Create(IReadOnlyList<StoredComparisonReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count is < 1 or > 20 || reports.Any(r => r is null) ||
            reports.Select(r => r.ReportId).Distinct().Count() != reports.Count)
            throw new InvalidDataException("One to twenty distinct reports required.");
        var checkedReports = new List<(string Cohort, StoredComparisonReport Report)>();
        foreach (var report in reports)
        {
            _ = IssueSummaryProjection.Create(report); // Validate each original report before deduplication.
            if (report.Status != "available") continue;
            var snapshot = report.Snapshot!;
            if (snapshot.PlanSha256 is not { Length: 64 } ||
                snapshot.PlanSha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)))
                throw new InvalidDataException("Invalid plan provenance.");
            ComparisonPlan.Validate(new(snapshot.ExperimentId, snapshot.Report.PolicyRevision, snapshot.Inputs));
            // Plan hash already pins manifest + ordered cases, including target,
            // dataset, evaluator, candidates, budget/stop rules and repetitions.
            // Do not broaden cohorts using only candidate names or text similarity.
            var cohort = RevisionHasher.Hash(new RevisionDocument(1, "issue-history-cohort", JsonSerializer.SerializeToElement(new
            {
                snapshot.PlanSha256,
                snapshot.Report.PolicyRevision,
                Inputs = snapshot.Inputs.OrderBy(i => i.InputRevision.Id).ThenBy(i => i.BindingId).ToArray()
            })));
            checkedReports.Add((cohort, report));
        }
        var groups = new List<IssueHistoryGroup>();
        foreach (var cohort in checkedReports.GroupBy(r => r.Cohort).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var trials = new Dictionary<Guid, (Guid Experiment, TrialEvidenceGate Trial, string Hash)>();
            var conflict = false;
            foreach (var (_, report) in cohort)
            {
                var snapshot = report.Snapshot!;
                foreach (var trial in snapshot.Report.Trials)
                {
                    var hash = RevisionHasher.Hash(new RevisionDocument(1, "issue-history-trial", JsonSerializer.SerializeToElement(trial)));
                    if (trials.TryGetValue(trial.TrialId, out var previous))
                    {
                        if (previous.Experiment != snapshot.ExperimentId || previous.Hash != hash) conflict = true;
                    }
                    else trials.Add(trial.TrialId, (snapshot.ExperimentId, trial, hash));
                }
            }
            var assessments = new HashSet<Guid>();
            var attempts = new HashSet<Guid>();
            var cells = new HashSet<(Guid, string, EvaluationCell)>();
            var criticality = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var (experiment, trial, _) in trials.Values)
            {
                if (!cells.Add((experiment, trial.CandidateRevision, trial.Cell))) conflict = true;
                foreach (var criterion in trial.Criteria)
                {
                    if (criticality.TryGetValue(criterion.Id, out var critical) && critical != criterion.Critical) conflict = true;
                    else criticality[criterion.Id] = criterion.Critical;
                }
                foreach (var id in trial.Criteria.Where(c => c.AssessmentId.HasValue).Select(c => c.AssessmentId!.Value))
                    if (!assessments.Add(id)) conflict = true;
                if (trial.Measurement.AttemptIds is null) conflict = true;
                else foreach (var id in trial.Measurement.AttemptIds)
                        if (id == Guid.Empty || !attempts.Add(id)) conflict = true;
            }
            var items = conflict ? Array.Empty<IssueSummary>() : IssueSummaryProjection.SummarizeRows(
                trials.Values.SelectMany(t => t.Trial.Criteria.Select(c => (t.Trial, c))));
            groups.Add(new(cohort.Key, conflict ? "conflicting_evidence" : "available",
                Array.AsReadOnly(cohort.Select(r => r.Report.ReportId).Order().ToArray()), trials.Count, items));
        }
        return new(reports.Any(r => r.Status != "available") || groups.Any(g => g.Status != "available") ? "partial" : "available",
            Array.AsReadOnly(reports.OrderBy(r => r.ReportId).Select(r => new IssueHistoryReport(r.ReportId, r.Sha256, r.Status)).ToArray()),
            groups.AsReadOnly());
    }
}
