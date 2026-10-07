using System.Text.Json;
using Cortex.Video.Editing;
using Xunit;
namespace Cortex.Video.Tests;
public sealed class EditorialDecisionContractTests
{
    internal static EditScriptDocument Script()=> (EditScriptDocument)EditingJson.Parse(EditingSerializationTests.Fixture("script.json"));
    [Fact] public void RoundTripsAllSevenScenes()
    {
        var script=Script();var plan=EditorialDecisionContext.Inherit(script);var parsed=Assert.IsType<EditorialDecisionPlanDocument>(EditingJson.Parse(EditingJson.Serialize(plan)));
        Assert.Equal(7,parsed.Scenes.Count);Assert.Equal("hook",parsed.Scenes[0].BeatId);Assert.Equal("cortex.edit.decisions.v1",parsed.Schema);Assert.Empty(EditorialDecisionValidation.Validate(parsed,script));Cortex.Core.Flows.FlowDomain.Check(EditorialDecisionFlowTypes.ExecutionProjection(plan),EditorialDecisionFlowTypes.Plan);
        Assert.Equal(JsonValueKind.Object,EditingSchemas.Get("cortex.edit.decisions.v1").ValueKind);
    }
    [Theory][InlineData(.99)][InlineData(2.51)][InlineData(double.NaN)] public void RejectsScaleOutsideCatalog(double scale)
    {
        var script=Script();var plan=EditorialDecisionContext.Inherit(script);var scene=plan.Scenes[0];plan=plan with{Scenes=[scene with{Proposal=scene.Proposal with{Motion=scene.Proposal.Motion with{ToScale=scale}}},..plan.Scenes.Skip(1)]};
        Assert.Contains(EditorialDecisionValidation.Validate(plan,script),issue=>issue.Code=="edit.decisions.scale");
    }
    [Fact] public void UnknownOrMissingSceneAndSourceCannotExecute()
    {
        var script=Script();var plan=EditorialDecisionContext.Inherit(script);plan=plan with{Scenes=[plan.Scenes[0] with{BeatId="unknown"}]};Assert.NotEmpty(EditorialDecisionValidation.Validate(plan,script));
        plan=EditorialDecisionContext.Inherit(script);plan=plan with{Scenes=[plan.Scenes[0] with{Proposal=plan.Scenes[0].Proposal with{MaterialKey="design:image:private"}},..plan.Scenes.Skip(1)]};Assert.NotEmpty(EditorialDecisionValidation.Validate(plan,script));
    }
    [Fact] public void HumanZoomSurvivesReplanAndChangedIdentityBecomesConflict()
    {
        var script=Script();var plan=EditorialDecisionContext.Inherit(script);var revised=EditorialDecisionMerge.ApplyPatch(plan,"hook",[new("motion.to_scale",JsonSerializer.SerializeToElement(1.6))],1);
        var merged=EditorialDecisionMerge.Replan(revised,EditorialDecisionContext.Inherit(script),script);Assert.Equal(1.6,EditorialDecisionMerge.Effective(merged).Scenes[0].Proposal.Motion.ToScale);Assert.Contains(merged.Scenes[0].Overrides,p=>p.Path=="motion.to_scale");
        Assert.Throws<InvalidDataException>(()=>EditorialDecisionMerge.ApplyPatch(revised,"hook",[],1));
        var changed=script with{Beats=[script.Beats[0] with{Purpose="A different subject"},..script.Beats.Skip(1)]};var conflict=EditorialDecisionMerge.Replan(revised,EditorialDecisionContext.Inherit(changed),changed);Assert.True(conflict.Scenes[0].Conflict);Assert.Single(conflict.Scenes[0].Overrides);
    }
    [Fact] public void ChangedScriptCannotValidateAnOldDecisionPlan()
    {
        var script=Script();var plan=EditorialDecisionContext.Inherit(script);var changed=script with{Project=script.Project with{Title="Changed title"}};
        Assert.Contains(EditorialDecisionValidation.Validate(plan,changed),i=>i.Code=="edit.decisions.stale");
    }
    [Fact] public void AcceptingProposalRecordsReviewWithoutInventingHumanEdits()
    {
        var plan=EditorialDecisionContext.Inherit(Script());var accepted=EditorialDecisionMerge.Review(plan,"hook",ReviewStatus.Accepted,1);Assert.Equal(ReviewStatus.Accepted,accepted.Scenes[0].ReviewStatus);Assert.Empty(accepted.Scenes[0].Overrides);Assert.Equal(2,accepted.Revision);
    }
    [Fact] public void ExportImportPreservesHumanDecisions()
    {
        var script=Script();var plan=EditorialDecisionMerge.ApplyPatch(EditorialDecisionContext.Inherit(script),"hook",[new("motion.to_scale",JsonSerializer.SerializeToElement(1.6))],1);var bundle=EditorialDecisionExport.Import(EditorialDecisionExport.Export(script,plan));Assert.Equal(1.6,EditorialDecisionMerge.Effective(bundle.Decisions).Scenes[0].Proposal.Motion.ToScale);
    }
}
