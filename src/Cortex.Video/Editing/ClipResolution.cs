namespace Cortex.Video.Editing;

public enum ClipMatchOutcome { Match, NoMatch, Uncertain }
public sealed record ClipMatchAssessment(string CandidateId, ClipMatchOutcome Outcome, double Score, string Method);
public interface IClipMatcher
{
    Task<IReadOnlyList<ClipMatchAssessment>> MatchAsync(ClipSearch search, IReadOnlyList<TakeCandidate> candidates, CancellationToken token);
}
public interface IVideoSnapshotReader
{
    Task<MediaSnapshot> ReadAsync(string path, EditSource source, CancellationToken token);
}
public sealed record ClipResolution(EditPlanDocument Plan, RecordingTasksDocument Tasks);
