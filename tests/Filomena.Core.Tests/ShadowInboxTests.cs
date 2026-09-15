using System.Text.Json;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Shadow;
using Xunit;

namespace Filomena.Core.Tests;

public sealed class ShadowInboxTests : IDisposable
{
    [Fact]
    public void New_non_resumable_shadow_run_keeps_seen_ids_but_never_resumes_old_work()
    {
        using (var previous = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10))
        {
            Assert.Equal("observed", previous.Observe(Event()).Status);
            previous.Admitted("provider-1", 1);
        }
        using var next = ShadowInboundJournal.StartNonResumableRun(PathName, Binding(), "new-revision", "new-epoch", 20);
        Assert.False(next.RequiresRuntimeRestore);
        Assert.False(next.ReconciliationRequired);
        Assert.NotNull(next.ShadowRunId);
        Assert.Equal("backlog", next.Observe(Event(20) with { StreamEpoch = "new-epoch" }).Status);
        Assert.Equal("duplicate", next.Observe(Event(21) with { StreamEpoch = "new-epoch" }).Status);
        Assert.Equal("observed", next.Observe(Event(22, "new-message") with { StreamEpoch = "new-epoch" }).Status);
        Assert.Contains("shadow-run:v1", ReadJournal());
    }

    [Fact]
    public void Non_resumable_run_cannot_rebind_a_durable_inbox_to_another_authority()
    {
        using (new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10)) { }
        Assert.Throws<InvalidDataException>(() => ShadowInboundJournal.StartNonResumableRun(PathName,
            new(Session, "foreign", ["self"], ["peer"]), "revision", "new", 0));
    }

    [Fact]
    public void Preflight_can_validate_old_storage_for_a_fresh_run_without_resuming_or_writing_it()
    {
        using (var previous = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10)) previous.Observe(Event());
        var before = ReadJournal();
        Assert.True(ShadowInboundJournal.InspectExisting(PathName, Binding(), "new-revision", forNewNonResumableRun: true));
        Assert.Equal(before, ReadJournal());
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), "shadow-inbox-" + Guid.NewGuid());
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "account", "5511000000000@s.whatsapp.net");
    private static ShadowSessionBinding Binding() => new(Session, "namespace", ["self"], ["peer"]);
    private string PathName { get { Directory.CreateDirectory(directory); return Path.Combine(directory, "inbound.jsonl"); } }
    private static CanonicalShadowInbound Event(long sequence = 11, string id = "provider-1", string actor = "peer") =>
        new("epoch", sequence, Session, SessionEntryKind.Dm, "namespace", actor,
            new(id, Session.ChannelId, Session.AccountId, Session.NativeConversationId, DateTimeOffset.UnixEpoch,
                SessionMessageDirection.Inbound, SessionEntryKind.Dm, JsonSerializer.SerializeToElement(new { text = "hello" })));

    [Fact]
    public void Backlog_is_not_admitted_but_new_event_is_durable_before_runtime()
    {
        using var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.Equal("backlog", inbox.Observe(Event(10)).Status);
        Assert.Equal("observed", inbox.Observe(Event()).Status);
        Assert.Contains("observed", ReadJournal());
        Assert.Equal("duplicate", inbox.Observe(Event()).Status);
    }

    [Fact]
    public void Completed_message_is_duplicate_after_restart()
    {
        using (var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10))
        {
            inbox.Observe(Event());
            inbox.Admitted("provider-1", 1);
            inbox.Completed("provider-1", "batch", "execution", true);
        }
        using var reopened = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.False(reopened.ReconciliationRequired);
        Assert.True(reopened.RequiresRuntimeRestore);
        Assert.Equal("duplicate", reopened.Observe(Event()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Crash_before_or_after_admission_requires_reconciliation(bool admitted)
    {
        using (var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10))
        {
            inbox.Observe(Event());
            if (admitted) inbox.Admitted("provider-1", 1);
        }
        using var reopened = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.True(reopened.ReconciliationRequired);
        Assert.Equal("reconciliation_required", reopened.Observe(Event(12, "next")).Status);
    }

    [Fact]
    public void Self_is_durably_ignored_and_does_not_create_a_cursor_gap()
    {
        using var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.Equal("self", inbox.Observe(Event(actor: "self")).Status);
        Assert.Equal("observed", inbox.Observe(Event(12, "next")).Status);
    }

    [Fact]
    public void Native_from_me_suppresses_execution_without_display_name_inference()
    {
        using var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.Equal("self", inbox.Observe(Event() with { FromMe = true }).Status);
        Assert.Equal("observed", inbox.Observe(Event(12, "next")).Status);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("group")]
    [InlineData("channel")]
    [InlineData("namespace")]
    [InlineData("sender")]
    [InlineData("id")]
    [InlineData("envelope")]
    public void Untrusted_identity_is_rejected(string mutation)
    {
        using var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        var input = Event();
        input = mutation switch
        {
            "session" => input with { Session = Session with { NativeConversationId = "other" } },
            "group" => input with { Kind = SessionEntryKind.Group },
            "channel" => input with { Session = Session with { ChannelId = "other" } },
            "namespace" => input with { ConversationNamespace = "foreign" },
            "sender" => input with { ActorId = "" },
            "id" => input with { Message = input.Message with { ProviderMessageId = "" } },
            _ => input with { Message = input.Message with { NativeConversationId = "other" } }
        };
        Assert.Equal("rejected", inbox.Observe(input).Status);
        Assert.Equal("observed", inbox.Observe(Event()).Status);
    }

    [Fact]
    public void Gap_is_preserved_and_blocks_handoff_instead_of_inventing_order()
    {
        using var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.Equal("reconciliation_required", inbox.Observe(Event(12)).Status);
        Assert.True(inbox.ReconciliationRequired);
        Assert.Contains("quarantined", ReadJournal());
    }

    [Fact]
    public void Cannot_rebind_existing_journal_or_reset_its_watermark()
    {
        using (new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10)) { }
        Assert.Throws<InvalidDataException>(() => new ShadowInboundJournal(PathName, Binding(), "changed", "epoch", 10));
        Assert.Throws<InvalidDataException>(() => new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 20));
        Assert.Throws<InvalidDataException>(() => new ShadowInboundJournal(PathName, new(Session, "foreign", ["self"], ["peer"]), "revision", "epoch", 10));
        Assert.Throws<InvalidDataException>(() => new ShadowInboundJournal(PathName, new(Session, "namespace", ["different-self"], ["peer"]), "revision", "epoch", 10));
    }

    [Fact]
    public void Duplicate_with_new_stream_sequence_advances_cursor_without_second_admission()
    {
        using var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.Equal("observed", inbox.Observe(Event()).Status);
        Assert.Equal("duplicate", inbox.Observe(Event(12)).Status);
        Assert.Equal("observed", inbox.Observe(Event(13, "next")).Status);
    }

    [Fact]
    public void Epoch_change_and_later_inputs_are_preserved_without_execution()
    {
        using (var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10))
        {
            Assert.Equal("reconciliation_required", inbox.Observe(Event() with { StreamEpoch = "new-epoch" }).Status);
            Assert.Equal("reconciliation_required", inbox.Observe(Event(12, "next")).Status);
            Assert.Contains("next", ReadJournal());
        }
        using var reopened = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.True(reopened.ReconciliationRequired);
    }

    [Fact]
    public void Torn_record_is_not_repaired_or_replaced_by_empty_registry()
    {
        using (new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10)) { }
        File.AppendAllText(PathName, "{broken");
        Assert.Throws<InvalidDataException>(() => new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10));
        Assert.EndsWith("{broken", File.ReadAllText(PathName));
    }

    [Fact]
    public void Exclusive_writer_prevents_a_second_bootstrap_from_racing_dedupe()
    {
        using var inbox = new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10);
        Assert.Throws<IOException>(() => new ShadowInboundJournal(PathName, Binding(), "revision", "epoch", 10));
    }

    private string ReadJournal()
    {
        using var stream = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
