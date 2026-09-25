using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Filomena.Core.Flows.Sessions;

namespace Filomena.Core.Shadow;

// Constructed by trusted host code, never by project JSON or a model output.
public sealed class ShadowSessionBinding
{
    public SessionKey Session { get; }
    public SessionEntryKind Kind { get; }
    public string ConversationNamespace { get; }
    public string OpaqueRef => Ref(JsonSerializer.Serialize(Session));
    private readonly HashSet<string> self;
    private readonly HashSet<string> peers;
    public bool AllowsNativeGroupParticipants { get; private set; }

    // Host admission policy, not project configuration. The authenticated native reader must
    // verify provider origin before constructing an inbound. Checkpoint lineage remains stable.
    public void EnableNativeGroupParticipants()
    {
        if (Kind != SessionEntryKind.Group || Session.ChannelId != "whatsapp")
            throw new InvalidOperationException("Native group participants require an exact WhatsApp group.");
        AllowsNativeGroupParticipants = true;
    }
    // Host-owned identities of the account itself; used to name the host's own speech, never handed to a model.
    public IReadOnlyCollection<string> SelfActorIds => self.Order(StringComparer.Ordinal).ToArray();
    // Preserve the existing DM authority hash for live checkpoint compatibility.
    internal string AuthorityRef => Kind == SessionEntryKind.Dm
        ? Ref(JsonSerializer.Serialize(new { Session, ConversationNamespace, self = self.Order(StringComparer.Ordinal), peers = peers.Order(StringComparer.Ordinal) }))
        : Ref(JsonSerializer.Serialize(new { Session, Kind, ConversationNamespace, self = self.Order(StringComparer.Ordinal), peers = peers.Order(StringComparer.Ordinal) }));

    public bool HasSameAuthority(ShadowSessionBinding other) => other is not null
        && Session == other.Session && Kind == other.Kind && ConversationNamespace == other.ConversationNamespace
        && AllowsNativeGroupParticipants == other.AllowsNativeGroupParticipants
        && self.SetEquals(other.self) && peers.SetEquals(other.peers);

    public ShadowSessionBinding(SessionKey session, string conversationNamespace, IEnumerable<string> selfActors, IEnumerable<string> peerActors,
        SessionEntryKind kind = SessionEntryKind.Dm)
    {
        if (new[] { session.RuntimeId, session.CharacterId, session.ChannelId, session.AccountId, session.NativeConversationId, conversationNamespace }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Incomplete trusted session binding.");
        if (kind is not (SessionEntryKind.Dm or SessionEntryKind.Group)
            || (kind == SessionEntryKind.Group && !session.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal)))
            throw new ArgumentException("Invalid trusted session kind.");
        Session = session; Kind = kind; ConversationNamespace = conversationNamespace;
        self = selfActors.ToHashSet(StringComparer.Ordinal); peers = peerActors.ToHashSet(StringComparer.Ordinal);
        if (self.Count == 0 || peers.Count == 0 || self.Concat(peers).Any(string.IsNullOrWhiteSpace) || self.Overlaps(peers))
            throw new ArgumentException("Disjoint trusted self and peer identities are required.");
    }

    public bool IsSelf(string actor) => self.Contains(actor);
    public bool IsPeer(string actor) => peers.Contains(actor) || (AllowsNativeGroupParticipants && !IsSelf(actor)
        && System.Text.RegularExpressions.Regex.IsMatch(actor, @"^whatsapp:\d+@s\.whatsapp\.net$"));
    public bool Accepts(CanonicalShadowInbound input) => input.Session == Session && input.Kind == Kind
        && input.ConversationNamespace == ConversationNamespace && (IsSelf(input.ActorId) || IsPeer(input.ActorId))
        && input.Message.ChannelId == Session.ChannelId && input.Message.AccountId == Session.AccountId
        && input.Message.NativeConversationId == Session.NativeConversationId && input.Message.EntryKind == Kind
        && input.Message.Direction == SessionMessageDirection.Inbound && !string.IsNullOrWhiteSpace(input.Message.ProviderMessageId);

