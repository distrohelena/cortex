using System.Text.Json;
using Cortex.Video.Editing;
using Cortex.Video.Composition;
namespace Cortex.Video.Tests;
internal static class HelengineCompositionTestData {
    internal static EditScriptDocument Script()=>new(){Schema=EditingDocumentKinds.Script,Revision=1,Project=new(){Id="composition",Title="Test",Format=ProjectFormat.Square,AspectRatio="1:1"},Sources=[new(){Id="post",Type=SourceKind.Image,Path="post.png"}],Beats=[new(){Id="a",Audio=new(){Type=AudioKind.None},Visual=new(){Type=VisualKind.Asset,Source="post",DurationSec=3}},new(){Id="b",Audio=new(){Type=AudioKind.None},Visual=new(){Type=VisualKind.Asset,Source="post",DurationSec=3}}]};
    internal static MediaSnapshot Image()=>new(){Id="post",SourceId="post",Path="gpu-red.png",Sha256=new string('a',64),Type=MediaKind.Image,Width=16,Height=16};
    internal static HelengineCapabilities Catalog()=>HelengineCapabilities.Parse(JsonDocument.Parse(File.ReadAllText(Path.Combine(EditingSerializationTests.Repository(),"tests/Cortex.Video.Tests/Fixtures/helengine-capabilities.json"))).RootElement);
    internal static IReadOnlyList<HelengineSceneBinding> Bindings()=>[new("a",Image(),0,3),new("b",Image(),3,6)];
    internal static ResolvedEditPlanDocument Resolved(EditScriptDocument script)=>new(){Schema=EditingDocumentKinds.Resolved,ProjectId=script.Project.Id,ScriptRef=new(){Path="script.json",Revision=script.Revision,Sha256=EditorialDecisionContext.Fingerprint(script)},ResolutionId="resolution",Status="resolved",Timebase="seconds",ReviewStatus=ReviewStatus.Accepted,Media=[Image()],Timeline=[new(){BeatId="a",Start=0,End=3,Audio=new(){Kind=BindingKind.None},Video=new(){Kind=BindingKind.Image,MediaId="post"},Enter="cut",Exit="cut"},new(){BeatId="b",Start=3,End=6,Audio=new(){Kind=BindingKind.None},Video=new(){Kind=BindingKind.Image,MediaId="post"},Enter="cut",Exit="cut"}],DurationSec=6,RenderRequirements=[],RecordingTaskResolutions=[],EditPlanRef=new(){Path="plan.json",ResolutionId="resolution",Sha256=new string('b',64)},RecordingTasksRef=new(){Path="tasks.json",ResolutionId="resolution",Sha256=new string('c',64)}};
}
