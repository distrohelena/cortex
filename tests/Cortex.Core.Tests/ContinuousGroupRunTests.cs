using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Conversations;
using Cortex.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using Cortex.Core.Tests.Outbound;
using Xunit;

namespace Cortex.Core.Tests;

public sealed class ContinuousGroupRunTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("continuous-group-").FullName;
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "default", "123456@g.us");
    private const string Peer = "whatsapp:5511944558218@s.whatsapp.net";
    private static readonly ShadowSessionBinding Binding = new(Session,
        FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId), ["self"], [Peer], SessionEntryKind.Group);

    [Theory]
    [InlineData(false, 5, false)]
    [InlineData(true, 7, false)]
    [InlineData(true, 7, true)]
    public async Task Explicit_continuous_authority_removes_only_the_count_fence(bool continuous, int count, bool allMembers)
    {
        var binding = new ShadowSessionBinding(Session, Binding.ConversationNamespace, ["self"], [Peer], SessionEntryKind.Group);
        if (allMembers) binding.EnableNativeGroupParticipants();
        var source = ShadowRuntimeTests.Project(false, sendExit: "sent", history: true);
        var document = JsonNode.Parse(source.Document.GetRawText())!;
        foreach (var state in document["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray().Where(n => n!["kind"]!.GetValue<string>() == "dm"))
                node!["kind"] = "group";
        var pin = new PinnedShadowProject(source with { Document = JsonSerializer.SerializeToElement(document) });
        var sender = new Sender();
        var fenceCalls = 0;
        await using var run = new ObservedLiveRun(
            pin, binding, directory,
            new OutboundBindings(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged"),
            new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"), "epoch", 0L,
            LiveRouteBindings.FromShadow(binding, "route", Session.NativeConversationId, "Agent", 1000) with { AllowExactGroupDelivery = true },
            sender, new TransportSafetyCapabilities(true, true, true, true, true, true, true),
            _ => { fenceCalls++; return Task.CompletedTask; }, 5, continuous);
        if (allMembers)
        {
            var media = Input(1) with { SkipNonText = true, ActorId = "whatsapp:19999@s.whatsapp.net" };
            var skipped = await run.AcceptAsync([media], TestContext.Current.CancellationToken);
            Assert.Equal("ignored", Assert.Single(skipped.Decisions).Status);
            Assert.Empty(skipped.Outcomes);
            Assert.Equal(0, sender.Calls);
            Assert.Equal("duplicate", Assert.Single((await run.AcceptAsync([media], TestContext.Current.CancellationToken)).Decisions).Status);
        }
        for (var i = 1; i <= count; i++)
        {
            var input = Input(i + (allMembers ? 1 : 0));
            if (allMembers) input = input with { ActorId = "whatsapp:" + (19999 + i % 2) + "@s.whatsapp.net" };
            var result = await run.AcceptAsync([input], TestContext.Current.CancellationToken);
            Assert.Equal("admitted", Assert.Single(result.Decisions).Status);
            Assert.True(Assert.Single(result.Outcomes).Success);
            Assert.Equal(i, sender.Calls);
            if (i < count || continuous)
            {
                Assert.Equal("duplicate", Assert.Single((await run.AcceptAsync([input], TestContext.Current.CancellationToken)).Decisions).Status);
                Assert.Equal(i, sender.Calls);
            }
        }
        Assert.Equal(count, fenceCalls);
        Assert.Equal(continuous ? OutboundMode.Enabled : OutboundMode.Disabled, run.OutboundMode);
    }

    private static CanonicalShadowInbound Input(int i) => new("epoch", i, Session, SessionEntryKind.Group,
        Binding.ConversationNamespace, Peer, new("native-" + i, Session.ChannelId, Session.AccountId, Session.NativeConversationId,
            DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Group,
            JsonSerializer.SerializeToElement(new { texto = "Oi", remetente = new { nome = "Peer" } })));

    private sealed class Sender : IBoundOutboundTransport
    {
        public int Calls { get; private set; }
        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken token)
            => Task.FromResult(new TransportRouteDescription(Session.ChannelId, Session.AccountId, Session.NativeConversationId));
        public Task<SendReceipt> SendTextAsync(string route, string text, CancellationToken token) => throw new InvalidOperationException();
        public Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken token)
            => Task.FromResult(new SendReceipt("sent-" + ++Calls) { Channel = Session.ChannelId, AccountId = Session.AccountId,
                Recipient = Session.NativeConversationId, GatewayRunId = "run-" + Calls });
    }
    public void Dispose() => Directory.Delete(directory, true);
}
