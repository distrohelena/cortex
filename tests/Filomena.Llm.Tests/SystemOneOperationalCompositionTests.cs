using System.Text.Json;
using Cortex.Core.Flows;
using Filomena.Llm.SystemOne;
using Cortex.Inference.SystemOne;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class SystemOneOperationalCompositionTests
{
    private static FlowCompletionRequest Request(string id = "selected-situation") => new(id, "original instructions",
        JsonSerializer.SerializeToElement(new { mensagens = "Filomena, me ajuda?" }),
        JsonDocument.Parse("""{"type":"object","additionalProperties":false,"properties":{"envolvimento":{"type":"string","enum":["direto","nenhum"]},"resumo":{"type":"string"}},"required":["envolvimento","resumo"]}""").RootElement.Clone(),
        "original-summary-model", "lmstudio", null);

    [Fact]
    public async Task Explicit_situation_route_merges_typed_decision_and_original_summary()
    {
        var typed = new Completion("""{"envolvimento":"direto"}""");
        var baseline = new Completion("""{"resumo":"pedido de ajuda"}""");
        var adapter = new SystemOneParticipationCompletion(typed, baseline, "jev", "jev-1.13.0", new HashSet<string> { "selected-situation" }, new HashSet<string> { "selected-presence" });
        using var output = JsonDocument.Parse(await adapter.CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal("direto", output.RootElement.GetProperty("envolvimento").GetString());
        Assert.Equal("pedido de ajuda", output.RootElement.GetProperty("resumo").GetString());
        Assert.Equal("jev", Assert.Single(typed.Requests).Provider);
        Assert.False(typed.Requests[0].ResponseSchema!.Value.GetProperty("properties").TryGetProperty("resumo", out _));
        var summary = Assert.Single(baseline.Requests);
        Assert.Equal("original-summary-model", summary.Model);
        Assert.Equal(Request().System, summary.System);
        Assert.True(JsonElement.DeepEquals(Request().Inputs, summary.Inputs));
    }

    [Fact]
    public async Task Matching_suffix_without_explicit_binding_does_not_route_to_typed_model()
    {
        var typed = new Completion("{}");
        var baseline = new Completion("unchanged");
        var adapter = new SystemOneParticipationCompletion(typed, baseline, "jev", "jev-1.13.0", new HashSet<string> { "selected-situation" }, new HashSet<string> { "selected-presence" });
        var request = Request("other-situation");
        Assert.Equal("unchanged", await adapter.CompleteAsync(request, TestContext.Current.CancellationToken));
        Assert.Empty(typed.Requests);
        Assert.Same(request, Assert.Single(baseline.Requests));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"resumo\":null}")]
    [InlineData("{\"resumo\":\" \"}")]
    [InlineData("{\"resumo\":\"ok\",\"extra\":true}")]
    [InlineData("{\"resumo\":\"ok\",\"resumo\":\"second\"}")]
    public async Task Invalid_summary_never_emits_partial_situation(string response)
    {
        var adapter = new SystemOneParticipationCompletion(new Completion("""{"envolvimento":"direto"}"""), new Completion(response),
            "jev", "jev-1.13.0", new HashSet<string> { "selected-situation" }, new HashSet<string>());
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.CompleteAsync(Request(), TestContext.Current.CancellationToken));
    }

    internal sealed class Completion(string response) : IFlowCompletion
    {
        internal List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult(response);
        }
    }
}
