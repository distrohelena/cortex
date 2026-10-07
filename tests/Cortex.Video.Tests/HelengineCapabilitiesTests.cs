using System.Text.Json;
using Cortex.Video.Composition;
using Xunit;
namespace Cortex.Video.Tests;
public sealed class HelengineCapabilitiesTests {
    [Fact] public void CatalogVersionPinned() {var catalog=HelengineCompositionTestData.Catalog();Assert.True(catalog.Supports("crossfade",1));Assert.False(catalog.Supports("crossfade",2));Assert.Contains("smoothstep.v1",catalog.Curves);Assert.Equal(64,catalog.Fingerprint.Length);}
    [Fact] public void UnknownParametersAndVersionsRejected() {var catalog=HelengineCompositionTestData.Catalog();Assert.Throws<InvalidDataException>(()=>catalog.ValidateEffect("crossfade",2,new Dictionary<string,JsonElement>()));Assert.Throws<InvalidDataException>(()=>catalog.ValidateEffect("crossfade",1,new Dictionary<string,JsonElement>{{"shader",JsonSerializer.SerializeToElement("code")}}));}
}
