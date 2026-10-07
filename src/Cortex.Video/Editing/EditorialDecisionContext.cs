using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Cortex.Video.Editing;
public static class EditorialDecisionContext
{
    public static string Hash(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Fingerprint(EditScriptDocument script)=>Hash(EditingJson.Serialize(script).GetRawText());
    public static string Identity(EditBeat beat)=>Hash(JsonSerializer.Serialize(new{beat.Purpose,beat.Visual.Type,beat.Visual.Source,beat.Visual.Speaker}));
    public static string? MaterialKey(EditBeat beat)=>beat.Visual.Type switch{VisualKind.Asset=>"design:image:"+beat.Visual.Source,VisualKind.Graphic=>"design:graphic:"+beat.Id+"_graphic",VisualKind.TitleCard=>"design:title_card:"+beat.Id+"_title_card",_=>null};
    public static double Duration(EditBeat beat)=>beat.Audio.TargetDurationSec??beat.Visual.DurationSec??beat.Audio.Selection?.MaxDurationSec??Math.Max(2,(beat.Audio.Script??beat.Audio.Brief??"").Split(' ',StringSplitOptions.RemoveEmptyEntries).Length/2.5);
    public static IReadOnlyList<string> Texts(EditBeat beat)=> (beat.Overlay is null?Enumerable.Empty<string>():new[]{"overlay"}).Concat((beat.Overlays??[]).Select((_,i)=>"overlays/"+i)).ToArray();
    public static EditorialDecisionPlanDocument Inherit(EditScriptDocument script)
    {
        var weights=script.Beats.Select(Duration).ToArray();var total=script.Project.TargetDurationSec??weights.Sum();var sum=weights.Sum();
        return new(){Schema=EditingDocumentKinds.Decisions,ProjectId=script.Project.Id,ScriptRef=new(){Path="script.json",Revision=script.Revision,Sha256=Fingerprint(script)},ContextFingerprint=Fingerprint(script),Scenes=script.Beats.Select((beat,i)=>{
            var duration=total*weights[i]/sum;var focus=beat.Visual.Focus;var image=MaterialKey(beat);var zoom=image is not null&&focus is not null;
            var (w,h)=script.Project.Format switch{ProjectFormat.Vertical=>(432,768),ProjectFormat.Horizontal=>(768,432),_=>(512,512)};
            return new EditorialSceneDecision{BeatId=beat.Id,IdentityFingerprint=Identity(beat),Origin="inherited",Reason=beat.Purpose??"Preservar a intenção da cena no roteiro.",Proposal=new(){MaterialKey=image,DurationSec=duration,Presentation=beat.Visual.Type is VisualKind.Graphic or VisualKind.TitleCard?"full_frame":"inset",Fit=beat.Visual.Type==VisualKind.TitleCard?"cover":"contain",ReferenceWidth=w,ReferenceHeight=h,TextBindings=Texts(beat),Entry=new(zoom?"fade":"cut",Math.Min(.25,duration/4)),Motion=new(){Type=zoom?"zoom":"none",StartSec=Math.Round(duration*4/15,6),DurationSec=Math.Round(duration*7/15,6),Focus=focus is null?new():new("text_region",focus.TargetText)}}};
        }).ToArray()};
    }
}
