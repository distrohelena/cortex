namespace Cortex.Video.Editing;
public sealed record CompiledImageMotion(EditorialSceneRecipe Recipe,EditorialImageSnapshot Image,double FocusX,double FocusY,int Fps,int FrameCount);
public sealed record ImageFrameTransform(PixelRectangle SourceRect,double Opacity);
public static class EditorialDecisionCompiler
{
    public static CompiledImageMotion Compile(EditorialSceneRecipe recipe,EditorialImageSnapshot image,EditorialFocusResolution? focus,int fps)
    {
        var fake=new EditorialDecisionPlanDocument{Schema=EditingDocumentKinds.Decisions,ProjectId="motion",ScriptRef=new(){Path="script.json",Sha256=new string('a',64)},ContextFingerprint=new string('a',64),Scenes=[new(){BeatId="image",IdentityFingerprint=new string('a',64),Reason="Motion",Proposal=recipe}]};EditorialDecisionValidation.ThrowIfInvalid(fake);
        if(image.Width<=0||image.Height<=0||fps is <1 or >60)throw new InvalidDataException("Invalid image dimensions or frame rate.");var x=recipe.Motion.Focus.X;var y=recipe.Motion.Focus.Y;
        if(recipe.Motion.Type=="zoom"&&recipe.Motion.Focus.Type=="text_region"){
            if(focus is not{State:"resolved",Region:not null}||focus.MediaId!=image.Id||focus.MediaHash!=image.Sha256)throw new InvalidDataException("Text focus needs a current image region.");
            var r=focus.Region;if(!double.IsFinite(r.X+r.Y+r.Width+r.Height)||r.X<0||r.Y<0||r.Width<=0||r.Height<=0||r.X+r.Width>image.Width||r.Y+r.Height>image.Height)throw new InvalidDataException("Focus region is outside the image.");x=(r.X+r.Width/2)/image.Width;y=(r.Y+r.Height/2)/image.Height;
        }
        return new(recipe,image,x,y,fps,(int)Math.Ceiling(recipe.DurationSec*fps));
    }
}
public static class EditorialImageTransform
{
    public static ImageFrameTransform AtFrame(CompiledImageMotion compiled,int frame)
    {
        if(frame<0||frame>=compiled.FrameCount)throw new ArgumentOutOfRangeException(nameof(frame));var p=compiled.Recipe;var m=p.Motion;var t=(double)frame/compiled.Fps;var u=Math.Clamp((t-m.StartSec)/m.DurationSec,0,1);if(m.Easing=="ease_in_out")u=u*u*(3-2*u);var scale=m.Type=="none"?1:m.FromScale+(m.ToScale-m.FromScale)*u;
        var w=compiled.Image.Width/scale;var h=compiled.Image.Height/scale;var x=Math.Clamp(compiled.FocusX*compiled.Image.Width-w/2,0,compiled.Image.Width-w);var y=Math.Clamp(compiled.FocusY*compiled.Image.Height-h/2,0,compiled.Image.Height-h);
        return new(new(x,y,w,h),p.Entry.Type=="fade"&&p.Entry.DurationSec>0?Math.Clamp(t/p.Entry.DurationSec,0,1):1);
    }
}
