using System.Text.Json;
using System.Text.Json.Nodes;
namespace Cortex.Video.Editing;
public static class EditorialDecisionMerge
{
    private static readonly HashSet<string> Paths=["presentation","fit","entry.type","entry.duration_sec","motion.type","motion.start_sec","motion.duration_sec","motion.from_scale","motion.to_scale","motion.easing","motion.focus.type","motion.focus.text","motion.focus.x","motion.focus.y","reference_width","reference_height","composition","editing","editing.transform","editing.animations","editing.effects","editing.mask","editing.transition","editing.audio","editing.voice_gain","editing.voice_muted","editing.voice_envelopes"];
    private static EditorialSceneRecipe Patch(EditorialSceneRecipe recipe,EditorialFieldPatch patch)
    {
        if(!Paths.Contains(patch.Path))throw new InvalidDataException("Unknown editable decision field.");var node=JsonSerializer.SerializeToNode(recipe,EditingJson.Options)!;var parts=patch.Path.Split('.');var current=node;
        foreach(var part in parts[..^1]) {if(part=="editing" && current[part] is null)current[part]=JsonSerializer.SerializeToNode(new EditorialCompositionEdit(),EditingJson.Options);current=current[part]??throw new InvalidDataException("Incomplete decision field.");}current[parts[^1]]=JsonNode.Parse(patch.Value.GetRawText());
        return node.Deserialize<EditorialSceneRecipe>(EditingJson.Options)??throw new InvalidDataException("Invalid decision recipe.");
    }
    public static EditorialDecisionPlanDocument Effective(EditorialDecisionPlanDocument plan)=>plan with{Scenes=plan.Scenes.Select(s=>{
        var recipe=s.Proposal;if(!s.Conflict)foreach(var patch in s.Overrides)recipe=Patch(recipe,patch);return s with{Proposal=recipe};
    }).ToArray()};
    public static EditorialDecisionPlanDocument ApplyPatch(EditorialDecisionPlanDocument plan,string beatId,IReadOnlyList<EditorialFieldPatch> patches,int expectedRevision)
    {
        if(expectedRevision!=plan.Revision)throw new InvalidDataException("Editorial revision conflict.");if(!plan.Scenes.Any(s=>s.BeatId==beatId))throw new InvalidDataException("Unknown scene.");
        if(patches.Count>32||patches.Select(p=>p.Path).Distinct().Count()!=patches.Count)throw new InvalidDataException("Invalid decision patch.");
        var result=plan with{Revision=plan.Revision+1,ReviewStatus=ReviewStatus.Pending,Scenes=plan.Scenes.Select(s=>s.BeatId!=beatId?s:s with{Conflict=false,ReviewStatus=ReviewStatus.Pending,Overrides=s.Overrides.Where(p=>patches.All(n=>n.Path!=p.Path)).Concat(patches).ToArray()}).ToArray()};
        EditorialDecisionValidation.ThrowIfInvalid(Effective(result));return result;
    }
    public static EditorialDecisionPlanDocument Review(EditorialDecisionPlanDocument plan,string beatId,ReviewStatus status,int revision)
    {
        if(revision!=plan.Revision)throw new InvalidDataException("Editorial revision conflict.");if(!plan.Scenes.Any(s=>s.BeatId==beatId)||status is not(ReviewStatus.Accepted or ReviewStatus.Rejected))throw new InvalidDataException("Invalid decision review.");if(plan.Scenes.Any(s=>s.BeatId==beatId&&s.Conflict)&&status==ReviewStatus.Accepted)throw new InvalidDataException("Resolve the scene conflict before acceptance.");
        var scenes=plan.Scenes.Select(s=>s.BeatId==beatId?s with{ReviewStatus=status}:s).ToArray();return plan with{Revision=revision+1,Scenes=scenes,ReviewStatus=scenes.All(s=>s.ReviewStatus==ReviewStatus.Accepted)?ReviewStatus.Accepted:scenes.Any(s=>s.ReviewStatus==ReviewStatus.Rejected)?ReviewStatus.Rejected:ReviewStatus.Pending};
    }
    public static EditorialDecisionPlanDocument UseProposal(EditorialDecisionPlanDocument plan,string beatId,string path,int revision)
    {
        if(revision!=plan.Revision)throw new InvalidDataException("Editorial revision conflict.");return plan with{Revision=revision+1,Scenes=plan.Scenes.Select(s=>s.BeatId!=beatId?s:s with{Overrides=s.Overrides.Where(p=>p.Path!=path).ToArray(),Conflict=false}).ToArray()};
    }
    public static EditorialDecisionPlanDocument Replan(EditorialDecisionPlanDocument previous,EditorialDecisionPlanDocument proposal,EditScriptDocument script)
    {
        var old=previous.Scenes.ToDictionary(s=>s.BeatId);return proposal with{Schema=previous.Schema,Catalog=previous.Schema==EditingDocumentKinds.DecisionsV2?(proposal.Catalog??previous.Catalog):null,PlanId=previous.PlanId,Revision=previous.Revision+1,Scenes=proposal.Scenes.Select(s=>previous.Schema==EditingDocumentKinds.Decisions?s with{Proposal=s.Proposal with{Editing=null}}:s).Select(s=>!old.TryGetValue(s.BeatId,out var prior)?s:s with{Overrides=prior.Overrides,Conflict=prior.Overrides.Count>0&&prior.IdentityFingerprint!=s.IdentityFingerprint}).ToArray(),Metadata=JsonSerializer.SerializeToElement(new{orphaned=previous.Scenes.Where(s=>!proposal.Scenes.Any(n=>n.BeatId==s.BeatId)&&s.Overrides.Count>0).ToArray()},EditingJson.Options)};
    }
}
public static class EditorialDecisionExport
{
    public static JsonElement Export(EditScriptDocument script,EditorialDecisionPlanDocument plan){EditorialDecisionValidation.ThrowIfInvalid(EditorialDecisionMerge.Effective(plan),script);return JsonSerializer.SerializeToElement(new{script=EditingJson.Serialize(script),decisions=EditingJson.Serialize(plan)});}
    public static EditorialProjectBundle Import(JsonElement bundle){var script=EditingJson.Parse(bundle.GetProperty("script")) as EditScriptDocument??throw new InvalidDataException("Invalid script.");var plan=EditingJson.Parse(bundle.GetProperty("decisions")) as EditorialDecisionPlanDocument??throw new InvalidDataException("Invalid decisions.");EditorialDecisionValidation.ThrowIfInvalid(EditorialDecisionMerge.Effective(plan),script);return new(script,plan);}
}
