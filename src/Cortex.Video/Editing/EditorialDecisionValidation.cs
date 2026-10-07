using Cortex.Core.Flows;
namespace Cortex.Video.Editing;
public static class EditorialDecisionValidation
{
    public static IReadOnlyList<FlowIssue> Validate(EditorialDecisionPlanDocument plan,EditScriptDocument? script=null)
    {
        var issues=new List<FlowIssue>();void Error(string code,string path,string text)=>issues.Add(new("edit.decisions."+code,path,text));
        if(plan.Schema is not(EditingDocumentKinds.Decisions or EditingDocumentKinds.DecisionsV2)||plan.Revision<1||string.IsNullOrWhiteSpace(plan.PlanId)||!EditingContractValidator.Hash(plan.ContextFingerprint)||!EditingContractValidator.Hash(plan.ScriptRef.Sha256))Error("header","/","Invalid decision plan header.");
        if(plan.Scenes is null||plan.Scenes.Count is <1 or >32){Error("scenes","/scenes","Use 1–32 complete scenes.");return issues;}
        if(plan.Scenes.Select(s=>s.BeatId).Distinct().Count()!=plan.Scenes.Count)Error("scenes","/scenes","Scene IDs must be unique.");
        if(script is not null&&(plan.ProjectId!=script.Project.Id||!plan.Scenes.Select(s=>s.BeatId).SequenceEqual(script.Beats.Select(b=>b.Id))))Error("scenes","/scenes","The plan must cover the script in order.");
        if(script is not null&&plan.ContextFingerprint!=EditorialDecisionContext.Fingerprint(script))Error("stale","/context_fingerprint","Decision context changed.");
        foreach(var (s,i)in plan.Scenes.Select((s,i)=>(s,i))){
            var path="/scenes/"+i;if(string.IsNullOrWhiteSpace(s.BeatId)||string.IsNullOrWhiteSpace(s.Reason)||s.Reason.Length>800||s.Origin is not("cortex" or "human" or "inherited")||!EditingContractValidator.Hash(s.IdentityFingerprint))Error("scene",path,"Invalid scene origin or reason.");
            if(s.Proposal is null){Error("recipe",path,"Missing recipe.");continue;}var p=s.Proposal;var m=p.Motion;var e=p.Entry;
            if(m is null||e is null||m.Focus is null){Error("recipe",path,"Incomplete motion recipe.");continue;}
            if(p.Presentation is not("full_frame" or "inset" or "side_by_side")||p.Fit is not("contain" or "cover")||e.Type is not("cut" or "fade")||m.Type is not("none" or "zoom")||m.Easing is not("linear" or "ease_in_out")||m.Focus.Type is not("center" or "point" or "text_region"))Error("catalog",path,"Unknown editorial effect.");
            if(!double.IsFinite(p.DurationSec)||p.DurationSec is <=0 or >600||!double.IsFinite(e.DurationSec)||e.DurationSec<0||e.DurationSec>p.DurationSec)Error("timing",path,"Invalid duration.");
            if(!double.IsFinite(m.FromScale)||!double.IsFinite(m.ToScale)||m.FromScale is <1 or >2.5||m.ToScale is <1 or >2.5)Error("scale",path,"Scale must be 1–2.5.");
            if(!double.IsFinite(m.StartSec)||!double.IsFinite(m.DurationSec)||m.StartSec<0||m.DurationSec<=0||m.Type=="zoom"&&m.StartSec+m.DurationSec>p.DurationSec+.000001)Error("timing",path,"Zoom must fit the scene.");
            if(!double.IsFinite(m.Focus.X)||!double.IsFinite(m.Focus.Y)||m.Focus.X is <0 or >1||m.Focus.Y is <0 or >1||m.Focus.Type=="text_region"&&string.IsNullOrWhiteSpace(m.Focus.Text))Error("focus",path,"Invalid focus target.");
            if(p.ReferenceWidth is <256 or >1024||p.ReferenceHeight is <256 or >1024||p.ReferenceWidth%16!=0||p.ReferenceHeight%16!=0||string.IsNullOrWhiteSpace(p.Composition)||p.Composition.Length>2400)Error("reference",path,"Invalid reference composition or size.");
            if(script?.Beats.SingleOrDefault(b=>b.Id==s.BeatId) is {} beat){
                if(p.MaterialKey!=EditorialDecisionContext.MaterialKey(beat))Error("source",path,"Use the material declared for this scene.");
                if(!p.TextBindings.SequenceEqual(EditorialDecisionContext.Texts(beat)))Error("text",path,"Do not change authored text bindings.");
                if(m.Focus.Type=="text_region"&&(m.Focus.Text is null||!((beat.Visual.Focus?.TargetText??"")+" "+(beat.Audio.Script??beat.Audio.Brief??"")+" "+(beat.Purpose??"")).Contains(m.Focus.Text,StringComparison.OrdinalIgnoreCase)))Error("focus",path,"Focus text must come from the scene.");
            }
        }
        issues.AddRange(EditorialDecisionV2Validation.Validate(plan));
        return issues;
    }
    public static void ThrowIfInvalid(EditorialDecisionPlanDocument plan,EditScriptDocument? script=null){var issues=Validate(plan,script);if(issues.Count>0)throw new InvalidDataException(string.Join("; ",issues.Select(i=>i.Code+": "+i.Message)));}
}
