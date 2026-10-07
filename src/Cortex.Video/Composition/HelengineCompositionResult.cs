using System.Text.Json;
using Cortex.Video.Editing;
namespace Cortex.Video.Composition;
public sealed record HelengineCompositionPending(string Code,string BeatId,string Message);
public sealed record HelengineCompositionResult(JsonElement Composition,IReadOnlyList<HelengineCompositionPending> Pending) {
    public bool IsExecutable=>Pending.Count==0;
    public JsonElement RequireExecutable()=>IsExecutable?Composition:throw new InvalidDataException(string.Join("; ",Pending.Select(item=>item.Code+": "+item.Message)));
}
/// <summary>Host-resolved bindings and timing; editorial models cannot author file paths.</summary>
public sealed record HelengineSceneBinding(string BeatId,MediaSnapshot? Media,double Start,double End,MediaBinding? Audio=null,EditorialFocusResolution? Focus=null,MediaBinding? Video=null,IReadOnlyList<EditOverlay>? Overlays=null,HelengineViewport? Viewport=null);
public sealed record HelengineViewport(double X,double Y,double Width,double Height);
