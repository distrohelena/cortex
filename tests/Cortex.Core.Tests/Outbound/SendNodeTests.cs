using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Outbound;

public sealed class SendNodeTests
{
    [Fact]
    public void MinimalTextOnlyContract()
    {
        var flow = TypedFlowCompiler.Compile(Graph(), "in");
        var send = flow.Nodes["send"];
        Assert.Equal("message", Assert.Single(send.Inputs).Id);
        Assert.IsType<StringType>(send.Inputs.Single().Type);
        Assert.Equal(new[] { "status", "diagnostico" }, send.Outputs.Select(p => p.Id));
    }

    [Theory]
    [InlineData("\"target\":\"someone\"")]
    [InlineData("\"settings\":{\"mode\":\"enabled\"}")]
    [InlineData("\"recipient\":\"someone\"")]
    [InlineData("\"executionProfile\":\"normal\"")]
    public void RejectsProjectAuthorityAndRecipient(string field) => Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Graph("," + field), "in"));

    private static JsonElement Graph(string extra = "") => JsonDocument.Parse("""
        {"version":4,"nodes":[{"id":"in","kind":"dm"},
        {"id":"ai","kind":"prompt","prompt":"answer","inputs":[],"outputMode":"text","responseFields":[{"id":"text","name":"text","type":"text"}]},
        {"id":"send","kind":"send"EXTRA}],
        "edges":[{"source":"ai","sourcePort":"text","target":"send","targetPort":"message"}],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"ai","targetPort":"exec"},{"source":"ai","sourcePort":"then","target":"send","targetPort":"exec"}]}
        """.Replace("EXTRA", extra, StringComparison.Ordinal)).RootElement.Clone();
}
