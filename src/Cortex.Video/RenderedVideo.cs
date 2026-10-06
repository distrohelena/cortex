namespace Cortex.Video;

public sealed record RenderedVideo(string Path, double Duration, IReadOnlyList<ProposedTakeClip> Timeline);

public interface IVideoCutRenderer
{
    Task<RenderedVideo> RenderAsync(string videoPath, TakeEditPlan plan, string outputDirectory, CancellationToken token);
}
