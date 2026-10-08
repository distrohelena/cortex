using System.Text.Json;
using Cortex.Video.Composition;
using Cortex.Video.Editing;
using Xunit;
namespace Cortex.Video.Tests;
public sealed class EditorialDecisionV2Tests {
    [Fact] public void PlannerTransitionParametersComeFromTheCatalog() {
        var catalog=HelengineCapabilities.Parse(JsonDocument.Parse("""
        {"schema":"helengine.media.capabilities.v1","composition_schema":"helengine.media.composition.v1","curves":["linear.v1"],"layer_properties":{"opacity":{"type":"number","minimum":0,"maximum":1}},"effects":[
          {"id":"tint","version":1,"category":"layer","main_input_role":"Source","input_roles":["Source"],"parameters":{}},
          {"id":"crossfade","version":1,"category":"transition","main_input_role":"Source","input_roles":[],"parameters":{}},
          {"id":"push","version":1,"category":"transition","main_input_role":"From","input_roles":["From","To"],"parameters":{"Direction":{"type":"enum","minimum":0,"maximum":1,"allowed_values":["Left","Right"]}}},
          {"id":"dip-to-color","version":1,"category":"transition","main_input_role":"From","input_roles":["From","To"],"parameters":{"Color":{"type":"color","minimum":0,"maximum":1}}}]}
        """).RootElement);
        var type=EditorialDecisionFlowTypes.ForCapabilities(catalog);
        var scene=(Cortex.Core.Flows.ObjectType)((Cortex.Core.Flows.ArrayType)type.Fields["scenes"].Type).ElementType;var recipe=(Cortex.Core.Flows.ObjectType)scene.Fields["proposal"].Type;var editing=(Cortex.Core.Flows.ObjectType)recipe.Fields["editing"].Type;var transition=(Cortex.Core.Flows.ObjectType)editing.Fields["transition"].Type;
        var parameters=(Cortex.Core.Flows.ObjectType)transition.Fields["parameters"].Type;
        Assert.Equal(["Color","Direction"],parameters.Fields.Keys.Order());Assert.True(transition.Fields.ContainsKey("audio_fade_sec"));
    }
    [Fact] public void HumanOverrideSurvivesCatalogRefresh() {var script=HelengineCompositionTestData.Script();var initial=EditorialDecisionContext.Inherit(script);var plan=initial with{Schema=EditingDocumentKinds.DecisionsV2,Catalog=new("helengine.media.capabilities.v1",HelengineCompositionTestData.Catalog().Fingerprint)};var changed=EditorialDecisionMerge.ApplyPatch(plan,"a",[new("motion.to_scale",JsonSerializer.SerializeToElement(1.7))],1);var refreshed=EditorialDecisionMerge.Replan(changed,plan with{Catalog=new("helengine.media.capabilities.v1",new string('f',64))},script);Assert.Equal(1.7,EditorialDecisionMerge.Effective(refreshed).Scenes[0].Proposal.Motion.ToScale);Assert.Single(refreshed.Scenes[0].Overrides);}
}
