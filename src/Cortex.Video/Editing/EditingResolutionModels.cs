using System.Text.Json;

namespace Cortex.Video.Editing;

public sealed record MediaSnapshot
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public required MediaKind Type { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public double? DurationSec { get; init; }
    public string? Fps { get; init; }
    public string? Speaker { get; init; }
}

public sealed record BeatDecision
{
    public required string BeatId { get; init; }
    public required DecisionStatus Status { get; init; }
    public required AudioDecision Audio { get; init; }
    public required VisualDecision Visual { get; init; }
    public required IReadOnlyList<ClipCandidate> Candidates { get; init; }
    public string? SelectedCandidateId { get; init; }
    public SelectionProvenance? Selection { get; init; }
}

public sealed record AudioDecision
{
    public required DecisionStatus Status { get; init; }
    public string? RecordingTaskId { get; init; }
    public SearchObservation? Search { get; init; }
}
public sealed record SearchObservation(string Status, string Outcome, string Method);
public sealed record VisualDecision
{
    public required DecisionStatus Status { get; init; }
    public required VisualIntent Intent { get; init; }
    public string? MediaId { get; init; }
    public EditFocus? ResolvedFocus { get; init; }
    public IReadOnlyList<JsonElement>? Requirements { get; init; }
}
public sealed record SelectionProvenance(string By, ReviewStatus ReviewStatus);

public sealed record ClipCandidate
{
    public required string Id { get; init; }
    public required string MediaId { get; init; }
    public required double SourceIn { get; init; }
    public required double SourceOut { get; init; }
    public required string Transcript { get; init; }
    public string? Speaker { get; init; }
    public required TimingGranularity TimingGranularity { get; init; }
    public required JsonElement Match { get; init; }
    public required int Rank { get; init; }
    public required string Reason { get; init; }
}

public sealed record RecordingTask
{
    public required string Id { get; init; }
    public required string BeatId { get; init; }
    public required RecordingReason Reason { get; init; }
    public string? Script { get; init; }
    public string? Brief { get; init; }
    public required JsonElement Match { get; init; }
    public double? TargetDurationSec { get; init; }
    public required RecordingProfile Recording { get; init; }
    public required TaskStatus Status { get; init; }
    public PickupResolution? ResolvedBy { get; init; }
}

public sealed record PickupResolution
{
    public required string MediaId { get; init; }
    public required string SourceId { get; init; }
    public required string Path { get; init; }
    public required double SourceIn { get; init; }
    public required double SourceOut { get; init; }
    public required string Transcript { get; init; }
    public required TimingGranularity TimingGranularity { get; init; }
    public required JsonElement Match { get; init; }
    public required SelectionProvenance Selection { get; init; }
}
public sealed record RecordingTaskResolution(string RecordingTask, TaskStatus Status, PickupResolution ResolvedBy);

public sealed record TimelineItem
{
    public required string BeatId { get; init; }
    public required double Start { get; init; }
    public required double End { get; init; }
    public required MediaBinding Audio { get; init; }
    public required MediaBinding Video { get; init; }
    public required string Enter { get; init; }
    public required string Exit { get; init; }
    public JsonElement? Match { get; init; }
    public IReadOnlyList<EditOverlay>? Overlays { get; init; }
}

public sealed record MediaBinding
{
    public required BindingKind Kind { get; init; }
    public string? MediaId { get; init; }
    public double? SourceIn { get; init; }
    public double? SourceOut { get; init; }
    public EditFocus? Focus { get; init; }
    public JsonElement? Recipe { get; init; }
}
