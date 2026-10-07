using Cortex.Video.Editing;
using Xunit;
namespace Cortex.Video.Tests;
public sealed class EditorialMotionCompilerTests
{
    [Fact] public void LocalFadeAndZoomHoldFinalFraming()
    {
        var recipe=new EditorialSceneRecipe{DurationSec=3,Entry=new("fade",.25),Motion=new(){Type="zoom",StartSec=.8,DurationSec=1.4,FromScale=1,ToScale=1.35}};
        var compiled=EditorialDecisionCompiler.Compile(recipe,new("image","hash",1000,1000),null,24);var first=EditorialImageTransform.AtFrame(compiled,0);var last=EditorialImageTransform.AtFrame(compiled,71);var end=EditorialImageTransform.AtFrame(compiled,53);
        Assert.Equal(0,first.Opacity);Assert.InRange(last.SourceRect.Width,740,742);Assert.Equal(end.SourceRect,last.SourceRect);Assert.Equal(72,compiled.FrameCount);
    }
    [Fact] public void TextFocusRequiresResolutionForTheSameImage()
    {
        var recipe=new EditorialSceneRecipe{DurationSec=3,Motion=new(){Type="zoom",StartSec=.8,DurationSec=1.4,Focus=new("text_region","Some text")}};var image=new EditorialImageSnapshot("image","hash",1000,1000);
        Assert.Throws<InvalidDataException>(()=>EditorialDecisionCompiler.Compile(recipe,image,null,24));
        var focus=new EditorialFocusResolution("hook","image","old","resolved",new(0,0,100,100),"human");Assert.Throws<InvalidDataException>(()=>EditorialDecisionCompiler.Compile(recipe,image,focus,24));
        focus=focus with{MediaHash="hash",Region=new(100,100,200,100)};Assert.Equal(72,EditorialDecisionCompiler.Compile(recipe,image,focus,24).FrameCount);
    }
}
