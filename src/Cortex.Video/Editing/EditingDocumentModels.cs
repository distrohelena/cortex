using System.Text.Json;

namespace Cortex.Video.Editing;

public abstract record EditingDocument
{
    public required string Schema { get; init; }
    public JsonElement? Metadata { get; init; }
}

public sealed record EditScriptDocument : EditingDocument
{
    public required int Revision { get; init; }
    public required EditProject Project { get; init; }
    public required IReadOnlyList<EditSource> Sources { get; init; }
    public required IReadOnlyList<EditBeat> Beats { get; init; }
}

public abstract record GeneratedEditingDocument : EditingDocument
{
    public required string ProjectId { get; init; }
    public required EditingDocumentReference ScriptRef { get; init; }
    public required string ResolutionId { get; init; }
}

public sealed record EditPlanDocument : GeneratedEditingDocument
{
    public required PlanStatus Status { get; init; }
    public required IReadOnlyList<MediaSnapshot> Media { get; init; }
    public required IReadOnlyList<BeatDecision> Beats { get; init; }
    public required IReadOnlyList<JsonElement> UnresolvedRequirements { get; init; }
}

public sealed record RecordingTasksDocument : GeneratedEditingDocument
{
    public required IReadOnlyList<RecordingTask> RecordingTasks { get; init; }
}

public sealed record ResolvedEditPlanDocument : GeneratedEditingDocument
{
    public required string Status { get; init; }
    public required string Timebase { get; init; }
    public required ReviewStatus ReviewStatus { get; init; }
    public required IReadOnlyList<MediaSnapshot> Media { get; init; }
    public required IReadOnlyList<TimelineItem> Timeline { get; init; }
    public required double DurationSec { get; init; }
    public double? TargetDurationSec { get; init; }
    public required IReadOnlyList<string> RenderRequirements { get; init; }
    public required IReadOnlyList<RecordingTaskResolution> RecordingTaskResolutions { get; init; }
    public required EditingDocumentReference EditPlanRef { get; init; }
    public required EditingDocumentReference RecordingTasksRef { get; init; }
    public IReadOnlyList<EditingNotice>? Notices { get; init; }
}

public sealed record EditingDocumentReference
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public int? Revision { get; init; }
    public string? ResolutionId { get; init; }
}

public sealed record EditingNotice(string Code, string Message);
public enum PlanStatus { Incomplete, Complete, Blocked, Stale }
public enum DecisionStatus { Matched, IntentionalNone, NeedsRecording, NeedsMedia, NeedsAlignment, NeedsReview, Blocked, Stale }
public enum TaskStatus { NeedsRecording, Recorded, NeedsReview, Resolved, Stale }
public enum ReviewStatus { Pending, Accepted, Rejected }
public enum MatchMode { Exact, Semantic, Intent }
public enum SourceKind { VideoFile, VideoPool, Image }
public enum AudioKind { None, FindClip, Record }
public enum VisualKind { Asset, Speaker, SpeakerWithOverlay, Graphic, TitleCard }
public enum BindingKind { None, Clip, Image, Graphic, TitleCard }
public enum MediaKind { Video, Image, Audio }
public enum TimingGranularity { Segment, Word, Frame }
public enum ProjectFormat { Vertical, Horizontal, Square }
public enum RecordingReason { NoSuitableClipFound, ScriptRequiresExactWording, ScriptRequestsRecording }
