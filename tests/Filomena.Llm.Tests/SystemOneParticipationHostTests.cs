using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Filomena.App;
using Cortex.Core.Flows;
using Cortex.Core.Shadow;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Outbound;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class SystemOneParticipationHostTests
{
    private static PinnedShadowProject Pin()
    {
        var bytes = Encoding.UTF8.GetBytes(SystemOneSummaryCompositionTests.Source().ToJsonString());
        return PinnedShadowProject.FromBytes(bytes, "host-test", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static SystemOneParticipationConfig Config() => new("systemone-participation:v1", "jev-1.13.0",
        ["dm-situation"], ["dm-presence"], []);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Operational_host_composition_reaches_shadow_with_summary_and_direct_rule(bool direct, bool regenerate)
    {
        using var http = new HttpClient(new SystemOneParticipationTests.Decisions(direct ? "direto" : "indireto", "responder", "jev-1.13.0"));
        var baseline = new SystemOneSummaryCompositionTests.Baseline(blockFirst: regenerate);
        var pin = Pin();
        var typedNodes = new List<string>();
        var completion = SystemOneParticipationHost.Create(Config(), pin, baseline, http,
            n => n == "TYPESAFE_API_KEY" ? "test-not-secret" : null, (node, _) => typedNodes.Add(node));
        var memory = new BoundaryCoverageV1Tests.Memory();
        await using var run = new ManualShadowRun(pin, BoundaryCoverageV1Tests.Binding, BoundaryCoverageV1Tests.Peer, "Helena",
            Directory.CreateTempSubdirectory("operational-jev-").FullName, BoundaryCoverageV1Tests.Roles, completion, memory);
        var result = await run.InjectAsync("Filomena, me ajuda com o relatório?", TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(result.Outcomes).Success);
        Assert.Contains(memory.Queries, q => q.Text == SystemOneSummaryCompositionTests.Summary);
        Assert.Equal(SystemOneSummaryCompositionTests.Summary, Assert.Single(baseline.Calls, r => r.NodeId == "dm-generation").Inputs.GetProperty("resumo_situacao").GetString());
        if (regenerate) Assert.Contains(baseline.Calls, r => r.NodeId == "dm-regeneration");
        Assert.Equal(direct ? 1 : 2, typedNodes.Count);
        Assert.DoesNotContain(baseline.Calls, r => r.NodeId == "dm-presence");
        Assert.Contains(result.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] == "shadowed");
        Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
        Assert.All(run.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        Assert.False(result.Outcomes[0].TransitionApplied);
    }

    [Fact]
    public async Task Host_opt_in_uses_real_adapter_and_missing_opt_in_keeps_baseline()
    {
        using var handler = new SystemOneParticipationTests.Decisions("direto", "responder", "jev-1.13.0");
        using var http = new HttpClient(handler);
        var baseline = new SystemOneOperationalCompositionTests.Completion("""{"resumo":"pedido"}""");
        var pin = Pin();
        Assert.Same(baseline, SystemOneParticipationHost.Create(null, pin, baseline, http, _ => null));
        var completion = SystemOneParticipationHost.Create(Config(), pin, baseline, http, n => n == "TYPESAFE_API_KEY" ? "test-not-secret" : null);
        using var result = JsonDocument.Parse(await completion.CompleteAsync(new FlowCompletionRequest("dm-situation", "classify",
            JsonSerializer.SerializeToElement(new { mensagens = "help" }), JsonDocument.Parse("""{"type":"object","additionalProperties":false,"properties":{"envolvimento":{"type":"string","enum":["direto","nenhum"]},"resumo":{"type":"string"}},"required":["envolvimento","resumo"]}""").RootElement.Clone(), "summary", "lmstudio", null), TestContext.Current.CancellationToken));
        Assert.Equal("direto", result.RootElement.GetProperty("envolvimento").GetString());
    }

    [Theory]
    [InlineData("credential")]
    [InlineData("version")]
    [InlineData("model")]
    [InlineData("node")]
    [InlineData("overlap")]
    [InlineData("unknown_question")]
    [InlineData("wrong_choices")]
    public void Invalid_host_binding_fails_before_network(string error)
    {
        using var http = new HttpClient();
        var config = error switch
        {
            "version" => Config() with { Version = "unknown" },
            "model" => Config() with { Model = "jev-latest" },
            "node" => Config() with { SituationNodeIds = ["dm-generation"] },
            "overlap" => Config() with { PresenceNodeIds = ["dm-situation"] },
            "unknown_question" => Config() with { ChoiceQuestions = new() { ["typo"] = new("classify", new Dictionary<string, string> { ["x"] = "x", ["y"] = "y" }) } },
            "wrong_choices" => Config() with { ChoiceQuestions = new() { ["envolvimento"] = new("classify", new Dictionary<string, string> { ["direto"] = "direct", ["wrong"] = "wrong" }) } },
            _ => Config()
        };
        Assert.Throws<InvalidDataException>(() => SystemOneParticipationHost.Create(config, Pin(), new SystemOneOperationalCompositionTests.Completion("{}"), http,
            _ => error == "credential" ? null : "test-not-secret"));
    }
}
