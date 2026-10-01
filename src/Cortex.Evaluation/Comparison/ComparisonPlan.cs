using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using System.Text.Json;

namespace Cortex.Evaluation.Comparison;

public sealed record ComparisonInputBinding(RevisionRef InputRevision, Guid BindingId);
public sealed record AttemptPriceBinding(Guid AttemptId, RevisionRef PriceRevision);
public sealed record ExperimentComparisonRequest(Guid ExperimentId, RevisionRef PolicyRevision,
    IReadOnlyList<ComparisonInputBinding> Inputs)
{
    public IReadOnlyList<AttemptPriceBinding> Prices { get; init; } = Array.Empty<AttemptPriceBinding>();
}
public sealed record StoredComparisonTrial(Guid TrialId, int Ordinal, string TrialKey, RevisionRef Candidate,
    RevisionRef Input, int Repetition);

public static class ComparisonPlan
{
    public static void Validate(ExperimentComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        static bool Valid(RevisionRef? reference) => reference is not null && reference.Id != Guid.Empty &&
            reference.Sha256 is { Length: 64 } && reference.Sha256.All(c => char.IsAsciiHexDigit(c) && !char.IsUpper(c));
        if (request.ExperimentId == Guid.Empty || !Valid(request.PolicyRevision) || request.Inputs is null ||
            request.Inputs.Count is < 1 or > 100_000 ||
            request.Inputs.Any(i => i is null || i.BindingId == Guid.Empty || !Valid(i.InputRevision)) ||
            request.Inputs.Select(i => i.InputRevision.Id).Distinct().Count() != request.Inputs.Count ||
            request.Inputs.Select(i => i.BindingId).Distinct().Count() != request.Inputs.Count ||
            request.Prices is null || request.Prices.Count > 100_000 ||
            request.Prices.Any(p => p is null || p.AttemptId == Guid.Empty || !Valid(p.PriceRevision)) ||
            request.Prices.Select(p => p.AttemptId).Distinct().Count() != request.Prices.Count)
            throw new ArgumentException("Explicit comparison policy and distinct input bindings required.");
    }

    public static IReadOnlyList<ScheduledTrial> Resolve(EvaluationAccess access, ExperimentComparisonRequest request,
        ExperimentManifest manifest, string planHash, IReadOnlyList<StoredComparisonTrial> rows)
    {
        access.Demand(EvaluationOperation.Evaluate);
        access.DemandScope(manifest.Scope);
        Validate(request);
        var cases = request.Inputs.Select(i => new ScopedRevisionRef(access.Scope, i.InputRevision)).ToArray();
        var actualHash = RevisionHasher.Hash(new RevisionDocument(1, "experiment-plan",
            JsonSerializer.SerializeToElement(new { manifest, cases })));
        if (actualHash != planHash) throw new InvalidDataException("Explicit inputs do not reproduce the stored experiment plan.");
        var schedule = ExperimentSchedule.Create(manifest, cases);
        if (rows.Count != schedule.Count || rows.Any(r => r is null || r.TrialId == Guid.Empty) ||
            rows.Select(r => r.TrialId).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Stored trial schedule is incomplete or duplicated.");
        for (var index = 0; index < schedule.Count; index++)
        {
            var expected = schedule[index];
            var row = rows[index];
            if (row.Ordinal != index || row.TrialKey != expected.TrialKey || row.Candidate != expected.Candidate.Revision ||
                row.Input != expected.Case.Revision || row.Repetition != expected.Repetition)
                throw new InvalidDataException("Stored trial does not match the pinned schedule.");
        }
        return schedule;
    }
}