    public static string Ref(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

// Future source must prove a contiguous, session-scoped stream and an atomic tail anchor.
// Current MCP/HTTP bridge does NOT satisfy this contract; no adapter synthesizes these fields.
public sealed record CanonicalShadowInbound(string StreamEpoch, long Sequence, SessionKey Session, SessionEntryKind Kind,
    string ConversationNamespace, string ActorId, SessionMessageEnvelope Message, bool FromMe = false, bool SkipNonText = false);
public sealed record ShadowInboundDecision(string Status, string Diagnostic, string? CanonicalRef = null);

/// <summary>Private durable inbox, not a normal trace. Contains input for manual crash recovery. No retry worker.</summary>
public sealed class ShadowInboundJournal : IDisposable
{
    private sealed record Entry(string Type, string Binding, string Revision, string Epoch, long Sequence,
        string? InputRef = null, CanonicalShadowInbound? Input = null, long? Watermark = null,
        string? Batch = null, string? Execution = null, string? RunId = null);
    private readonly object gate = new();
    private readonly FileStream file;
    private readonly StreamWriter writer;
    private readonly ShadowSessionBinding binding;
    private Entry header;
    private readonly Dictionary<string, string> states = new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> sequences = new();
    private readonly HashSet<string> currentRunInputs = new(StringComparer.Ordinal);
    private long cursor;
    private bool reconciliation;
    private bool disposed;
    public bool ReconciliationRequired { get { lock (gate) return reconciliation; } }
    public bool RequiresRuntimeRestore { get; }
    public string? ShadowRunId => header.RunId;

    // Host-only lifecycle action: requires a NEW preflight, pinned project and atomic source snapshot.
    // Previous work is preserved for audit/dedupe, never replayed into the new runtime.
    public static ShadowInboundJournal StartNonResumableRun(string path, ShadowSessionBinding binding,
        string revision, string epoch, long initialTail) => new(path, binding, revision, epoch, initialTail, true);

    public ShadowInboundJournal(string path, ShadowSessionBinding binding, string revision, string epoch, long initialTail)
        : this(path, binding, revision, epoch, initialTail, false) { }

    private ShadowInboundJournal(string path, ShadowSessionBinding binding, string revision, string epoch, long initialTail, bool newRun)
    {
        if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(epoch) || initialTail < 0) throw new ArgumentException("Invalid inbox anchor.");
        this.binding = binding;
        header = new("shadow-inbox:v1", binding.AuthorityRef, revision, epoch, initialTail);
        var requested = header;
        cursor = initialTail;
        file = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough);
        try
        {
            using var reader = new StreamReader(file, new UTF8Encoding(false, true), false, 4096, leaveOpen: true);
            var contents = reader.ReadToEnd();
            if (contents.Length > 0 && !contents.EndsWith('\n')) throw new InvalidDataException("Torn inbox record requires reconciliation.");
            var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                header = JsonSerializer.Deserialize<Entry>(lines[0]) ?? throw new InvalidDataException("Invalid inbox header.");
                if (header.Type != "shadow-inbox:v1" || header.Binding != binding.AuthorityRef || header.Sequence < 0)
                    throw new InvalidDataException("Inbox authority mismatch.");
                cursor = header.Sequence;
            }
            foreach (var line in lines.Skip(1)) Apply(JsonSerializer.Deserialize<Entry>(line) ?? throw new InvalidDataException("Invalid inbox record."));
            if (!newRun && (header with { RunId = null }) != requested) throw new InvalidDataException("Inbox binding or initial anchor mismatch.");
            reconciliation |= states.Values.Any(x => x is "observed" or "admitted");
            RequiresRuntimeRestore = !newRun && currentRunInputs.Any(id => states[id] is "observed" or "admitted" or "completed" or "execution_failed");
            file.Seek(0, SeekOrigin.End);
            writer = new(file, new UTF8Encoding(false), 4096, leaveOpen: true);
            if (lines.Length == 0) Append(header);
            if (newRun)
            {
                var boundary = requested with { Type = "shadow-run:v1", RunId = Guid.NewGuid().ToString("N") };
                Append(boundary); Apply(boundary);
            }
        }
        catch { file.Dispose(); throw; }
    }

    public ShadowInboundDecision Observe(CanonicalShadowInbound input)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!binding.Accepts(input)) return new("rejected", "untrusted_inbound_identity");
            var reference = InputRef(input.Message.ProviderMessageId);
            if (reconciliation) { WriteInput("quarantined", input, reference); return new("reconciliation_required", "inbox_unresolved", reference); }
            if (input.StreamEpoch != header.Epoch || input.Sequence < 0)
                return Quarantine(input, reference, "stream_epoch_unproven");
            if (input.Sequence <= header.Sequence) return new("backlog", "before_initial_tail", reference);
            if (input.Sequence <= cursor)
            {
                if (sequences.TryGetValue(input.Sequence, out var known) && known == reference) return new("duplicate", "already_observed", reference);
                return Quarantine(input, reference, "sequence_identity_conflict");
            }
            if (input.Sequence != cursor + 1) return Quarantine(input, reference, "stream_gap_or_out_of_order");
            var state = states.ContainsKey(reference) ? "duplicate" : input.FromMe || binding.IsSelf(input.ActorId) ? "self"
                : input.SkipNonText ? "ignored" : "observed";
            WriteInput(state, input, reference);
            return new(state, state == "observed" ? "durably_observed" : "no_execution", reference);
        }
    }

    public void Admitted(string providerId, long watermark) => Transition(providerId, "admitted", watermark, null, null);
    internal bool IsBoundTo(ShadowSessionBinding expected, string revision) => header.Binding == expected.AuthorityRef && header.Revision == revision;
    public static bool InspectExisting(string path, ShadowSessionBinding binding, string revision, bool forNewNonResumableRun = false)
    {
        if (!File.Exists(path)) return true; // Storage only; an initial source anchor is a separate mandatory gate.
        Entry anchor;
        using (var reader = new StreamReader(path, new UTF8Encoding(false, true)))
        {
            anchor = JsonSerializer.Deserialize<Entry>(reader.ReadLine() ?? throw new InvalidDataException("Inbox header missing.")) ?? throw new InvalidDataException("Inbox header invalid.");
            while (reader.ReadLine() is { } line)
                if (JsonSerializer.Deserialize<Entry>(line) is { Type: "shadow-run:v1" } run) anchor = run;
        }
        // Full validation still replays the durable state machine; this does not append a run boundary.
        using var journal = new ShadowInboundJournal(path, binding, forNewNonResumableRun ? anchor.Revision : revision, anchor.Epoch, anchor.Sequence);
        return forNewNonResumableRun || (!journal.ReconciliationRequired && !journal.RequiresRuntimeRestore);
    }
    internal void RequireReconciliation(string providerId)
    {
        lock (gate)
        {
            var entry = header with { Type = "quarantined", InputRef = InputRef(providerId) };
            Append(entry); Apply(entry);
        }
    }
    public void Completed(string providerId, string batch, string execution, bool success) =>
        Transition(providerId, success ? "completed" : "execution_failed", null, batch, execution);

    private void Transition(string providerId, string state, long? watermark, string? batch, string? execution)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var reference = InputRef(providerId);
            var entry = header with { Type = state, Sequence = cursor, InputRef = reference, Watermark = watermark, Batch = batch, Execution = execution };
            if (!states.TryGetValue(reference, out var previous) || (state == "admitted" ? previous != "observed" : previous != "admitted"))
                throw new InvalidOperationException("Invalid inbox transition.");
            Append(entry); Apply(entry);
        }
    }

    private string InputRef(string providerId) => ShadowSessionBinding.Ref(binding.OpaqueRef + "\n" + providerId);
    private ShadowInboundDecision Quarantine(CanonicalShadowInbound input, string reference, string diagnostic)
    {
        WriteInput("quarantined", input, reference);
        return new("reconciliation_required", diagnostic, reference);
    }
    private void WriteInput(string type, CanonicalShadowInbound input, string reference)
    {
        var entry = header with { Type = type, Sequence = input.Sequence, InputRef = reference, Input = input with { Message = input.Message with { Message = input.Message.Message.Clone() } } };
        Append(entry); Apply(entry);
    }
    private void Apply(Entry entry)
    {
        if (entry.Type == "shadow-run:v1")
        {
            if (entry.Binding != binding.AuthorityRef || string.IsNullOrWhiteSpace(entry.Revision)
                || string.IsNullOrWhiteSpace(entry.Epoch) || entry.Sequence < 0 || !Guid.TryParseExact(entry.RunId, "N", out _))
                throw new InvalidDataException("Invalid non-resumable shadow boundary.");
            foreach (var id in states.Where(pair => pair.Value is "observed" or "admitted").Select(pair => pair.Key).ToArray()) states[id] = "abandoned";
            header = entry with { Type = "shadow-inbox:v1" };
            cursor = entry.Sequence; sequences.Clear(); currentRunInputs.Clear(); reconciliation = false;
            return;
        }
        if (entry.Binding != header.Binding || entry.Revision != header.Revision || entry.Epoch != header.Epoch || entry.InputRef is null)
            throw new InvalidDataException("Invalid inbox binding.");
        if (entry.Type == "quarantined") { reconciliation = true; return; }
        if (entry.Type is "observed" or "self" or "duplicate" or "ignored")
        {
            if (entry.Sequence != cursor + 1 || entry.Input is null || !binding.Accepts(entry.Input) || InputRef(entry.Input.Message.ProviderMessageId) != entry.InputRef)
                throw new InvalidDataException("Invalid inbox order or identity.");
            cursor = entry.Sequence; sequences.Add(cursor, entry.InputRef);
            if (entry.Type != "duplicate") states.Add(entry.InputRef, entry.Type);
            if (entry.Type == "observed") currentRunInputs.Add(entry.InputRef);
        }
        else if (entry.Type is "admitted" or "completed" or "execution_failed")
        {
            if (!states.TryGetValue(entry.InputRef, out var previous) || (entry.Type == "admitted" ? previous != "observed" : previous != "admitted"))
                throw new InvalidDataException("Invalid durable inbox transition.");
            states[entry.InputRef] = entry.Type;
        }
        else throw new InvalidDataException("Unknown inbox state.");
    }
    private void Append(Entry entry)
    {
        try { writer.WriteLine(JsonSerializer.Serialize(entry)); writer.Flush(); file.Flush(flushToDisk: true); }
        catch { reconciliation = true; throw; }
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; writer.Dispose(); file.Dispose(); } }
}
