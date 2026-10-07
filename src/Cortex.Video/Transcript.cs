namespace Cortex.Video;

public sealed record TranscriptSegment(double Start, double End, string Text);
public sealed record TranscriptWord(double Start, double End, string Text);
public sealed record TranscriptResult(string Text, IReadOnlyList<TranscriptSegment> Segments)
{
    public IReadOnlyList<TranscriptWord> Words { get; init; } = [];
    public string? WordTimingMethod { get; init; }
}

public interface IVideoTranscriber
{
    Task<TranscriptResult> TranscribeAsync(string videoPath, CancellationToken token);
}

