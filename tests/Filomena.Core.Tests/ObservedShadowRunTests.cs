using System.Text.Json;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Core.Tests.Outbound;
using Xunit;

namespace Filomena.Core.Tests;

public sealed class ObservedShadowRunTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("observed-shadow-").FullName;
    private static readonly SessionKey Session = new("observed", "agent", "whatsapp", "default", "5511944558218@s.whatsapp.net");
    private const string Peer = "whatsapp:5511944558218@s.whatsapp.net";
    private const string Self = "self";
    private static ShadowSessionBinding Binding => new(Session, FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId), [Self], [Peer]);
    private static OutboundBindings Roles => new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged");

    private ObservedShadowRun Create(string mode = "boundary", long tail = 0) => new(new(ShadowRuntimeTests.Project(false)), Binding,
        directory, Roles, new ShadowRuntimeTests.Completion(mode), new ShadowRuntimeTests.Reader(mode), "native-epoch-A", tail);

    [Fact]
    public async Task Admits_one_native_message_and_deduplicates_the_repeated_native_message()
    {
        await using var run = Create();
        var result = await run.AcceptAsync([Input(run, 1, "native-1"), Input(run, 1, "native-1")], Token);

        Assert.Equal(["admitted", "duplicate"], result.Decisions.Select(d => d.Status));
        Assert.Single(result.Outcomes);
        Assert.Single(run.History);
        Assert.Equal("native-1", Assert.Single(run.History).ProviderMessageId);
    }

    [Fact]
    public async Task Self_observation_is_journaled_without_execution()
    {
        await using var run = Create();
        var result = await run.AcceptAsync([Input(run, 1, "self-1", Self, true)], Token);

        Assert.Equal("self", Assert.Single(result.Decisions).Status);
        Assert.Empty(result.Outcomes);
        Assert.Empty(run.History);
    }

    [Fact]
    public async Task Snapshot_at_the_initial_tail_is_an_anchor_only_and_has_no_history()
    {
        await using var run = Create(tail: 10);
        var result = await run.AcceptAsync([Input(run, 10, "snapshot-10")], Token);

        Assert.Equal("backlog", Assert.Single(result.Decisions).Status);
        Assert.Empty(run.Outcomes);
        Assert.Empty(run.History);
        Assert.Equal("admitted", Assert.Single((await run.AcceptAsync([Input(run, 11, "after-snapshot")], Token)).Decisions).Status);
    }

    [Fact]
    public async Task Wrong_binding_rejects_the_whole_batch_before_any_execution()
    {
        await using var run = Create();
        var valid = Input(run, 1, "valid-1");
        var wrong = valid with
        {
            Session = valid.Session with { NativeConversationId = "other-conversation" },
            Message = valid.Message with { NativeConversationId = "other-conversation" }
        };

        var result = await run.AcceptAsync([valid, wrong], Token);

        Assert.All(result.Decisions, decision => Assert.Equal("rejected", decision.Status));
        Assert.Empty(run.Outcomes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.AcceptAsync([Input(run, 2, "after-rejection")], Token));
    }

    [Theory]
    [InlineData("epoch")]
    [InlineData("gap")]
    public async Task Epoch_or_gap_is_quarantined_in_the_journal_and_stops_the_run(string failure)
    {
        await using var run = Create();
        var input = Input(run, failure == "gap" ? 2 : 1, failure + "-1");
        if (failure == "epoch") input = input with { StreamEpoch = "foreign-epoch" };

        var result = await run.AcceptAsync([input], Token);

        Assert.Equal("reconciliation_required", Assert.Single(result.Decisions).Status);
        Assert.Empty(run.Outcomes);
        using var file = new FileStream(Path.Combine(directory, "observation-shadow-inbound.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var text = new StreamReader(file);
        Assert.Contains("quarantined", text.ReadToEnd());
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.AcceptAsync([Input(run, 1, "after-failure")], Token));
    }

    [Fact]
    public async Task Conflicting_sequence_within_batch_cannot_admit_even_the_first_input()
    {
        await using var run = Create();
        var result = await run.AcceptAsync([Input(run, 1, "first"), Input(run, 1, "conflict")], Token);
        Assert.Empty(result.Outcomes);
        Assert.Empty(run.History);
        Assert.DoesNotContain(result.Decisions, d => d.Status == "admitted");
        Assert.Equal(OutboundMode.Disabled, run.OutboundMode);
    }

    [Fact]
    public async Task Restart_starts_a_new_run_but_keeps_durable_native_dedupe()
    {
        string firstRunId;
        await using (var first = Create())
        {
            firstRunId = first.RunId;
            var result = await first.AcceptAsync([Input(first, 1, "native-1")], Token);
            Assert.Equal("admitted", Assert.Single(result.Decisions).Status);
        }

        await using var second = Create();
        Assert.NotEqual(firstRunId, second.RunId);
        Assert.Empty(second.History);
        var duplicate = await second.AcceptAsync([Input(second, 1, "native-1")], Token);
        Assert.Equal("duplicate", Assert.Single(duplicate.Decisions).Status);
        Assert.Empty(second.Outcomes);

        var fresh = await second.AcceptAsync([Input(second, 2, "native-2")], Token);
        Assert.Equal("admitted", Assert.Single(fresh.Decisions).Status);
        Assert.Single(second.Outcomes);
    }

    [Fact]
    public async Task Runtime_shadow_does_not_record_spoken_history_or_apply_transitions()
    {
        await using var run = Create();
        var result = await run.AcceptAsync([Input(run, 1, "native-1")], Token);

        Assert.Single(result.Outcomes);
        Assert.False(result.Outcomes[0].TransitionApplied);
        Assert.All(run.History, entry => Assert.Equal(SessionMessageDirection.Inbound, entry.Direction));
        Assert.Contains(result.Trace, trace => trace.EventName == "node-outbound" && trace.MemoryMetadata!["result"] == "shadowed");
        Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
    }

    [Fact]
    public async Task Runtime_outage_stops_future_admission_without_retrying()
    {
        await using var run = Create("outage");
        var result = await run.AcceptAsync([Input(run, 1, "native-1")], Token);

        Assert.False(Assert.Single(result.Outcomes).Success);
        Assert.DoesNotContain(result.Trace, trace => trace.EventName == "node-outbound");
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.AcceptAsync([Input(run, 2, "native-2")], Token));
    }

    private CanonicalShadowInbound Input(ObservedShadowRun run, long sequence, string providerId,
        string actor = Peer, bool fromMe = false) =>
        new("native-epoch-A", sequence, Session, SessionEntryKind.Dm, Binding.ConversationNamespace, actor,
            new(providerId, Session.ChannelId, Session.AccountId, Session.NativeConversationId, DateTimeOffset.UtcNow,
                SessionMessageDirection.Inbound, SessionEntryKind.Dm,
                JsonSerializer.SerializeToElement(new
                {
                    texto = "O trabalho demorou.",
                    remetente = new { nome = "Helena" },
                    canal = new { nome = "DM", nativeId = Session.NativeConversationId },
                    reply = (object?)null
                })), fromMe);

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
