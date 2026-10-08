using System.Text.Json;
using Cortex.Video.Editing;
using Cortex.Video.Composition;
namespace Cortex.Video.Tests;
internal static class HelengineCompositionTestData {
    internal static EditScriptDocument Script()=>new(){Schema=EditingDocumentKinds.Script,Revision=1,Project=new(){Id="composition",Title="Test",Format=ProjectFormat.Square,AspectRatio="1:1"},Sources=[new(){Id="post",Type=SourceKind.Image,Path="post.png"}],Beats=[new(){Id="a",Audio=new(){Type=AudioKind.None},Visual=new(){Type=VisualKind.Asset,Source="post",DurationSec=3}},new(){Id="b",Audio=new(){Type=AudioKind.None},Visual=new(){Type=VisualKind.Asset,Source="post",DurationSec=3}}]};
    internal static HelengineCapabilities Catalog()=>HelengineCapabilities.Parse(JsonDocument.Parse(File.ReadAllText(Path.Combine(EditingSerializationTests.Repository(),"tests/Cortex.Video.Tests/Fixtures/helengine-capabilities.json"))).RootElement);
}
