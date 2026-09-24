using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Core.Tests.Outbound;
using Xunit;

namespace Filomena.Core.Tests;

// Live composition with fakes only: trusted inbox, pinned project, host with a bound route, no Gateway.
public sealed class LiveHandoffTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "live-handoff-" + Guid.NewGuid());
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "default", "5511944558218@s.whatsapp.net");
    private const string Self = "whatsapp:5511999999999@s.whatsapp.net";
    private const string Peer = "whatsapp:5511944558218@s.whatsapp.net";
    private static readonly OutboundBindings Roles = new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged");
    private static readonly TransportSafetyCapabilities Complete = new(true, true, true, true, true, true, true);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public LiveHandoffTests() => Directory.CreateDirectory(directory);

    private static ShadowSessionBinding Binding() => new(Session, FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId), [Self], [Peer]);
    private static LiveRouteBinding Route() => LiveRouteBindings.FromShadow(Binding(), "opaque-route", Session.NativeConversationId, "Filomena", 1000);
    private static CanonicalShadowInbound Input(long sequence, string id) => new("epoch", sequence, Session, SessionEntryKind.Dm,
        FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId), Peer,
        new(id, Session.ChannelId, Session.AccountId, Session.NativeConversationId, DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
            JsonSerializer.SerializeToElement(new { texto = "O trabalho demorou.", remetente = new { nome = "Helena", stableId = Peer }, canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null })));
    private ShadowOutboundHost Host(PinnedShadowProject pin, TransportSafetyCapabilities? capabilities = null) =>
        new(Path.Combine(directory, "outbound.jsonl"), pin.Project.Document, Roles, [Session], capabilities ?? Complete);
    private ShadowInboundJournal Inbox(PinnedShadowProject pin) =>
        new(Path.Combine(directory, "inbox.jsonl"), Binding(), pin.Project.ProjectRevision, "epoch", 10);

    [Fact]
    public void RouteBindingDerivesFromTheTrustedSessionBindingWithoutNewIdentifiers()
    {
        var route = Route();
        Assert.Equal(Session, route.Session);
        Assert.Equal(("whatsapp", "default", Session.NativeConversationId), (route.Channel, route.AccountId, route.Recipient));
        Assert.Equal(new OutboundSpeaker("Filomena", Self), route.Speaker);
        Assert.Equal("opaque-route", route.TransportRoute);
        var twoSelves = new ShadowSessionBinding(Session, Binding().ConversationNamespace, [Self, "whatsapp:5511888888888@lid"], [Peer]);
        Assert.Throws<InvalidOperationException>(() => LiveRouteBindings.FromShadow(twoSelves, "opaque-route", Session.NativeConversationId, "Filomena", 1000));
    }

    [Fact]
    public async Task LiveHandoffAcceptsTrustedInboundAndCommitsConfirmedSendOnce()
    {
        var pin = new PinnedShadowProject(ShadowRuntimeTests.Project(false, sendExit: "sent"));
        var transport = new LiveRuntimeTests.SequencedTransport();
        using var host = Host(pin);
        host.BindLiveRoute(Route(), transport);
        host.Rearm(OutboundMode.Enabled);
        using var inbox = Inbox(pin);
        await using var handoff = new LiveRuntimeHandoff(pin, Binding(), inbox, host, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"));
        Assert.Equal("admitted", (await handoff.AcceptAsync(Input(11, "input-1"), Token)).Status);
        await handoff.DrainAsync(Token);
        var outcome = Assert.Single(handoff.Outcomes);
        Assert.True(outcome.Success, outcome.Error);
        Assert.True(outcome.TransitionApplied);
        Assert.Equal(2, handoff.History.Count);
        Assert.Equal(SessionMessageDirection.Outbound, handoff.History[1].Direction);
        Assert.Equal("provider-1", handoff.History[1].ProviderMessageId);
        Assert.Equal(("opaque-route", "Vamos por partes."), Assert.Single(transport.Sends));
        Assert.Contains(handoff.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "sent");
        Assert.False(host.RequiresReconciliation(Session));
        Assert.Contains(handoff.Audit, a => a.Status == "completed" && a.ExecutionId == outcome.ExecutionId);
        Assert.DoesNotContain("5511944558218", JsonSerializer.Serialize(handoff.Audit));
    }

    [Fact]
    public void ShadowHandoffRefusesAnEnabledHostAndLiveHandoffRefusesAnythingButEnabled()
    {
        var pin = new PinnedShadowProject(ShadowRuntimeTests.Project(false, sendExit: "sent"));
        var transport = new LiveRuntimeTests.SequencedTransport();
        using var host = Host(pin);
        host.BindLiveRoute(Route(), transport);
        host.Rearm(OutboundMode.Enabled);
        using var inbox = Inbox(pin);
        Assert.Throws<InvalidOperationException>(() => new ShadowRuntimeHandoff(pin, Binding(), inbox, host, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty")));
        host.Rearm(OutboundMode.Shadow);
        Assert.Throws<InvalidOperationException>(() => new LiveRuntimeHandoff(pin, Binding(), inbox, host, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty")));
        // Incomplete capabilities keep Enabled ineffective even with a bound route: the live composition cannot start.
        using var unproven = new ShadowOutboundHost(Path.Combine(directory, "unproven.jsonl"), pin.Project.Document, Roles, [Session], TransportSafetyCapabilities.Unproven);
        unproven.BindLiveRoute(Route(), transport);
        unproven.Rearm(OutboundMode.Enabled);
        Assert.Throws<InvalidOperationException>(() => new LiveRuntimeHandoff(pin, Binding(), inbox, unproven, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty")));
        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task LiveHandoffRecoversDurableReceiptIntoHistoryOnceAndRefusesIndeterminate()
    {
        var pin = new PinnedShadowProject(ShadowRuntimeTests.Project(false, sendExit: "sent"));
        var transport = new LiveRuntimeTests.SequencedTransport();
        using (var crashed = Host(pin))
        {
            crashed.BindLiveRoute(Route(), transport);
            crashed.Rearm(OutboundMode.Enabled);
            var execution = crashed.Begin(pin.Project.Document, "execution", Session, SessionEntryKind.Dm, ["input-0"]);
            Assert.Equal("sent", (await execution.SendAsync(Approve(execution), Token)).Status);
            // Process dies before the runtime commits the speech locally.
        }
        using var host = Host(pin);
        host.BindLiveRoute(Route(), transport);
        host.Rearm(OutboundMode.Enabled);
        Assert.True(host.RequiresReconciliation(Session));
        using (var inbox = new ShadowInboundJournal(Path.Combine(directory, "inbox-0.jsonl"), Binding(), pin.Project.ProjectRevision, "epoch", 10))
        {
            await using var handoff = new LiveRuntimeHandoff(pin, Binding(), inbox, host, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"));
            var speech = Assert.Single(handoff.History);
            Assert.Equal((SessionMessageDirection.Outbound, "provider-1", "candidate"), (speech.Direction, speech.ProviderMessageId, speech.Message.GetProperty("texto").GetString()));
            Assert.Equal(Self, speech.Message.GetProperty("remetente").GetProperty("stableId").GetString());
            Assert.False(host.RequiresReconciliation(Session));
            Assert.Empty(host.PendingSpeechCommits(Session));
            var recovered = Assert.Single(handoff.Audit, a => a.Status == "speech_recovered");
            Assert.DoesNotContain("provider-1", JsonSerializer.Serialize(recovered));
            // The echo of the recovered speech carries the host's own actor: self-suppressed at the trusted inbox, no execution.
            Assert.Equal("self", (await handoff.AcceptAsync(Input(11, "provider-1") with { ActorId = Self }, Token)).Status);
            Assert.Empty(handoff.Outcomes);
        }
        // The commit is durable: a later composition has nothing to recover and does not duplicate the speech.
        using (var inbox = new ShadowInboundJournal(Path.Combine(directory, "inbox-1.jsonl"), Binding(), pin.Project.ProjectRevision, "epoch", 10))
        {
            await using var handoff = new LiveRuntimeHandoff(pin, Binding(), inbox, host, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"));
            Assert.Empty(handoff.Audit);
            Assert.Empty(handoff.History);
        }
        Assert.Single(transport.Sends);

        using var uncertain = new ShadowOutboundHost(Path.Combine(directory, "uncertain.jsonl"), pin.Project.Document, Roles, [Session], Complete);
        uncertain.BindLiveRoute(Route(), new LiveRuntimeTests.SequencedTransport { SendError = new IOException("dropped") });
        uncertain.Rearm(OutboundMode.Enabled);
        var lost = uncertain.Begin(pin.Project.Document, "lost", Session, SessionEntryKind.Dm, ["input-9"]);
        Assert.Equal("indeterminate", (await lost.SendAsync(Approve(lost), Token)).Status);
        uncertain.Rearm(OutboundMode.Enabled);
        using var inbox2 = new ShadowInboundJournal(Path.Combine(directory, "inbox-uncertain.jsonl"), Binding(), pin.Project.ProjectRevision, "epoch", 10);
        var refused = Assert.Throws<InvalidOperationException>(() => new LiveRuntimeHandoff(pin, Binding(), inbox2, uncertain, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty")));
        Assert.Contains("reconciliation", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisarmedLiveHostPreservesInboundWithoutExecutionOrSend()
    {
        var pin = new PinnedShadowProject(ShadowRuntimeTests.Project(false, sendExit: "sent"));
        var transport = new LiveRuntimeTests.SequencedTransport();
        using var host = Host(pin);
        host.BindLiveRoute(Route(), transport);
        host.Rearm(OutboundMode.Enabled);
        using var inbox = Inbox(pin);
        await using var handoff = new LiveRuntimeHandoff(pin, Binding(), inbox, host, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"));
        host.Rearm(OutboundMode.Disabled);
        var decision = await handoff.AcceptAsync(Input(11, "input-1"), Token);
        Assert.Equal(("preserved", "deployment_disarmed_or_unresolved"), (decision.Status, decision.Diagnostic));
        Assert.Empty(handoff.Outcomes);
        Assert.Empty(transport.Sends);
        Assert.True(inbox.ReconciliationRequired);
    }

    [Fact]
    public async Task GroupReceiptRecoveryPreservesGroupKindWithoutResending()
    {
        var pin = new PinnedShadowProject(ShadowRuntimeTests.Project(false, sendExit: "sent"));
        var session = Session with { NativeConversationId = "120300000001@g.us" };
        var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId),
            [Self], [Peer], SessionEntryKind.Group);
        var route = LiveRouteBindings.FromShadow(binding, "group-route", session.NativeConversationId, "Filomena", 1000)
            with { AllowExactGroupDelivery = true };
        var transport = new GroupReceiptTransport();
        ShadowOutboundHost CreateHost() => new(Path.Combine(directory, "group-outbound.jsonl"), pin.Project.Document, Roles, [session], Complete);
        using (var crashed = CreateHost())
        {
            crashed.BindLiveRoute(route, transport);
            crashed.Rearm(OutboundMode.Enabled);
            var execution = crashed.Begin(pin.Project.Document, "group-execution", session, SessionEntryKind.Group, ["group-input"]);
            Assert.Equal("sent", (await execution.SendAsync(Approve(execution), Token)).Status);
        }
        using var host = CreateHost();
        host.BindLiveRoute(route, transport);
        host.Rearm(OutboundMode.Enabled);
        using var inbox = new ShadowInboundJournal(Path.Combine(directory, "group-inbox.jsonl"), binding, pin.Project.ProjectRevision, "epoch", 10);
        await using var handoff = new LiveRuntimeHandoff(pin, binding, inbox, host, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"));
        var speech = Assert.Single(handoff.History);
        Assert.Equal("Grupo", speech.Message.GetProperty("canal").GetProperty("nome").GetString());
        Assert.Equal(1, transport.Calls);
        Assert.Empty(host.PendingSpeechCommits(session));
    }

    private sealed class GroupReceiptTransport : IBoundOutboundTransport
    {
        public int Calls { get; private set; }
        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken token) =>
            Task.FromResult(new TransportRouteDescription("whatsapp", "default", "120300000001@g.us"));
        public Task<SendReceipt> SendTextAsync(string route, string text, CancellationToken token) => throw new NotSupportedException();
        public Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new SendReceipt("group-receipt") { Channel = request.Channel, AccountId = request.AccountId, Recipient = request.Recipient });
        }
    }

    private static TypedValue Approve(OutboundExecution execution)
    {
        var recall = new TypedValue(new ArrayType(FlowDomain.Memory), JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var boundaries = new TypedValue(new ArrayType(FlowDomain.Boundary), JsonSerializer.SerializeToElement(new object[] { new { trusted = true } }));
        execution.Recall("recall", recall);
        execution.Compiled("compile", recall, boundaries);
        var candidate = new TypedValue(new StringType(), JsonSerializer.SerializeToElement("candidate"));
        execution.Generated("generate", new Dictionary<string, TypedValue> { ["text"] = candidate });
        execution.Enforced("enforce", candidate, boundaries, true, "revision");
        return candidate;
    }

    public void Dispose() => Directory.Delete(directory, true);
}
