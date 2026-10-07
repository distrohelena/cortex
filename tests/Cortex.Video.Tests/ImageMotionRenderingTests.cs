using System.Diagnostics;
using System.Security.Cryptography;
using Cortex.Video.Editing;
using Xunit;
namespace Cortex.Video.Tests;
public sealed class ImageMotionRenderingTests
{
    [Fact] public async Task RendersSeventyTwoFramesWithTransparentEntrance()
    {
        Assert.True(CutRenderingTests.NativeToolsAvailable);var root=Path.Combine(Path.GetTempPath(),"image-motion-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try{var image=Path.Combine(root,"source.png");await Run("ffmpeg",["-hide_banner","-loglevel","error","-f","lavfi","-i","color=red:s=100x100","-frames:v","1",image]);
            var hash=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(image,TestContext.Current.CancellationToken)));var recipe=new EditorialSceneRecipe{DurationSec=3,Entry=new("fade",.25),Motion=new(){Type="zoom",StartSec=.8,DurationSec=1.4}};var motion=EditorialDecisionCompiler.Compile(recipe,new("image",hash,100,100),null,24);
            var frames=await new FfmpegImageMotionRenderer("ffmpeg").RenderAsync(new(image,root,Path.Combine(root,"frames"),motion,200,200,"contain"),TestContext.Current.CancellationToken);Assert.Equal(72,frames.FrameCount);Assert.Equal(72,Directory.GetFiles(Path.Combine(root,"frames"),"*.png").Length);Assert.Equal((200,200),(frames.Width,frames.Height));
            var pixels=await Run("ffmpeg",["-hide_banner","-loglevel","error","-i",Path.Combine(root,"frames","frame-000000.png"),"-frames:v","1","-f","rawvideo","-pix_fmt","rgba","-"]);Assert.InRange(pixels[3],0,8);
        }finally{Directory.Delete(root,true);}
    }
    private static async Task<byte[]> Run(string tool,IEnumerable<string> args){var info=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in args)info.ArgumentList.Add(a);using var p=Process.Start(info)!;using var memory=new MemoryStream();var copy=p.StandardOutput.BaseStream.CopyToAsync(memory,TestContext.Current.CancellationToken);var error=p.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);await p.WaitForExitAsync(TestContext.Current.CancellationToken);await copy;Assert.True(p.ExitCode==0,await error);return memory.ToArray();}
}
