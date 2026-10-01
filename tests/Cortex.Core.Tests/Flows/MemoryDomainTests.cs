using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class MemoryDomainTests
{
    [Fact]
    public void MemoryProjectsOnlyKnownDomainPropertiesIncludingLists()
    {
        var memory = FlowDomain.Parse(JsonSerializer.SerializeToElement(new { type = "memory" }));
        var value = JsonSerializer.SerializeToElement(new { conteudo = "não brincar com X", categoria = "limite" });
        Assert.IsType<StringType>(FlowDomain.PropertyType(memory, ["conteudo"]));
        var category = Assert.IsType<EnumType>(FlowDomain.PropertyType(memory, ["categoria"]));
        Assert.Equal(["limite", "apelido", "relacao", "fato", "nao_classificada"], category.Values);
        Assert.Equal("não brincar com X", FlowDomain.Project(value, memory, ["conteudo"]).GetString());
        var list = JsonSerializer.SerializeToElement(new[] { value, value });
        Assert.Equal(["não brincar com X", "não brincar com X"],
            FlowDomain.Project(list, new ArrayType(memory), ["conteudo"]).EnumerateArray().Select(v => v.GetString()));
        Assert.Throws<InvalidDataException>(() => FlowDomain.PropertyType(memory, ["namespace"]));
        Assert.False(memory.IsCompatibleWith(new StringType()));
    }

    [Fact]
    public void MemorySerializationDoesNotExposeTechnicalFields()
    {
        var memory = FlowDomain.Parse(JsonSerializer.SerializeToElement(new { type = "memory" }));
        var value = JsonSerializer.SerializeToElement(new
        {
            conteudo = "gosta de Persona 4 Golden",
            categoria = "fato",
            uuid = "never-send-uuid",
            group_id = "never-send-namespace",
            score = 0.9,
            raw = new { secret = "never-send-secret" }
        });
        var payload = FlowDomain.ForModel(value, memory, new Dictionary<string, string>());
        Assert.Equal("gosta de Persona 4 Golden", payload.GetProperty("conteudo").GetString());
        Assert.Equal("fato", payload.GetProperty("categoria").GetString());
        Assert.DoesNotContain("never-send", payload.GetRawText());
        Assert.Equal(2, payload.EnumerateObject().Count());
    }
}
