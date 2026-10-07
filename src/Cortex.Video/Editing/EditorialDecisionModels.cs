using System.Text.Json;
namespace Cortex.Video.Editing;
public sealed record EditorialDecisionPlanDocument:EditingDocument
{
    public required string ProjectId{get;init;}
    public string PlanId{get;init;}=Guid.NewGuid().ToString("N");
    public int Revision{get;init;}=1;
    public required EditingDocumentReference ScriptRef{get;init;}
    public required string ContextFingerprint{get;init;}
    public ReviewStatus ReviewStatus{get;init;}=ReviewStatus.Pending;
    public required IReadOnlyList<EditorialSceneDecision> Scenes{get;init;}
}
public sealed record EditorialSceneDecision
{
    public required string BeatId{get;init;}
    public required string IdentityFingerprint{get;init;}
    public required string Reason{get;init;}
    public string Origin{get;init;}="cortex";
    public required EditorialSceneRecipe Proposal{get;init;}
    public IReadOnlyList<EditorialFieldPatch> Overrides{get;init;}=[];
    public ReviewStatus ReviewStatus{get;init;}=ReviewStatus.Pending;
    public bool Conflict{get;init;}
}
public sealed record EditorialFieldPatch(string Path,JsonElement Value);
public sealed record EditorialEntry(string Type="cut",double DurationSec=.25);
public sealed record EditorialFocusTarget(string Type="center",string? Text=null,double X=.5,double Y=.5);
public sealed record EditorialMotion
{
    public string Type{get;init;}="none";
    public double StartSec{get;init;}=.8;
    public double DurationSec{get;init;}=1.4;
    public double FromScale{get;init;}=1;
    public double ToScale{get;init;}=1.35;
    public string Easing{get;init;}="ease_in_out";
    public EditorialFocusTarget Focus{get;init;}=new();
}
public sealed record EditorialSceneRecipe
{
    public string? MaterialKey{get;init;}
    public string Presentation{get;init;}="inset";
    public string Fit{get;init;}="contain";
    public double DurationSec{get;init;}=3;
    public EditorialEntry Entry{get;init;}=new();
    public EditorialMotion Motion{get;init;}=new();
    public int ReferenceWidth{get;init;}=512;
    public int ReferenceHeight{get;init;}=512;
    public string Composition{get;init;}="Clear editorial illustration; keep authored text separate.";
    public IReadOnlyList<string> TextBindings{get;init;}=[];
}
public sealed record EditorialImageSnapshot(string Id,string Sha256,int Width,int Height);
public sealed record EditorialFocusResolution(string BeatId,string MediaId,string MediaHash,string State,PixelRectangle? Region,string Origin="human");
public sealed record EditorialProjectBundle(EditScriptDocument Script,EditorialDecisionPlanDocument Decisions);
