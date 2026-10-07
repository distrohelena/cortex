using System.Text.Json;
using Cortex.Video.Composition;
using Xunit;
namespace Cortex.Video.Tests;
public sealed class HelengineCutRendererTests {
    [Fact] public async Task RecordedVideoUsesEngineForCuts() {string root="C:/dev/helworks/builds/helengine/media-composition/fixtures/cut-test-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(root);try {var client=new CapturingCutCompositionClient();var renderer=new HelengineCutRenderer(client,"C:/env/ffmpeg-8.1.1-full_build-shared/bin/ffprobe.exe");var plan=new TakeEditPlan(1,"reviewed","sourceSeconds","transcript_only","test",new string('a',64),"test","test",[new("take",new(0,.5,[]),"fala",Keep:true,Reason:"selecionada")],[],[],[]);await renderer.RenderAsync("C:/dev/helworks/builds/helengine/media-composition/fixtures/av-offset.mp4",plan,root,CancellationToken.None);Assert.Single(client.Composition.GetProperty("layers").EnumerateArray());Assert.Single(client.Composition.GetProperty("audio_clips").EnumerateArray());Assert.Equal("helengine.media.composition.v1",client.Composition.GetProperty("schema").GetString());}finally {Directory.Delete(root,true);}}
}
