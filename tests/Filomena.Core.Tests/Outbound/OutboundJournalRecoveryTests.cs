using System.Text.Json;
using Filomena.Core.Outbound;
using Xunit;

namespace Filomena.Core.Tests.Outbound;

public sealed class OutboundJournalRecoveryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "journal-recovery-" + Guid.NewGuid().ToString("N"));
    private string JournalPath => Path.Combine(directory, "outbound.jsonl");
    private static OutboundJournalEntry Pending => new(1, "real:v1", "pending", "attempt", "execution", "session", ["input"], "enforcement")
    { Text = "candidate", SpeakerName = "Filomena", SpeakerStableId = "self" };
    public OutboundJournalRecoveryTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("orphan_terminal")]
    [InlineData("pending_to_committed")]
    [InlineData("terminal_to_pending")]
    [InlineData("duplicate_reservation")]
    [InlineData("session_changed")]
    [InlineData("execution_changed")]
    [InlineData("inputs_changed")]
    [InlineData("revision_changed")]
    [InlineData("namespace_changed")]
    [InlineData("sent_without_receipt")]
    public void RestartRejectsInvalidDurableHistoryWithoutRewritingIt(string corruption)
    {
        var pending = Pending;
        var sent = pending with { Status = "sent", ProviderMessageId = "provider" };
        OutboundJournalEntry[] rows = corruption switch
        {
            "orphan_terminal" => [pending with { Status = "history_committed" }],
            "pending_to_committed" => [pending, pending with { Status = "history_committed" }],
            "terminal_to_pending" => [pending, pending with { Status = "not_sent" }, pending],
            "duplicate_reservation" => [pending, pending],
            "session_changed" => [pending, sent with { SessionRef = "other" }],
            "execution_changed" => [pending, sent with { ExecutionRef = "other" }],
            "inputs_changed" => [pending, sent with { InputRefs = ["other"] }],
            "revision_changed" => [pending, sent with { EnforcementRevision = "other" }],
            "namespace_changed" => [pending, pending with { Namespace = "shadow:v1", Status = "shadowed" }],
            "sent_without_receipt" => [pending, sent with { ProviderMessageId = null }],
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
        File.WriteAllText(JournalPath, string.Concat(rows.Select(row => JsonSerializer.Serialize(row) + "\n")));
        var before = File.ReadAllBytes(JournalPath);
        Assert.Throws<InvalidDataException>(() => { using var journal = new OutboundJournal(JournalPath); });
        Assert.Equal(before, File.ReadAllBytes(JournalPath));
    }

    [Fact]
    public void ValidReceiptRemainsRecoverableAfterRestart()
    {
        using (var journal = new OutboundJournal(JournalPath))
        {
            var attempt = journal.Reserve("execution", "session", ["input"], "enforcement", "candidate", new("Filomena", "self"));
            Assert.True(journal.Resolve(attempt.JournalRef!, "sent", "provider_accepted", "provider-ref", "provider"));
        }
        using var reopened = new OutboundJournal(JournalPath);
        Assert.True(reopened.RequiresReconciliation("session"));
        Assert.Equal("provider", Assert.Single(reopened.RecoverableSends("session")).ProviderMessageId);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
