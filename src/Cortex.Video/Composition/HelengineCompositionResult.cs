namespace Cortex.Video.Composition;
/// <summary>Something a composition still needs before it can be rendered (missing media, unresolved focus...).</summary>
public sealed record HelengineCompositionPending(string Code,string BeatId,string Message);
