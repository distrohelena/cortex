namespace Cortex.Video.Editing;

public sealed record EditProject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required ProjectFormat Format { get; init; }
    public required string AspectRatio { get; init; }
    public double? TargetDurationSec { get; init; }
    public RecordingProfile? RecordingDefaults { get; init; }
}

public sealed record EditSource
{
    public required string Id { get; init; }
    public required SourceKind Type { get; init; }
    public required string Path { get; init; }
    public string? Speaker { get; init; }
    public bool? Transcription { get; init; }
}

public sealed record EditBeat
{
    public required string Id { get; init; }
    public string? Purpose { get; init; }
    public required AudioIntent Audio { get; init; }
    public required VisualIntent Visual { get; init; }
    public EditOverlay? Overlay { get; init; }
    public IReadOnlyList<EditOverlay>? Overlays { get; init; }
}

public sealed record AudioIntent
{
    public required AudioKind Type { get; init; }
    public ClipSearch? Search { get; init; }
    public ClipSelection? Selection { get; init; }
    public RecordingFallback? Fallback { get; init; }
    public string? Script { get; init; }
    public string? Brief { get; init; }
    public RecordingProfile? Recording { get; init; }
    public double? TargetDurationSec { get; init; }
    public bool? RequireExactWording { get; init; }
}

public sealed record ClipSearch
{
    public required string Speaker { get; init; }
    public required MatchMode Match { get; init; }
    public string? Text { get; init; }
    public string? Meaning { get; init; }
    public string? SourceId { get; init; }
}

public sealed record ClipSelection
{
    public required string Strategy { get; init; }
    public double? MaxDurationSec { get; init; }
    public IReadOnlyList<string>? Prefer { get; init; }
    public IReadOnlyDictionary<string, double>? Score { get; init; }
}

public sealed record RecordingFallback
{
    public required AudioKind Type { get; init; }
    public string? Script { get; init; }
    public string? Brief { get; init; }
    public double? TargetDurationSec { get; init; }
    public RecordingProfile? Recording { get; init; }
}

public sealed record RecordingProfile
{
    public string? Speaker { get; init; }
    public string? Framing { get; init; }
    public string? Orientation { get; init; }
    public string? Delivery { get; init; }
}

public sealed record VisualIntent
{
    public required VisualKind Type { get; init; }
    public string? Source { get; init; }
    public string? Speaker { get; init; }
    public string? Enter { get; init; }
    public string? Exit { get; init; }
    public double? DurationSec { get; init; }
    public EditFocus? Focus { get; init; }
}

public sealed record EditFocus
{
    public required string Type { get; init; }
    public required string TargetText { get; init; }
    public PixelRectangle? SourceRectPixels { get; init; }
}

public sealed record PixelRectangle(double X, double Y, double Width, double Height);
public sealed record EditOverlay
{
    public required string Text { get; init; }
    public string? Style { get; init; }
    public OverlayTrigger? Trigger { get; init; }
}
public sealed record OverlayTrigger
{
    public required string SpokenPhrase { get; init; }
    public AlignedTrigger? Resolved { get; init; }
}
public sealed record AlignedTrigger(double OffsetSec, TimingGranularity TimingGranularity, string Method);
