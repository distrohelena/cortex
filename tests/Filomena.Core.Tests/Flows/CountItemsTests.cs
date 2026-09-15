using System.Text.Json;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class CountItemsTests
{
    [Fact]
    public void RejectsOptionalListInsteadOfCoercingNull()
    {
        using var nullJson = JsonDocument.Parse("null");
        Assert.Throws<InvalidDataException>(() => FlowDomain.CountItems(nullJson.RootElement, new NullableType(new ArrayType(new StringType()))));
        Assert.Throws<InvalidDataException>(() => FlowDomain.CountItems(nullJson.RootElement, new ArrayType(new StringType())));
    }

    [Theory]
    [InlineData("[]", 0)]
    [InlineData("[\"a\",\"b\"]", 2)]
    public void CountsActualValuesWithoutStringConversion(string json, int expected)
    {
        using var values = JsonDocument.Parse(json);
        Assert.Equal(expected, FlowDomain.CountItems(values.RootElement, new ArrayType(new StringType())).GetInt32());
    }

    [Fact]
    public void CountsDomainListsAndOptionalElements()
    {
        using var empty = JsonDocument.Parse("[]");
        Assert.Equal(0, FlowDomain.CountItems(empty.RootElement, new ArrayType(FlowDomain.Memory)).GetInt32());
        Assert.Equal(0, FlowDomain.CountItems(empty.RootElement, new ArrayType(FlowDomain.Message)).GetInt32());
        using var values = JsonDocument.Parse("[null,null]");
        Assert.Equal(2, FlowDomain.CountItems(values.RootElement, new ArrayType(new NullableType(FlowDomain.Person))).GetInt32());
    }

    [Fact]
    public void CompilesLazyDomainListWithoutExecutionPins()
    {
        // History provides a real domain array; an Instruction cannot manufacture nested objects.
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new object[] { new { id = "in", kind = "dm" }, new { id = "history", kind = "history", settings = new { maxMessages = 20, maxCharacters = 5000, includeCharacterMessages = true, mode = "before-current-batch" } }, new { id = "count", kind = "count" }, new { id = "consumer", kind = "prompt", prompt = "consume", inputs = new[] { new { id = "n", name = "n", type = "number", required = true } }, responseFields = new[] { new { id = "text", name = "text", type = "text" } } } },
            edges = new[] { new { source = "history", sourcePort = "messages", target = "count", targetPort = "list" }, new { source = "count", sourcePort = "count", target = "consumer", targetPort = "n" } },
            controlEdges = new[] { new { source = "in", sourcePort = "then", target = "history", targetPort = "exec" }, new { source = "history", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });
        var flow = TypedFlowCompiler.Compile(graph, "in");
        Assert.Empty(flow.Nodes["count"].Exits);
        Assert.IsType<NumberType>(flow.Nodes["count"].Outputs.Single().Type);
    }
}
