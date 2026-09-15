using System.Text;
using System.Text.Json;

namespace Filomena.Core.Outbound;

// No retry worker. Shadow namespace is permanently terminal. Future real records may require reconciliation.
internal sealed record OutboundJournalEntry(int Version, string Namespace, string Status, string AttemptRef,
    string ExecutionRef, string SessionRef, string[] InputRefs, string EnforcementRevision);

internal sealed class OutboundJournal : IDisposable
{
    private readonly FileStream file;
    private readonly List<OutboundJournalEntry> entries = [];
    private bool poisoned;
    internal bool RequiresReconciliation(string sessionRef) => poisoned || entries.Any(e => e.SessionRef == sessionRef && e.Namespace == "real:v1" && e.Status is "pending" or "indeterminate");

    internal OutboundJournal(string path)
    {
        // Exclusive writer for the journal lifetime; a second runtime cannot race the replay index.
        file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough);
        try
        {
            using var reader = new StreamReader(file, new UTF8Encoding(false, true), false, 4096, leaveOpen: true);
            var text = reader.ReadToEnd();
            if (text.Length > 0 && !text.EndsWith('\n')) throw new InvalidDataException("outbound_journal_incomplete");
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var entry = JsonSerializer.Deserialize<OutboundJournalEntry>(line) ?? throw new InvalidDataException("outbound_journal_invalid");
                if (entry.Version != 1 || entry.InputRefs is null || string.IsNullOrWhiteSpace(entry.SessionRef) ||
                    !(entry.Namespace == "shadow:v1" && entry.Status == "shadowed" || entry.Namespace == "real:v1" && entry.Status is "pending" or "indeterminate" or "sent" or "not_sent" or "abandoned"))
                    throw new InvalidDataException("outbound_journal_invalid");
                entries.Add(entry);
            }
            file.Position = file.Length;
        }
        catch { file.Dispose(); throw; }
    }

    internal ShadowSendResult RecordShadow(string execution, string session, string[] inputs, string revision)
    {
        if (poisoned) return new("failed", "journal_unavailable");
        if (entries.Any(e => e.SessionRef == session && e.Namespace == "real:v1" && e.Status is "pending" or "indeterminate"))
            return new("blocked", "reconciliation_required");
        var executionRef = ShadowOutboundHost.Hash(session + "\n" + execution);
        if (entries.Any(e => e.ExecutionRef == executionRef)) return new("blocked", "execution_replay");
        if (entries.Any(e => e.SessionRef == session && e.InputRefs.Intersect(inputs, StringComparer.Ordinal).Any()))
            return new("blocked", "replayed_input");
        var entry = new OutboundJournalEntry(1, "shadow:v1", "shadowed", Guid.NewGuid().ToString("N"), executionRef, session, inputs, revision);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
        try { file.Write(bytes); file.Flush(flushToDisk: true); entries.Add(entry); }
        catch { poisoned = true; throw; }
        return new("shadowed", "would_send", entry.AttemptRef);
    }
    public void Dispose() => file.Dispose();
}
