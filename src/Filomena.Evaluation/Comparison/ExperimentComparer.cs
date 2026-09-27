namespace Filomena.Evaluation.Comparison;

public sealed record EvaluationCell(string CaseRevision, int Repetition);
public sealed record ComparisonTrial(string CandidateRevision, EvaluationCell Cell, Guid PhysicalAttempt,
    string Outcome, bool CriticalFailure, double Milliseconds, decimal? Cost, string Currency);
public sealed record CandidateSummary(string Id, int Observed, int Expected, bool Eligible,
    IReadOnlyList<string> GateFailures, double? Correctness, double? ConfidenceLow, double? ConfidenceHigh,
    double? MedianMilliseconds, double? P95Milliseconds, decimal? MeanCost, string? Currency);
public sealed record ComparisonReport(IReadOnlyList<CandidateSummary> Candidates, IReadOnlyList<string> ParetoCandidates);

public static class ExperimentComparer
{
    public static ComparisonReport Compare(IReadOnlyList<string> candidates, IReadOnlyList<EvaluationCell> cells,
        IReadOnlyList<ComparisonTrial> trials)
    {
        // Pure statistics over already authorized, revision-compatible input. The application must
        // resolve assessments and enforce scope/holdout access before invoking this function.
        var candidateSet = candidates.ToHashSet(StringComparer.Ordinal);
        var cellSet = cells.ToHashSet();
        if (candidateSet.Count == 0 || candidateSet.Count != candidates.Count || candidates.Any(string.IsNullOrWhiteSpace) ||
            cellSet.Count == 0 || cellSet.Count != cells.Count || cells.Any(c => string.IsNullOrWhiteSpace(c.CaseRevision) || c.Repetition < 0))
            throw new InvalidDataException("Invalid paired comparison schedule.");
        var attempts = new HashSet<Guid>();
        var observations = new HashSet<(string, EvaluationCell)>();
        foreach (var trial in trials)
        {
            if (!candidateSet.Contains(trial.CandidateRevision) || !cellSet.Contains(trial.Cell) ||
                trial.PhysicalAttempt == Guid.Empty || !attempts.Add(trial.PhysicalAttempt) ||
                !observations.Add((trial.CandidateRevision, trial.Cell)) ||
                trial.Outcome is not ("pass" or "fail" or "unscored" or "error" or "indeterminate") ||
                !double.IsFinite(trial.Milliseconds) || trial.Milliseconds < 0 || trial.Cost < 0 ||
                trial.Currency.Length != 3 || trial.Currency.Any(c => c is < 'A' or > 'Z') ||
                trial.CriticalFailure && trial.Outcome == "pass")
                throw new InvalidDataException("Invalid or duplicated comparison observation.");
        }
        var summaries = candidates.Select(id => Summarize(id, cells.Count,
            trials.Where(t => t.CandidateRevision == id).ToArray())).ToArray();
        var pareto = summaries.Where(c => c.Eligible && !summaries.Any(other => Dominates(other, c)))
            .Select(c => c.Id).ToArray();
        return new(Array.AsReadOnly(summaries), Array.AsReadOnly(pareto));
    }

    private static CandidateSummary Summarize(string id, int expected, ComparisonTrial[] trials)
    {
        var failures = new List<string>();
        if (trials.Length != expected) failures.Add("incomplete");
        if (trials.Any(t => t.CriticalFailure)) failures.Add("critical_failure");
        if (trials.Any(t => t.Outcome is "error" or "indeterminate")) failures.Add("execution_failure");
        if (trials.Any(t => t.Outcome == "unscored")) failures.Add("unscored");
        var scored = trials.Where(t => t.Outcome is "pass" or "fail").ToArray();
        var correct = scored.Count(t => t.Outcome == "pass");
        double? correctness = scored.Length == 0 ? null : (double)correct / scored.Length;
        var interval = Wilson(correct, scored.Length);
        var durations = trials.Select(t => t.Milliseconds).Order().ToArray();
        var currencies = trials.Select(t => t.Currency).Distinct(StringComparer.Ordinal).ToArray();
        decimal? mean = trials.Length > 0 && currencies.Length == 1 && trials.All(t => t.Cost is not null)
            ? trials.Average(t => t.Cost!.Value) : null;
        double? median = durations.Length == 0 ? null : durations.Length % 2 == 0
            ? (durations[durations.Length / 2 - 1] + durations[durations.Length / 2]) / 2
            : durations[durations.Length / 2];
        double? p95 = durations.Length == 0 ? null : durations[(int)Math.Ceiling(durations.Length * 0.95) - 1];
        return new(id, trials.Length, expected, failures.Count == 0, failures.AsReadOnly(), correctness,
            interval.Low, interval.High, median, p95, mean, currencies.Length == 1 ? currencies[0] : null);
    }

    private static bool Dominates(CandidateSummary a, CandidateSummary b) =>
        a.Id != b.Id && a.Eligible && b.Eligible && a.Currency is not null && a.Currency == b.Currency &&
        a.MeanCost is not null && b.MeanCost is not null &&
        a.Correctness >= b.Correctness && a.MeanCost <= b.MeanCost && a.MedianMilliseconds <= b.MedianMilliseconds &&
        (a.Correctness > b.Correctness || a.MeanCost < b.MeanCost || a.MedianMilliseconds < b.MedianMilliseconds);

    // Descriptive 95% Wilson interval, not a claim of independent holdout or statistical superiority.
    private static (double? Low, double? High) Wilson(int correct, int count)
    {
        if (count == 0) return (null, null);
        const double z = 1.959963984540054;
        var p = (double)correct / count;
        var denominator = 1 + z * z / count;
        var center = (p + z * z / (2 * count)) / denominator;
        var margin = z * Math.Sqrt(p * (1 - p) / count + z * z / (4d * count * count)) / denominator;
        return (Math.Max(0, center - margin), Math.Min(1, center + margin));
    }
}
