using System.Text;
using System.Text.Json;

namespace Filomena.Core.Outbound;

// No retry worker. Shadow namespace is permanently terminal. Real attempts are append-only state machines:
// pending → sent | not_sent | indeterminate; sent → history_committed; indeterminate → sent | not_sent | abandoned
// only through explicit reconciliation with external evidence. Anything else unresolved requires reconciliation.
// A pending record keeps the authorized text and speaker so a durable receipt can be completed into history
// idempotently after a crash; the journal directory is ACL-protected and never a memory source.
internal sealed record OutboundJournalEntry(int Version, string Namespace, string Status, string AttemptRef,
    string ExecutionRef, string SessionRef, string[] InputRefs, string EnforcementRevision)
{
    public string? DiagnosticCode { get; init; }
    public string? ProviderRef { get; init; }
    public string? ProviderMessageId { get; init; }
    public string? Text { get; init; }
    public string? SpeakerName { get; init; }
    public string? SpeakerStableId { get; init; }
}

internal sealed record RecoverableSend(string AttemptRef, string ProviderMessageId, string Text, string SpeakerName, string SpeakerStableId);

internal sealed class OutboundJournal : IDisposable
{
    private const string ShadowNamespace = "shadow:v1";
    private const string RealNamespace = "real:v1";
    private static readonly string[] RealStatuses = ["pending", "sent", "not_sent", "indeterminate", "abandoned", "history_committed"];
    private static readonly Dictionary<string, string[]> Transitions = new(StringComparer.Ordinal)
    {
        ["pending"] = ["sent", "not_sent", "indeterminate"],
        ["sent"] = ["history_committed"],
        ["indeterminate"] = ["sent", "not_sent", "abandoned"],
    };
    private readonly FileStream file;
    private readonly List<OutboundJournalEntry> entries = [];
    private bool poisoned;

    internal bool RequiresReconciliation(string sessionRef) => poisoned || HasUnresolvedAttempt(sessionRef);

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
                if (entry.Version != 1 || entry.InputRefs is null || string.IsNullOrWhiteSpace(entry.SessionRef) || string.IsNullOrWhiteSpace(entry.AttemptRef) ||
                    !(entry.Namespace == ShadowNamespace && entry.Status == "shadowed" || entry.Namespace == RealNamespace && RealStatuses.Contains(entry.Status)))
                    throw new InvalidDataException("outbound_journal_invalid");
                entries.Add(entry);
            }
            file.Position = file.Length;
            // A reservation that never resolved means the process died with a provider call possibly outstanding.
            // It may have been delivered; it is never retried and never resolved automatically.
            foreach (var pending in Reservations().Where(e => Latest(e.AttemptRef) == "pending").ToArray())
                Write(pending with { Status = "indeterminate", DiagnosticCode = "restart_with_pending", ProviderRef = null, Text = null, SpeakerName = null, SpeakerStableId = null });
        }
        catch { file.Dispose(); throw; }
    }

    private IEnumerable<OutboundJournalEntry> Reservations() => entries.Where(e => e.Namespace == RealNamespace && e.Status == "pending");
    private string Latest(string attemptRef) => entries.Last(e => e.AttemptRef == attemptRef).Status;
    private IEnumerable<string> Attempts(string sessionRef) =>
        entries.Where(e => e.Namespace == RealNamespace && e.SessionRef == sessionRef).Select(e => e.AttemptRef).Distinct();
    // Any real attempt whose latest durable state is not terminal, including records written by an earlier process.
    private bool HasUnresolvedAttempt(string sessionRef) => Attempts(sessionRef).Any(attempt => Latest(attempt) is "pending" or "indeterminate" or "sent");

    private ShadowSendResult? Blocked(string session, string executionRef, string[] inputs)
    {
        if (HasUnresolvedAttempt(session)) return new("blocked", "reconciliation_required");
        if (entries.Any(e => e.ExecutionRef == executionRef)) return new("blocked", "execution_replay");
        if (entries.Any(e => e.SessionRef == session && e.InputRefs.Intersect(inputs, StringComparer.Ordinal).Any()))
            return new("blocked", "replayed_input");
        return null;
    }

    internal ShadowSendResult RecordShadow(string execution, string session, string[] inputs, string revision)
    {
        if (poisoned) return new("failed", "journal_unavailable");
        var executionRef = ShadowOutboundHost.Hash(session + "\n" + execution);
        if (Blocked(session, executionRef, inputs) is { } blocked) return blocked;
        var entry = new OutboundJournalEntry(1, ShadowNamespace, "shadowed", Guid.NewGuid().ToString("N"), executionRef, session, inputs, revision);
        Write(entry);
        return new("shadowed", "would_send", entry.AttemptRef);
    }

    // Durable reservation committed BEFORE any external effect. Shadow and real records share one replay index,
    // so a historical shadow attempt can never be promoted into a real send.
    internal ShadowSendResult Reserve(string execution, string session, string[] inputs, string revision, string text, OutboundSpeaker speaker)
    {
        if (poisoned) return new("failed", "journal_unavailable");
        var executionRef = ShadowOutboundHost.Hash(session + "\n" + execution);
        if (Blocked(session, executionRef, inputs) is { } blocked) return blocked;
        var entry = new OutboundJournalEntry(1, RealNamespace, "pending", Guid.NewGuid().ToString("N"), executionRef, session, inputs, revision)
        { Text = text, SpeakerName = speaker.Name, SpeakerStableId = speaker.StableId };
        Write(entry);
        return new("pending", "reserved", entry.AttemptRef);
    }

    // Append-only resolution following the transition table. A failed write poisons the journal: the host must
    // treat the outcome as unknown. An invalid transition is refused, never rewritten.
    internal bool Resolve(string attemptRef, string status, string diagnosticCode, string? providerRef, string? providerMessageId = null)
    {
        if (poisoned) return false;
        var reservation = Reservations().FirstOrDefault(e => e.AttemptRef == attemptRef);
        if (reservation is null || !Transitions.TryGetValue(Latest(attemptRef), out var allowed) || !allowed.Contains(status)) return false;
        try
        {
            Write(reservation with
            {
                Status = status, DiagnosticCode = diagnosticCode, ProviderRef = providerRef, ProviderMessageId = providerMessageId,
                Text = null, SpeakerName = null, SpeakerStableId = null,
            });
        }
        catch { return false; }
        return true;
    }

    internal bool IsIndeterminate(string sessionRef, string attemptRef) =>
        Attempts(sessionRef).Contains(attemptRef) && Latest(attemptRef) == "indeterminate";

    // Provider accepted, speech not yet in local history: everything needed to complete it once, without resending.
    internal IReadOnlyList<RecoverableSend> RecoverableSends(string sessionRef) =>
        Attempts(sessionRef).Where(attempt => Latest(attempt) == "sent").Select(attempt =>
        {
            var reservation = entries.First(e => e.AttemptRef == attempt && e.Status == "pending");
            var sent = entries.Last(e => e.AttemptRef == attempt && e.Status == "sent");
            return new RecoverableSend(attempt, sent.ProviderMessageId ?? "", reservation.Text ?? "", reservation.SpeakerName ?? "", reservation.SpeakerStableId ?? "");
        }).ToArray();

    private void Write(OutboundJournalEntry entry)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, SerializerOptions) + "\n");
        try { file.Write(bytes); file.Flush(flushToDisk: true); entries.Add(entry); }
        catch { poisoned = true; throw; }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public void Dispose() => file.Dispose();
}
