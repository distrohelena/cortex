namespace Cortex.Video;

public sealed record TakeSource(double Start, double End, IReadOnlyList<int> SegmentIds);
public sealed record TakeAssessment(double DeliveryScore, double ContentScore);
public sealed record TakeCandidate(string Id, TakeSource Source, string Text, string GroupId = "", TakeAssessment? Assessment = null, bool Keep = false, string Reason = "");
public sealed record TakeGroup(string Id, IReadOnlyList<string> CandidateIds, string SuggestedId);
public sealed record ProposedTakeClip(string CandidateId, double SourceStart, double SourceEnd, double Start, double End);
public sealed record TakeRepeatEvidence(string FirstId, string SecondId, bool SameSpeech, double Probability);
public sealed record TakeEditPlan(int Version, string Status, string Timebase, string AssessmentBasis, string Goal,
    string SourceTranscriptHash, string Provider, string Model, IReadOnlyList<TakeCandidate> Candidates,
    IReadOnlyList<TakeGroup> Groups, IReadOnlyList<TakeRepeatEvidence> RepeatEvidence, IReadOnlyList<ProposedTakeClip> ProposedTimeline);

public interface ITakeSuggester
{
    Task<TakeEditPlan> SuggestAsync(TranscriptResult transcript, string goal, CancellationToken token);
}
