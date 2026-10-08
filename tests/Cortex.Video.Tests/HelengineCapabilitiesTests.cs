using System.Text.Json;
using Cortex.Video.Composition;
using Xunit;
namespace Cortex.Video.Tests;
public sealed class HelengineCapabilitiesTests {
    [Fact] public void CatalogVersionPinned() {var catalog=HelengineCompositionTestData.Catalog();Assert.True(catalog.Supports("crossfade",1));Assert.False(catalog.Supports("crossfade",2));Assert.Contains("smoothstep.v1",catalog.Curves);Assert.Equal(64,catalog.Fingerprint.Length);}
    [Fact] public void ProjectEffectShapesAccepted() {
        var catalog=HelengineCapabilities.Parse(JsonDocument.Parse("""
        {"schema":"helengine.media.capabilities.v1","composition_schema":"helengine.media.composition.v1","curves":["linear.v1"],"effects":[{"id":"soft-shadow","version":1,"parameters":{
          "Color":{"type":"color","minimum":0,"maximum":1},"Offset":{"type":"float2","minimum":-64,"maximum":64},"Enabled":{"type":"boolean","minimum":0,"maximum":1}}}]}
        """).RootElement);
        catalog.ValidateEffect("soft-shadow",1,new Dictionary<string,JsonElement>{{"Color",JsonSerializer.SerializeToElement(new[]{0,0,0,.6})},{"Offset",JsonSerializer.SerializeToElement(new[]{4,6})},{"Enabled",JsonSerializer.SerializeToElement(true)}});
        Assert.Throws<InvalidDataException>(()=>catalog.ValidateEffect("soft-shadow",1,new Dictionary<string,JsonElement>{{"Offset",JsonSerializer.SerializeToElement(new[]{4,100})}}));
        Assert.Throws<InvalidDataException>(()=>catalog.ValidateEffect("soft-shadow",1,new Dictionary<string,JsonElement>{{"Color",JsonSerializer.SerializeToElement(new[]{0,0})}}));
    }
    [Fact] public void UnknownParametersAndVersionsRejected() {var catalog=HelengineCompositionTestData.Catalog();Assert.Throws<InvalidDataException>(()=>catalog.ValidateEffect("crossfade",2,new Dictionary<string,JsonElement>()));Assert.Throws<InvalidDataException>(()=>catalog.ValidateEffect("crossfade",1,new Dictionary<string,JsonElement>{{"shader",JsonSerializer.SerializeToElement("code")}}));}
}
