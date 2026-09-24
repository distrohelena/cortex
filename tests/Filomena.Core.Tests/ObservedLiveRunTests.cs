using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Core.Tests.Outbound;
using Xunit;

namespace Filomena.Core.Tests;

public sealed class ObservedLiveRunTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("observed-live-").FullName;
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "default", "5511944558218@s.whatsapp.net");
    private const string Peer = "whatsapp:5511944558218@s.whatsapp.net";
    private static ShadowSessionBinding Binding => new(Session, FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId), ["self"], [Peer]);
    private static OutboundBindings Roles => new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private ObservedLiveRun Create(LiveRuntimeTests.SequencedTransport transport, Func<CancellationToken, Task>? fence = null,
        TransportSafetyCapabilities? capabilities = null, int maxInteractions = 1, string epoch = "epoch", long tail = 10,
        int version = 1) => new(new(ShadowRuntimeTests.Project(false, sendExit: "sent", history: true, version: version)), Binding,
        directory, Roles, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"), epoch, tail,
        LiveRouteBindings.FromShadow(Binding, "opaque-route", Session.NativeConversationId, "Filomena", 1000), transport,
        capabilities ?? new(true, true, true, true, true, true, true), fence ?? (_ => Task.CompletedTask), maxInteractions);

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task Approved_DM_policy_keeps_old_inbound_in_selected_context_after_reopen(bool removeAge, int expectedCount)
    {
        var source = ShadowRuntimeTests.Project(false, sendExit: "sent", history: true);
        var document = JsonNode.Parse(source.Document.GetRawText())!;
        if (removeAge)
            foreach (var state in document["states"]!.AsArray())
                state!["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "history")!["settings"]!["maxAgeMinutes"] = null;
        var pin = new PinnedShadowProject(source with { Document = JsonSerializer.SerializeToElement(document) });
        ObservedLiveRun Open(LiveRuntimeTests.SequencedTransport sender) => new(pin, Binding, directory, Roles,
            new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"), "epoch", 10,
            LiveRouteBindings.FromShadow(Binding, "opaque-route", Session.NativeConversationId, "Filomena", 1000), sender,
            new(true, true, true, true, true, true, true), _ => Task.CompletedTask);
        var old = Input(11, "yesterday");
        old = old with { Message = old.Message with { Timestamp = DateTimeOffset.UtcNow.AddDays(-1) } };
        await using (var first = Open(new() { ProviderPrefix = "old-" }))
            await first.AcceptAsync([old], Token);
        var sender = new LiveRuntimeTests.SequencedTransport();
        await using var next = Open(sender);
        Assert.Empty(sender.Sends);
        Assert.Equal("duplicate", Assert.Single((await next.AcceptAsync([old], Token)).Decisions).Status);
        var result = await next.AcceptAsync([Input(12, "today")], Token);
        var selected = Assert.Single(result.Outcomes).Outputs["history"]["messages"].EnumerateArray().ToArray();
        Assert.Equal(expectedCount, selected.Length);
        if (removeAge) Assert.Equal(old.Message.Message.GetProperty("texto").GetString(), selected[0].GetProperty("texto").GetString());
        Assert.Single(sender.Sends);
    }

    [Fact]
    public async Task Native_events_missed_before_poll_recover_in_order_once_across_restart()
    {
        // The old window anchored successfully but never admitted the provider events.
        await using (var original = Create(new(), maxInteractions: 5))
            Assert.Empty(original.Outcomes);
        var first = Input(11, "original-native-1");
        var second = Input(12, "original-native-2");
        var sender = new LiveRuntimeTests.SequencedTransport();
        await using (var recovery = Create(sender, maxInteractions: 5))
        {
            Assert.Equal("admitted", Assert.Single((await recovery.AcceptAsync([first], Token)).Decisions).Status);
            Assert.Equal("admitted", Assert.Single((await recovery.AcceptAsync([second], Token)).Decisions).Status);
            Assert.Equal(2, sender.Sends.Count);
            Assert.Equal(new[] { "original-native-1", "original-native-2" }, recovery.History
                .Where(h => h.Direction == SessionMessageDirection.Inbound).Select(h => h.ProviderMessageId));
        }
        var afterRestart = new LiveRuntimeTests.SequencedTransport();
        await using var repeated = Create(afterRestart, maxInteractions: 5);
        Assert.Equal("duplicate", Assert.Single((await repeated.AcceptAsync([first], Token)).Decisions).Status);
        Assert.Equal("duplicate", Assert.Single((await repeated.AcceptAsync([second], Token)).Decisions).Status);
        Assert.Empty(afterRestart.Sends);
        Assert.Empty(repeated.Outcomes);
    }

    [Fact]
    public async Task New_epoch_restores_conversation_but_not_old_execution_authority()
    {
        await using (var first = Create(new LiveRuntimeTests.SequencedTransport { ProviderPrefix = "old-" }))
            await first.AcceptAsync([Input(11, "old")], Token);
        var sender = new LiveRuntimeTests.SequencedTransport();
        await using var next = Create(sender, epoch: "epoch-B", tail: 0);
        Assert.Equal(2, next.History.Count);
        Assert.Empty(next.Outcomes);
        Assert.Empty(sender.Sends);
        Assert.Equal("duplicate", Assert.Single((await next.AcceptAsync([Input(1, "old") with { StreamEpoch = "epoch-B" }], Token)).Decisions).Status);
        var result = await next.AcceptAsync([Input(2, "new") with { StreamEpoch = "epoch-B" }], Token);
        Assert.Equal("engaged", Assert.Single(result.Outcomes).StateId);
        Assert.Single(sender.Sends);
    }

    [Fact]
    public async Task Changed_project_cannot_reuse_checkpoint_or_erase_it()
    {
        await using (var first = Create(new LiveRuntimeTests.SequencedTransport()))
            await first.AcceptAsync([Input(11, "one")], Token);
        var original = File.ReadAllBytes(Path.Combine(directory, "observation-live-context.json"));
        Assert.Throws<InvalidDataException>(() => Create(new LiveRuntimeTests.SequencedTransport(), version: 2));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(directory, "observation-live-context.json")));
    }

    [Fact]
    public async Task Uncertain_attempt_cannot_become_spoken_context_after_restart()
    {
        await using (var first = Create(new LiveRuntimeTests.SequencedTransport { SendError = new IOException("uncertain") }))
            await first.AcceptAsync([Input(11, "one")], Token);
        var sender = new LiveRuntimeTests.SequencedTransport();
        Assert.Throws<InvalidDataException>(() => Create(sender));
        Assert.Empty(sender.Sends);
    }

    [Fact]
    public async Task Parseable_checkpoint_corruption_cannot_change_conversation()
    {
        await using (var first = Create(new LiveRuntimeTests.SequencedTransport()))
            await first.AcceptAsync([Input(11, "one")], Token);
        var path = Path.Combine(directory, "observation-live-context.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Vamos por partes.", "Forged historical statement.", StringComparison.Ordinal));
        ObservedLiveRun? unexpected = null;
        try { Assert.Throws<InvalidDataException>(() => unexpected = Create(new LiveRuntimeTests.SequencedTransport())); }
        finally { if (unexpected is not null) await unexpected.DisposeAsync(); }
    }

    [Fact]
    public async Task New_live_window_restores_context_and_state_without_replaying_or_sending()
    {
        var firstSender = new LiveRuntimeTests.SequencedTransport { ProviderPrefix = "before-" };
        await using (var first = Create(firstSender))
            await first.AcceptAsync([Input(11, "before-reconnect")], Token);

        var sender = new LiveRuntimeTests.SequencedTransport();
        await using var resumed = Create(sender);
        Assert.Equal(2, resumed.History.Count);
        Assert.Empty(resumed.Outcomes);
        Assert.Empty(sender.Sends);
        var duplicate = await resumed.AcceptAsync([Input(11, "before-reconnect")], Token);
        Assert.Equal("duplicate", Assert.Single(duplicate.Decisions).Status);
        Assert.Empty(duplicate.Outcomes);
        var next = await resumed.AcceptAsync([Input(12, "after-reconnect")], Token);
        var outcome = Assert.Single(next.Outcomes);
        Assert.Equal("engaged", outcome.StateId);
        var history = outcome.Outputs["history"]["messages"].EnumerateArray().ToArray();
        Assert.Equal(2, history.Length);
        Assert.Equal("Vamos por partes.", history[1].GetProperty("texto").GetString());
        Assert.Single(sender.Sends);
        Assert.Equal(4, resumed.History.Count);
    }

    [Fact]
    public async Task Missing_checkpoint_with_prior_work_fails_closed_instead_of_losing_context()
    {
        await using (var first = Create(new LiveRuntimeTests.SequencedTransport()))
            await first.AcceptAsync([Input(11, "one")], Token);
        File.Delete(Path.Combine(directory, "observation-live-context.json"));
        var sender = new LiveRuntimeTests.SequencedTransport();
        Assert.Throws<InvalidDataException>(() => Create(sender));
        Assert.Empty(sender.Sends);
    }

    [Fact]
    public async Task Journal_changed_since_checkpoint_is_not_silently_resumed()
    {
        await using (var first = Create(new LiveRuntimeTests.SequencedTransport()))
            await first.AcceptAsync([Input(11, "one")], Token);
        File.AppendAllText(Path.Combine(directory, "observation-live-inbound.jsonl"), "\n");
        var sender = new LiveRuntimeTests.SequencedTransport();
        Assert.Throws<InvalidDataException>(() => Create(sender));
        Assert.Empty(sender.Sends);
    }

    [Fact]
    public async Task Bounded_live_window_preserves_sent_history_and_state_for_five_turns_then_stops()
    {
        var sender = new LiveRuntimeTests.SequencedTransport();
        var checks = 0;
        await using var run = Create(sender, _ => { checks++; return Task.CompletedTask; }, maxInteractions: 5);
        for (var ordinal = 1; ordinal <= 5; ordinal++)
        {
            var result = await run.AcceptAsync([Input(10 + ordinal, "turn-" + ordinal)], Token);
            var outcome = Assert.Single(result.Outcomes);
            Assert.True(outcome.Success, outcome.Error);
            Assert.Equal(ordinal == 1 ? "initial" : "engaged", outcome.StateId);
            Assert.True(outcome.TransitionApplied);
            var context = outcome.Outputs["history"]["messages"].EnumerateArray().ToArray();
            Assert.Equal((ordinal - 1) * 2, context.Length);
            Assert.Equal(ordinal - 1, context.Count(m => m.GetProperty("texto").GetString() == "Vamos por partes."));
            Assert.Equal(ordinal * 2, run.History.Count);
            Assert.Equal(ordinal, sender.Sends.Count);
            Assert.Equal(ordinal == 5 ? OutboundMode.Disabled : OutboundMode.Enabled, run.OutboundMode);
        }
        Assert.Equal(5, checks);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.AcceptAsync([Input(16, "six")], Token));
        Assert.Equal(5, sender.Sends.Count);
    }

    [Fact]
    public async Task Echo_and_repeated_poll_do_not_spend_another_turn_or_send()
    {
        var sender = new LiveRuntimeTests.SequencedTransport();
        await using var run = Create(sender, maxInteractions: 2);
        await run.AcceptAsync([Input(11, "one")], Token);
        Assert.Equal("duplicate", Assert.Single((await run.AcceptAsync([Input(11, "one")], Token)).Decisions).Status);
        Assert.Equal("self", Assert.Single((await run.AcceptAsync([Input(12, "echo") with { ActorId = "self", FromMe = true }], Token)).Decisions).Status);
        Assert.Single(sender.Sends);
        var second = await run.AcceptAsync([Input(13, "two")], Token);
        Assert.Equal("engaged", Assert.Single(second.Outcomes).StateId);
        Assert.Equal(2, sender.Sends.Count);
        Assert.Equal(OutboundMode.Disabled, run.OutboundMode);
    }

    [Fact]
    public async Task Uncertain_send_closes_multi_turn_window_without_retry_or_second_execution()
    {
        var sender = new LiveRuntimeTests.SequencedTransport { SendError = new IOException("uncertain") };
        await using var run = Create(sender, maxInteractions: 5);
        var result = await run.AcceptAsync([Input(11, "one")], Token);
        Assert.Contains(result.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "indeterminate");
        Assert.Equal(OutboundMode.Disabled, run.OutboundMode);
        Assert.Single(run.History);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.AcceptAsync([Input(12, "two")], Token));
        Assert.Single(sender.Sends);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Invalid_window_budget_never_enables_sender(int limit)
    {
        var sender = new LiveRuntimeTests.SequencedTransport();
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(sender, maxInteractions: limit));
        Assert.Empty(sender.Sends);
    }

    [Fact]
    public async Task One_native_inbound_sends_once_commits_speech_and_closes_the_window()
    {
        var sender = new LiveRuntimeTests.SequencedTransport();
        var checks = 0;
        await using var run = Create(sender, _ => { checks++; return Task.CompletedTask; });
        var result = await run.AcceptAsync([Input(11, "native-1"), Input(11, "native-1")], Token);
        Assert.Equal(["admitted", "duplicate"], result.Decisions.Select(d => d.Status));
        Assert.True(Assert.Single(result.Outcomes).Success);
        Assert.Single(sender.Sends);
        Assert.Equal(1, checks);
        Assert.Equal(2, run.History.Count);
        Assert.Equal(SessionMessageDirection.Outbound, run.History[1].Direction);
        Assert.Equal(OutboundMode.Disabled, run.OutboundMode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.AcceptAsync([Input(12, "native-2")], Token));
        Assert.Single(sender.Sends);
    }

    [Fact]
    public async Task Two_new_peer_inputs_reject_the_whole_batch_without_models_or_send()
    {
        var sender = new LiveRuntimeTests.SequencedTransport();
        await using var run = Create(sender);
        var result = await run.AcceptAsync([Input(11, "one"), Input(12, "two")], Token);
        Assert.All(result.Decisions, d => Assert.Equal("rejected", d.Status));
        Assert.Empty(run.History);
        Assert.Empty(result.Outcomes);
        Assert.Empty(sender.Sends);
        Assert.Equal(OutboundMode.Disabled, run.OutboundMode);
    }

    [Fact]
    public async Task Self_and_snapshot_do_not_consume_the_one_inbound_window()
    {
        var sender = new LiveRuntimeTests.SequencedTransport();
        await using var run = Create(sender);
        Assert.Equal("backlog", Assert.Single((await run.AcceptAsync([Input(10, "old")], Token)).Decisions).Status);
        Assert.Equal("self", Assert.Single((await run.AcceptAsync([Input(11, "echo") with { ActorId = "self", FromMe = true }], Token)).Decisions).Status);
        Assert.Empty(run.Outcomes);
        Assert.Empty(sender.Sends);
        await run.AcceptAsync([Input(12, "new")], Token);
        Assert.Single(sender.Sends);
    }

    [Fact]
    public async Task Failed_final_preflight_never_reaches_transport_and_never_retries()
    {
        var sender = new LiveRuntimeTests.SequencedTransport();
        await using var run = Create(sender, _ => throw new InvalidDataException("revision_changed"));
        var result = await run.AcceptAsync([Input(11, "one")], Token);
        Assert.Empty(sender.Sends);
        Assert.DoesNotContain(run.History, h => h.Direction == SessionMessageDirection.Outbound);
        Assert.Equal(OutboundMode.Disabled, run.OutboundMode);
        Assert.Contains(result.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "indeterminate");
    }

    [Fact]
    public void Unproven_capabilities_cannot_create_live_composition()
    {
        var sender = new LiveRuntimeTests.SequencedTransport();
        Assert.Throws<InvalidOperationException>(() => Create(sender, capabilities: TransportSafetyCapabilities.Unproven));
        Assert.Empty(sender.Sends);
    }

    private static CanonicalShadowInbound Input(long sequence, string id) => new("epoch", sequence, Session, SessionEntryKind.Dm,
        Binding.ConversationNamespace, Peer, new(id, Session.ChannelId, Session.AccountId, Session.NativeConversationId,
            DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
            JsonSerializer.SerializeToElement(new { texto = "O trabalho demorou.", remetente = new { nome = "Helena" },
                canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null })));
    public void Dispose() => Directory.Delete(directory, true);
}
