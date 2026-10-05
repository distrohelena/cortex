namespace Cortex.Video;

public sealed record TranscriptSegment(double Start, double End, string Text);
public sealed record TranscriptResult(string Text, IReadOnlyList<TranscriptSegment> Segments);

public interface IVideoTranscriber
{
    Task<TranscriptResult> TranscribeAsync(string videoPath, CancellationToken token);
}

