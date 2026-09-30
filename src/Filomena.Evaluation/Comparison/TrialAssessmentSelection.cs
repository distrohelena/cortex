using Filomena.Evaluation.Contracts;

namespace Filomena.Evaluation.Comparison;

public sealed record TrialAssessmentEvidence(Guid Id, Guid CaseId, Guid? TrialId, string Criterion,
    RevisionRef EvaluatorRevision, string Outcome, int? Severity, Guid? Supersedes, bool EvidenceAvailable);
public sealed record TrialAssessmentSelection(string Status, Guid? AssessmentId, string? Outcome, int? Severity)
{
    public string Method => "unique-terminal-supersession:v1";
    public static TrialAssessmentSelection Resolve(Guid caseId, Guid trialId, string criterion,
        RevisionRef evaluatorRevision, IReadOnlyList<TrialAssessmentEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (caseId == Guid.Empty || trialId == Guid.Empty || string.IsNullOrWhiteSpace(criterion) ||
            criterion.Length > 256 || criterion.Any(char.IsControl) || !ValidRevision(evaluatorRevision))
            throw new InvalidDataException("Invalid assessment selection context.");
        var rows = new Dictionary<Guid, TrialAssessmentEvidence>();
        foreach (var row in evidence)
            if (row is null || row.Id == Guid.Empty || row.CaseId != caseId || row.TrialId != trialId ||
                row.Criterion != criterion || !ValidRevision(row.EvaluatorRevision) || row.Severity is < 0 or > 5 ||
                row.Outcome is not ("pass" or "fail" or "unscored") || row.Supersedes == Guid.Empty || !rows.TryAdd(row.Id, row))
                throw new InvalidDataException("Assessment evidence has invalid or mismatched identity.");
        TrialAssessmentSelection Unresolved(string status) => new(status, null, null, null);
        if (rows.Count == 0) return Unresolved("missing");
        var childCounts = rows.Keys.ToDictionary(id => id, _ => 0);
        foreach (var row in rows.Values)
            if (row.Supersedes is Guid previous)
            {
                if (!rows.ContainsKey(previous)) return Unresolved("incomplete_lineage");
                childCounts[previous]++;
            }
        var terminals = rows.Keys.Where(id => childCounts[id] == 0).ToArray();
        var pending = new Queue<Guid>(terminals);
        var visited = 0;
        // Iterative O(N) elimination also catches a hidden disconnected cycle;
        // no recursion or selection based on insertion/time/quality ordering.
        while (pending.TryDequeue(out var id))
        {
            visited++;
            if (rows[id].Supersedes is Guid previous && --childCounts[previous] == 0) pending.Enqueue(previous);
        }
        if (visited != rows.Count) return Unresolved("invalid_lineage");
        if (terminals.Length != 1) return Unresolved("ambiguous");
        var selected = rows[terminals[0]];
        if (selected.EvaluatorRevision != evaluatorRevision) return Unresolved("evaluator_revision_mismatch");
        if (!selected.EvidenceAvailable) return Unresolved("evidence_unavailable");
        return new("resolved", selected.Id, selected.Outcome, selected.Severity);
    }

    private static bool ValidRevision(RevisionRef? revision) => revision is not null && revision.Id != Guid.Empty &&
        revision.Sha256 is { Length: 64 } && revision.Sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
