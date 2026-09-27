using Filomena.Evaluation.Contracts;
using System.Security.Cryptography;
using System.Text.Json;

namespace Filomena.Evaluation.Execution;

public sealed record ScheduledTrial(string TrialKey, ScopedRevisionRef Candidate, ScopedRevisionRef Case, int Repetition);

public static class ExperimentSchedule
{
    public static IReadOnlyList<ScheduledTrial> Create(ExperimentManifest manifest, IReadOnlyList<ScopedRevisionRef> cases,
        int maximumTrials = 100_000)
    {
        if (ManifestValidator.Validate(manifest).Count != 0 || cases.Count == 0 || maximumTrials < 1)
            throw new InvalidDataException("Invalid experiment schedule input.");
        var issues = new List<ValidationIssue>();
        foreach (var item in cases) ManifestValidator.AddReferenceIssue(item, manifest.Scope, "case", issues);
        if (issues.Count != 0 || cases.Select(c => c.Revision.Id).Distinct().Count() != cases.Count)
            throw new InvalidDataException("Case revisions must be distinct and scoped to the experiment.");
        var candidates = new List<ScopedRevisionRef> { manifest.Baseline };
        foreach (var candidate in manifest.Candidates)
        {
            if (candidate == manifest.Baseline) continue;
            if (candidates.Any(c => c.Revision.Id == candidate.Revision.Id))
                throw new InvalidDataException("Ambiguous candidate revision.");
            candidates.Add(candidate);
        }
        var count = (long)cases.Count * manifest.Repetitions * candidates.Count;
        // At least one physical call per trial; multi-call recipe budget checks still happen later.
        if (count > maximumTrials || count > manifest.Budgets.MaxCalls)
            throw new InvalidDataException("Schedule exceeds host capacity or minimum physical-call budget.");
        var planHash = RevisionHasher.Hash(new RevisionDocument(1, "experiment-schedule",
            JsonSerializer.SerializeToElement(new { manifest, cases })));
        var blocks = cases.SelectMany(c => Enumerable.Range(0, manifest.Repetitions).Select(r => (Case: c, Repetition: r)))
            .OrderBy(b => Key(new { manifest.Seed, Case = b.Case.Revision.Id, b.Repetition }), StringComparer.Ordinal).ToArray();
        var orderedCandidates = candidates.OrderBy(c => Key(new { manifest.Seed, Candidate = c.Revision.Id }), StringComparer.Ordinal).ToArray();
        var result = new List<ScheduledTrial>((int)count);
        void Add(ScopedRevisionRef candidate, ScopedRevisionRef item, int repetition) =>
            result.Add(new(Key(new { planHash, candidate, item, repetition }), candidate, item, repetition));
        if (manifest.Order == CandidateOrder.Sequential)
        {
            foreach (var candidate in orderedCandidates)
                foreach (var block in blocks) Add(candidate, block.Case, block.Repetition);
        }
        else
        {
            foreach (var block in blocks)
                foreach (var candidate in orderedCandidates.OrderBy(c =>
                    Key(new { manifest.Seed, Case = block.Case.Revision.Id, block.Repetition, Candidate = c.Revision.Id }), StringComparer.Ordinal))
                    Add(candidate, block.Case, block.Repetition);
        }
        return result.AsReadOnly();
    }

    private static string Key<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
