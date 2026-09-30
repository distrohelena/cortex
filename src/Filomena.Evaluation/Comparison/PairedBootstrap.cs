namespace Filomena.Evaluation.Comparison;

public sealed record PairedBootstrapPolicy(int ContractVersion, string BaselineRevision, int Seed, int Samples, int MinimumCases);
public sealed record PairedDifference(string BaselineRevision, string CandidateRevision, string Status,
    int CaseCount, double? MeanCorrectnessDifference, double? ConfidenceLow, double? ConfidenceHigh,
    PairedBootstrapPolicy Policy, string Method);

internal static class PairedBootstrap
{
    internal static IReadOnlyList<PairedDifference> Calculate(PairedBootstrapPolicy policy,
        IReadOnlyList<CandidateSummary> summaries, IReadOnlyList<CaseComparisonSummary> cases)
    {
        if (policy.ContractVersion != 1 || policy.Samples is < 100 or > 10000 || policy.MinimumCases < 2 ||
            string.IsNullOrWhiteSpace(policy.BaselineRevision) || !summaries.Any(c => c.Id == policy.BaselineRevision) ||
            (long)policy.Samples * cases.Count * summaries.Count > 10_000_000)
            throw new InvalidDataException("Invalid or excessive paired bootstrap policy.");
        var baseline = summaries.Single(c => c.Id == policy.BaselineRevision);
        var orderedCases = cases.OrderBy(c => c.CaseRevision, StringComparer.Ordinal).ToArray();
        var result = new List<PairedDifference>();
        foreach (var candidate in summaries.Where(c => c.Id != baseline.Id))
        {
            const string method = "paired-case-percentile95-system-random-net10-v1";
            if (!candidate.Eligible || !baseline.Eligible)
            {
                result.Add(new(baseline.Id, candidate.Id, "ineligible_evidence", cases.Count, null, null, null, policy, method));
                continue;
            }
            // Each case is one cluster. All repetitions stay together; both candidates use
            // the same sampled case. This estimates macro-average, not trial-weighted accuracy.
            var differences = orderedCases.Select(c => c.Candidates.Single(s => s.Id == candidate.Id).Correctness!.Value -
                c.Candidates.Single(s => s.Id == baseline.Id).Correctness!.Value).ToArray();
            var mean = differences.Average();
            if (cases.Count < policy.MinimumCases)
            {
                result.Add(new(baseline.Id, candidate.Id, "insufficient_cases", cases.Count, mean, null, null, policy, method));
                continue;
            }
            var random = new Random(policy.Seed);
            var samples = new double[policy.Samples];
            for (var sample = 0; sample < samples.Length; sample++)
            {
                var sum = 0d;
                for (var i = 0; i < differences.Length; i++) sum += differences[random.Next(differences.Length)];
                samples[sample] = sum / differences.Length;
            }
            Array.Sort(samples);
            var low = samples[(int)Math.Ceiling(samples.Length * 0.025) - 1];
            var high = samples[(int)Math.Ceiling(samples.Length * 0.975) - 1];
            result.Add(new(baseline.Id, candidate.Id, "available", cases.Count, mean, low, high, policy, method));
        }
        return result.AsReadOnly();
    }
}
