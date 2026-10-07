using System.Text.Json;
namespace Cortex.Video.Composition;
/// <summary>Host protocol boundary for catalog, validation and rendering; model planning is intentionally separate.</summary>
public interface IHelengineCompositionClient {
    Task<HelengineCapabilities> CapabilitiesAsync(CancellationToken token);
    Task ValidateAsync(JsonElement composition,string assetsRoot,CancellationToken token);
    Task<JsonElement> RenderAsync(JsonElement composition,string assetsRoot,string output,string profile,CancellationToken token);
    Task FrameAsync(JsonElement composition,string assetsRoot,string output,long numerator,long denominator,int width,int height,CancellationToken token);
}
