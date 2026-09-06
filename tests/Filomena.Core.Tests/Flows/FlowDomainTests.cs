using System.Text.Json;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class FlowDomainTests
{
    [Fact]
    public void PropertiesPreserveIdentityAndNullableListShape()
    {
        var message = JsonSerializer.SerializeToElement(new { texto = "oi", remetente = new { nome = "Helena", stableId = "fixture:helena", secret = "hidden" }, canal = new { nome = "DM", nativeId = "private-id" }, reply = (object?)null, token = "hidden" });
        Assert.IsType<StringType>(FlowDomain.PropertyType(FlowDomain.Message, ["texto"]));
        Assert.Equal("oi", FlowDomain.Project(message, FlowDomain.Message, ["texto"]).GetString());
        Assert.IsType<NullableType>(FlowDomain.PropertyType(FlowDomain.Message, ["remetente"]));
        var person = FlowDomain.Project(message, FlowDomain.Message, ["remetente"]);
        Assert.Equal("fixture:helena", person.GetProperty("stableId").GetString());
        var list = JsonSerializer.SerializeToElement(new[] { message, message });
        Assert.IsType<NullableType>(((ArrayType)FlowDomain.PropertyType(new ArrayType(FlowDomain.Message), ["remetente"])).ElementType);
        Assert.Equal(2, FlowDomain.Project(list, new ArrayType(FlowDomain.Message), ["remetente"]).GetArrayLength());
        var safe = FlowDomain.ForModel(message, FlowDomain.Message, new Dictionary<string, string>()).GetRawText();
        Assert.DoesNotContain("hidden", safe); Assert.DoesNotContain("fixture:helena", safe); Assert.DoesNotContain("private-id", safe);
        Assert.Throws<InvalidDataException>(() => FlowDomain.PropertyType(FlowDomain.Message, ["token"]));
        Assert.False(FlowDomain.Message.IsCompatibleWith(new StringType()));
        Assert.False(new StringType().IsCompatibleWith(new EnumType(["a"])));
        Assert.False(new NullableType(FlowDomain.Person).IsCompatibleWith(FlowDomain.Person));
        Assert.Throws<InvalidDataException>(() => FlowDomain.Project(JsonSerializer.SerializeToElement<object?>(null), FlowDomain.Message, ["texto"]));
        Assert.Equal(JsonValueKind.Null, FlowDomain.Project(message, FlowDomain.Message, ["reply", "id"]).ValueKind);
    }
}
