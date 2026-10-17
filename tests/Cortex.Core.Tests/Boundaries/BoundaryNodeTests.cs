using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class BoundaryNodeTests
{
    [Fact]
    public void Compiles_operational_memory_to_boundary_contract_and_roundtrips()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "in", kind = "dm" },
                new { id = "memory", kind = "memory", settings = new { mode = "semantic", scope = "conversation", categories = new[] { "limite" }, maxItems = 4, maxCharacters = 700, activeOnly = true, queryText = "limites" } },
                new { id = "interpret", kind = "interpretBoundaries",
                    settings = new { taxonomyVersion = "v1", grammarVersion = "v1", templatesVersion = "v1" } },
                new { id = "consumer", kind = "prompt", prompt = "Validate", inputs = new[] { new { id = "b", name = "limites", type = "boundary[]", required = true } } }
            },
            edges = new[] { new { source = "memory", sourcePort = "memories", target = "interpret", targetPort = "memories" }, new { source = "interpret", sourcePort = "boundaries", target = "consumer", targetPort = "b" } },
            controlEdges = new[] { new { source = "in", sourcePort = "then", target = "memory", targetPort = "exec" }, new { source = "memory", sourcePort = "then", target = "interpret", targetPort = "exec" }, new { source = "interpret", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });
        var roundtrip = JsonSerializer.Deserialize<JsonElement>(graph.GetRawText());
        var compiled = TypedFlowCompiler.Compile(roundtrip, "in");
        Assert.Equal(new[] { "then" }, compiled.Nodes["interpret"].Exits);
        Assert.Equal(new ArrayType(FlowDomain.Boundary), compiled.Nodes["interpret"].Outputs.Single().Type);
    }

    [Fact]
    public void Domain_serializer_rejects_fabricated_boundary_without_execution_registry()
    {
        var fake = JsonSerializer.SerializeToElement(new { status = "compiled", kind = "no_teasing_about_topic", originalText = "x", topicEvidence = "x", constraintEvidence = "x" });
        Assert.Throws<InvalidDataException>(() => FlowDomain.ForModel(fake, FlowDomain.Boundary, new Dictionary<string, string>()));
    }
}
