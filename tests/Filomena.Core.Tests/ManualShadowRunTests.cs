using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Core.Tests.Outbound;
using Xunit;

namespace Filomena.Core.Tests;

public sealed class ManualShadowRunTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("manual-shadow-").FullName;
    private static readonly SessionKey Session = new("manual", "agent", "whatsapp", "default", "5511944558218@s.whatsapp.net");
    private const string Peer = "whatsapp:5511944558218@s.whatsapp.net";
    private static ShadowSessionBinding Binding => new(Session, FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId), ["self"], [Peer]);
    private static OutboundBindings Roles => new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged");
    private ManualShadowRun Create(string mode = "boundary", string actor = Peer) => new(new(ShadowRuntimeTests.Project(false)), Binding,
        actor, "Helena", directory, Roles, new ShadowRuntimeTests.Completion(mode), new ShadowRuntimeTests.Reader(mode));

    [Fact]
    public async Task Manual_multiturn_uses_real_runtime_once_and_never_records_spoken_history()
    {
        await using var run = Create();
        var a = await run.InjectAsync("Filomena, o trabalho demorou.", TestContext.Current.CancellationToken);
        var b = await run.InjectAsync("E agora?", TestContext.Current.CancellationToken);
        Assert.StartsWith($"shadow:{run.RunId}:", a.InboundId);
        Assert.NotEqual(a.InboundId, b.InboundId);
        Assert.Equal("manual_shadow_harness", a.Source);
        Assert.Equal(2, run.Outcomes.Count);
        Assert.Equal(2, run.History.Count);
        Assert.All(run.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        Assert.All(run.Outcomes, o => Assert.False(o.TransitionApplied));
        Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
        Assert.Contains(run.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "shadowed");
        Assert.Contains(run.Trace, t => t.EventName == "node-hypothetical-transition");
    }

    [Fact]
    public async Task Interaction_waits_for_normal_quiet_window_before_returning()
    {
        var project = ShadowRuntimeTests.Project(false);
        var doc = System.Text.Json.Nodes.JsonNode.Parse(project.Document.GetRawText())!;
        var batch = doc["states"]![0]!["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "batch")!;
        batch["settings"] = System.Text.Json.Nodes.JsonNode.Parse("""{"quietSeconds":0.1,"maxMessages":6,"maxWaitSeconds":1}""");
        await using var run = new ManualShadowRun(new(project with { Document = System.Text.Json.JsonSerializer.SerializeToElement(doc) }), Binding,
            Peer, "Helena", directory, Roles, new ShadowRuntimeTests.Completion("boundary"), new ShadowRuntimeTests.Reader("boundary"));
        var result = await run.InjectAsync("One message", TestContext.Current.CancellationToken);
        Assert.Single(result.Outcomes);
        Assert.Equal(SessionBatchCloseReason.QuietPeriod, result.Outcomes[0].CloseReason);
    }

    [Fact]
    public async Task New_run_does_not_restore_old_executions_or_history()
    {
        string id;
        await using (var first = Create())
        {
            id = first.RunId;
            await first.InjectAsync("Teste.", TestContext.Current.CancellationToken);
        }
        await using var second = Create();
        Assert.NotEqual(id, second.RunId);
        Assert.Empty(second.History);
        Assert.Empty(second.Outcomes);
        await second.InjectAsync("Novo teste.", TestContext.Current.CancellationToken);
        Assert.Single(second.Outcomes);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("self")]
    public void Unbound_or_self_actor_cannot_inject(string actor) => Assert.Throws<ArgumentException>(() => Create(actor: actor));

    [Fact]
    public async Task Memory_outage_never_reaches_shadow_sink()
    {
        await using var run = Create("outage");
        await run.InjectAsync("Teste.", TestContext.Current.CancellationToken);
        Assert.False(Assert.Single(run.Outcomes).Success);
        Assert.DoesNotContain(run.Trace, t => t.EventName == "node-outbound");
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.InjectAsync("Do not continue", TestContext.Current.CancellationToken));
    }
    public void Dispose() => Directory.Delete(directory, true);
}
