using System.Text.Json.Nodes;
using Xunit;

namespace Cortex.Core.Tests.ResponsePlans;

public sealed class ResponsePlanSpikeTests
{
    private const string Valid = """
        {"acts":[{"target":"person_0","intent":"advise","content":"resolver uma etapa por vez","grounding":"generic"}]}
        """;

    [Theory]
    [InlineData(Valid)]
    [InlineData("{\"acts\":[]}")]
    public void Accepts_valid_and_empty_plans(string raw) => Assert.Null(ResponsePlanSpike.Validate(raw, ["person_0"]));

    [Theory]
    [InlineData("target", "invented_person")]
    [InlineData("intent", "judge")]
    [InlineData("grounding", "certain")]
    [InlineData("content", "  ")]
    public void Rejects_invalid_fields(string field, string value)
    {
        var data = JsonNode.Parse(Valid)!; data["acts"]![0]![field] = value;
        Assert.NotNull(ResponsePlanSpike.Validate(data.ToJsonString(), ["person_0"]));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"acts\":null}")]
    [InlineData("{\"acts\":[],\"rationale\":\"x\"}")]
    [InlineData("{\"acts\":[],\"acts\":[]}")]
    [InlineData("{\"acts\":[{}]}")]
    public void Rejects_invalid_shape(string raw) => Assert.NotNull(ResponsePlanSpike.Validate(raw, ["person_0"]));

    [Fact]
    public void Structural_validity_does_not_claim_semantic_authorization()
    {
        const string dangerous = """
            {"acts":[{"target":"person_0","intent":"evaluate","content":"está em pânico","grounding":"unsupported"}]}
            """;
        Assert.Null(ResponsePlanSpike.Validate(dangerous, ["person_0"]));
    }

    [Fact]
    public void Rejects_extra_fields_and_overlong_content()
    {
        var data = JsonNode.Parse(Valid)!; data["acts"]![0]!["score"] = 1;
        Assert.NotNull(ResponsePlanSpike.Validate(data.ToJsonString(), ["person_0"]));
        data = JsonNode.Parse(Valid)!; data["acts"]![0]!["content"] = new string('a', 401);
        Assert.NotNull(ResponsePlanSpike.Validate(data.ToJsonString(), ["person_0"]));
    }
}
