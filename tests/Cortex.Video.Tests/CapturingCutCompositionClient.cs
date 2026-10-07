using System.Text.Json;
using Cortex.Video.Composition;
namespace Cortex.Video.Tests;
public sealed class CapturingCutCompositionClient:IHelengineCompositionClient {
    public JsonElement Composition {get;private set;}
    public Task<HelengineCapabilities> CapabilitiesAsync(CancellationToken token)=>Task.FromResult(HelengineCompositionTestData.Catalog());
    public Task ValidateAsync(JsonElement composition,string assetsRoot,CancellationToken token)=>Task.CompletedTask;
    public async Task<JsonElement> RenderAsync(JsonElement composition,string assetsRoot,string output,string profile,CancellationToken token) {Composition=composition;await File.WriteAllBytesAsync(output,[1,2,3],token);return JsonSerializer.SerializeToElement(new{fingerprint=new string('a',64)});}
    public Task FrameAsync(JsonElement composition,string assetsRoot,string output,long numerator,long denominator,int width,int height,CancellationToken token)=>Task.CompletedTask;
}
