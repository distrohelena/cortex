using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;

namespace Filomena.Core.Outbound;

public enum OutboundMode { Disabled, Shadow, Enabled }

public sealed record TransportSafetyCapabilities(bool StableOrigin, bool StableInboundIdentity,
    bool RouteBinding, bool SelfSuppression, bool SendReceipt, bool EchoCorrelation, bool SinglePhysicalMessage)
{
    public static TransportSafetyCapabilities Unproven { get; } = new(false, false, false, false, false, false, false);
    public bool Complete => StableOrigin && StableInboundIdentity && RouteBinding && SelfSuppression && SendReceipt && EchoCorrelation && SinglePhysicalMessage;
}

// Trusted host configuration, never deserialized from a graph. IDs identify reviewed roles, not titles.
public sealed record OutboundBindings(IReadOnlyList<string> RecallNodes, IReadOnlyList<string> CompilerNodes,
    IReadOnlyDictionary<string, string> CandidatePorts, IReadOnlyList<string> EnforcementNodes, string? WouldTransitionTo);

// What the transport reports about a route before dispatch. Missing fields are missing, never inferred.
public sealed record TransportRouteDescription(string? Channel, string? AccountId, string? Recipient);

/// <summary>Pre-send route resolution plus one physical send. Implementations never retry, split or reroute.</summary>
public interface IOutboundTransport : IConversationSender
{
    Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken);
}

// How the host's own confirmed speech is identified in local history. Host-owned, never chosen by the project.
public sealed record OutboundSpeaker(string Name, string StableId);

// Administrative, host-owned binding of exactly one allowlisted session to one transport route and its expected
// exact destination. The project/model cannot construct, widen or replace it.
public sealed record LiveRouteBinding(SessionKey Session, string TransportRoute, string Channel, string AccountId,
    string Recipient, OutboundSpeaker Speaker, int MaxSinglePartCharacters);

public sealed record ShadowSendResult(string Status, string DiagnosticCode, string? JournalRef = null, string? WouldTransitionTo = null,
    string? ProviderMessageId = null, OutboundSpeaker? Speaker = null);

// A durable provider receipt whose speech has not yet been committed to local history (crash or restart in between).
public sealed record PendingSpeechCommit(string AttemptRef, string ProviderMessageId, string Text, OutboundSpeaker Speaker);

// Explicit, audited operator decision about an indeterminate attempt, based on external evidence. Never automatic.
public enum ReconciliationVerdict { Sent, NotSent, Abandoned }

/// <summary>Host authority. Deliberately has NO transport dependency at construction; a live route is bound administratively.</summary>
public sealed class ShadowOutboundHost : IDisposable
{
    // Native WhatsApp adapter chunks text at min(configured limit, 4096) with a 4000 fallback; stay at or below that floor.
    public const int NativeSinglePartCeiling = 4000;
    private readonly object gate = new();
    private readonly OutboundJournal journal;
    private readonly string projectHash;
    private readonly OutboundBindings bindings;
    private readonly HashSet<SessionKey> sessions;
    private readonly TransportSafetyCapabilities capabilities;
    private OutboundMode mode = OutboundMode.Disabled;
    private LiveRouteBinding? live;
    private IOutboundTransport? transport;
    private bool disposed;

    public ShadowOutboundHost(string journalPath, JsonElement reviewedProject, OutboundBindings bindings,
        IEnumerable<SessionKey> allowedSessions, TransportSafetyCapabilities capabilities)
    {
        projectHash = Fingerprint(reviewedProject);
        this.bindings = new(bindings.RecallNodes.ToArray(), bindings.CompilerNodes.ToArray(),
            new Dictionary<string, string>(bindings.CandidatePorts), bindings.EnforcementNodes.ToArray(), bindings.WouldTransitionTo);
        sessions = allowedSessions.ToHashSet();
        this.capabilities = capabilities;
        journal = new(journalPath);
    }

    public OutboundMode Mode { get { lock (gate) return mode; } }
    public bool RequiresReconciliation(SessionKey session) { lock (gate) return disposed || journal.RequiresReconciliation(Hash(JsonSerializer.Serialize(session))); }
    // Mode is the operational request; Enabled is effective only with complete capabilities and a bound live route.
    public OutboundMode EffectiveMode
    {
        get
        {
            lock (gate)
            {
                if (disposed || mode == OutboundMode.Disabled) return OutboundMode.Disabled;
                if (mode == OutboundMode.Shadow) return OutboundMode.Shadow;
                return capabilities.Complete && live is not null && transport is not null ? OutboundMode.Enabled : OutboundMode.Disabled;
            }
        }
    }

    // Explicit operational rearm. Restart never restores a mode from project or journal.
    public void Rearm(OutboundMode requested)
    {
        if (!Enum.IsDefined(requested)) throw new ArgumentOutOfRangeException(nameof(requested));
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); mode = requested; }
    }

    // Administrative only. Binding does not enable anything; Rearm(Enabled) is a separate explicit act.
    public void BindLiveRoute(LiveRouteBinding binding, IOutboundTransport transport)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(transport);
        if (binding.Speaker is null || new[] { binding.TransportRoute, binding.Channel, binding.AccountId, binding.Recipient, binding.Speaker.Name, binding.Speaker.StableId }
                .Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Live route binding requires an exact transport route, channel, account, recipient and speaker.", nameof(binding));
        if (binding.MaxSinglePartCharacters is <= 0 or > NativeSinglePartCeiling)
            throw new ArgumentOutOfRangeException(nameof(binding), "MaxSinglePartCharacters must stay within the native single-part ceiling.");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!sessions.Contains(binding.Session)) throw new InvalidOperationException("A live route must bind an allowlisted exact session.");
            live = binding;
            this.transport = transport;
        }
    }

    internal OutboundExecution Begin(JsonElement project, string executionId, SessionKey session, SessionEntryKind kind, IEnumerable<string> inboundIds) =>
        new(this, bindings, Fingerprint(project) == projectHash, executionId, session, kind, inboundIds.ToArray());

    internal async Task<ShadowSendResult> GuardAsync(OutboundExecution execution, bool provenance, string? enforcementRevision, string text, CancellationToken cancellationToken)
    {
        LiveRouteBinding binding;
        IOutboundTransport sender;
        string attempt;
        // Authorization is sampled at the sink, not at generation. Rearm is serialized with this decision.
        lock (gate)
        {
            execution.RecordGuardAudit(mode, sessions.Contains(execution.Session), provenance);
            if (disposed || mode == OutboundMode.Disabled) return new("disabled", "outbound_disabled");
            if (!execution.ProjectAuthorized) return new("blocked", "project_not_authorized");
            if (execution.Kind != SessionEntryKind.Dm) return new("blocked", "dm_only");
            if (!sessions.Contains(execution.Session)) return new("blocked", "session_not_allowlisted");
            if (!provenance) return new("blocked", "invalid_provenance");
            if (execution.InboundIds.Count == 0 || execution.InboundIds.Any(string.IsNullOrWhiteSpace)) return new("blocked", "invalid_origin_identity");
            var inputRefs = execution.InboundIds.Select(id => Hash(execution.SessionRef + "\n" + id)).ToArray();
            if (mode == OutboundMode.Shadow)
            {
                try
                {
                    var result = journal.RecordShadow(execution.ExecutionId, execution.SessionRef, inputRefs, enforcementRevision!);
                    return result with { WouldTransitionTo = result.Status == "shadowed" ? bindings.WouldTransitionTo : null };
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    return new("failed", "journal_unavailable");
                }
            }
            if (!capabilities.Complete) return new("blocked", "transport_capabilities_incomplete");
            if (live is null || transport is null || live.Session != execution.Session) return new("blocked", "route_not_bound");
            if (text.Length > live.MaxSinglePartCharacters) return new("blocked", "payload_exceeds_single_part");
            ShadowSendResult reservation;
            try { reservation = journal.Reserve(execution.ExecutionId, execution.SessionRef, inputRefs, enforcementRevision!, text, live.Speaker); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return new("failed", "journal_unavailable");
            }
            if (reservation.Status != "pending") return reservation;
            attempt = reservation.JournalRef!;
            execution.Reserved(attempt);
            binding = live;
            sender = transport;
        }

        // Pre-send destination check, outside the lock. Any doubt here is proof that nothing was dispatched.
        TransportRouteDescription description;
        try { description = await sender.DescribeRouteAsync(binding.TransportRoute, cancellationToken).ConfigureAwait(false); }
        catch (Exception) { return Resolve(attempt, "not_sent", "route_unresolved", "failed"); }
        if (description is null || !Same(description.Channel, binding.Channel, ignoreCase: true) ||
            !Same(description.AccountId, binding.AccountId, ignoreCase: false) || !Same(description.Recipient, binding.Recipient, ignoreCase: false))
            return Resolve(attempt, "not_sent", "route_mismatch", "failed");

        // Short fence: kill switch and cancellation are rechecked immediately before the external call.
        lock (gate)
        {
            if (disposed || mode != OutboundMode.Enabled || !ReferenceEquals(live, binding) || !ReferenceEquals(transport, sender))
                return Resolve(attempt, "not_sent", "disarmed_before_dispatch", "disabled");
            if (cancellationToken.IsCancellationRequested) return Resolve(attempt, "not_sent", "cancelled_before_dispatch", "failed");
        }

        SendReceipt receipt;
        try { receipt = await sender.SendTextAsync(binding.TransportRoute, text, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Indeterminate(attempt, "cancelled_after_dispatch"); }
        catch (Exception) { return Indeterminate(attempt, "transport_failure"); }

        // Receipt evidence is checked after the fact; a mismatch cannot be undone, so it latches the host.
        if (receipt is null || string.IsNullOrWhiteSpace(receipt.ProviderMessageId)) return Indeterminate(attempt, "receipt_missing");
        if (receipt.Channel is not null && !Same(receipt.Channel, binding.Channel, ignoreCase: true) ||
            receipt.Recipient is not null && !Same(receipt.Recipient, binding.Recipient, ignoreCase: false) ||
            receipt.AccountId is not null && !Same(receipt.AccountId, binding.AccountId, ignoreCase: false))
            return Indeterminate(attempt, "receipt_route_mismatch");
        if (receipt.Channel is null || receipt.Recipient is null) return Indeterminate(attempt, "receipt_route_unverified");
        execution.ReceiptAccount(receipt.AccountId is null ? "unattested" : "attested");

        lock (gate)
        {
            if (disposed || !journal.Resolve(attempt, "sent", "provider_accepted", Hash(execution.SessionRef + "\n" + receipt.ProviderMessageId), receipt.ProviderMessageId))
            {
                mode = OutboundMode.Disabled;
                return new("indeterminate", "journal_unavailable_after_send", attempt);
            }
        }
        execution.Confirmed(receipt.ProviderMessageId);
        return new("sent", "provider_accepted", attempt, bindings.WouldTransitionTo, receipt.ProviderMessageId, binding.Speaker);
    }

    // Provider accepted, local history committed once, transition decided: the attempt is closed.
    internal void HistoryCommitted(OutboundExecution execution, bool transitionApplied)
    {
        lock (gate)
        {
            if (execution.AttemptRef is null || execution.ProviderMessageId is null) throw new InvalidOperationException("Only a confirmed send can commit history.");
            if (disposed) return;
            journal.Resolve(execution.AttemptRef, "history_committed", transitionApplied ? "transition_applied" : "transition_not_applied", null);
        }
    }

    // Recovery surface. Reads and appends journal state only; it never dispatches and never infers delivery.
    public IReadOnlyList<PendingSpeechCommit> PendingSpeechCommits(SessionKey session)
    {
        lock (gate)
        {
            if (disposed) return [];
            return journal.RecoverableSends(Hash(JsonSerializer.Serialize(session)))
                .Select(send => new PendingSpeechCommit(send.AttemptRef, send.ProviderMessageId, send.Text, new(send.SpeakerName, send.SpeakerStableId))).ToArray();
        }
    }

    // Speech was appended to local history exactly once by the caller; the transition decision is whatever it was.
    public void CompleteSpeechCommit(SessionKey session, string attemptRef, bool transitionApplied)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var sessionRef = Hash(JsonSerializer.Serialize(session));
            if (!journal.RecoverableSends(sessionRef).Any(send => send.AttemptRef == attemptRef) ||
                !journal.Resolve(attemptRef, "history_committed", transitionApplied ? "recovered_transition_applied" : "recovered_transition_not_applied", null))
                throw new InvalidOperationException("No durable receipt awaits a history commit for this attempt.");
        }
    }

    // Explicit reconciliation of an indeterminate attempt with named external evidence. "Sent" requires the provider
    // message ID from that evidence and then still needs a speech commit; nothing here dispatches or retries.
    public void Reconcile(SessionKey session, string attemptRef, ReconciliationVerdict verdict, string evidenceRef, string? providerMessageId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRef);
        if (!Enum.IsDefined(verdict)) throw new ArgumentOutOfRangeException(nameof(verdict));
        if (verdict == ReconciliationVerdict.Sent && string.IsNullOrWhiteSpace(providerMessageId))
            throw new ArgumentException("A sent verdict requires the provider message ID from the evidence.", nameof(providerMessageId));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var sessionRef = Hash(JsonSerializer.Serialize(session));
            if (!journal.IsIndeterminate(sessionRef, attemptRef)) throw new InvalidOperationException("Only an indeterminate attempt can be reconciled.");
            var status = verdict switch { ReconciliationVerdict.Sent => "sent", ReconciliationVerdict.NotSent => "not_sent", _ => "abandoned" };
            var providerRef = providerMessageId is null ? null : Hash(sessionRef + "\n" + providerMessageId);
            if (!journal.Resolve(attemptRef, status, "reconciled:" + evidenceRef, providerRef, providerMessageId))
                throw new InvalidOperationException("Reconciliation could not be recorded durably.");
        }
    }

    private ShadowSendResult Resolve(string attempt, string journalStatus, string diagnosticCode, string resultStatus)
    {
        lock (gate) { if (!disposed) journal.Resolve(attempt, journalStatus, diagnosticCode, null); }
        return new(resultStatus, diagnosticCode, attempt);
    }

    // Possible external effect with unknown outcome: never retried, and the host stops dispatching until reconciled.
    private ShadowSendResult Indeterminate(string attempt, string diagnosticCode)
    {
        lock (gate)
        {
            mode = OutboundMode.Disabled;
            if (!disposed) journal.Resolve(attempt, "indeterminate", diagnosticCode, null);
        }
        return new("indeterminate", diagnosticCode, attempt);
    }

    private static bool Same(string? observed, string expected, bool ignoreCase) =>
        observed is not null && string.Equals(observed, expected, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    // Exact document pin: serialization-only changes require explicit host review/rearm as well.
    internal static string Fingerprint(JsonElement value) => Hash(value.GetRawText());
    public void Dispose() { lock (gate) { mode = OutboundMode.Disabled; disposed = true; journal.Dispose(); } }
}

internal sealed class OutboundExecution
{
    private readonly ShadowOutboundHost host;
    private readonly OutboundBindings bindings;
    private TypedValue? recall;
    private TypedValue? compiled;
    private TypedValue? current;
    private TypedValue? approved;
    private string? revision;
    private int attempts;
    private OutboundMode decisionMode;
    private bool allowlisted;
    private bool provenanceDecision;
    private string? generationRef;
    private string? boundarySetRef;
    private string receiptAccount = "none";
    internal bool ProjectAuthorized { get; }
    internal string ExecutionId { get; }
    internal SessionKey Session { get; }
    internal string SessionRef { get; }
    internal SessionEntryKind Kind { get; }
    internal IReadOnlyList<string> InboundIds { get; }
    internal string? AttemptRef { get; private set; }
    internal string? ProviderMessageId { get; private set; }
    internal OutboundExecution(ShadowOutboundHost host, OutboundBindings bindings, bool projectAuthorized,
        string executionId, SessionKey session, SessionEntryKind kind, string[] inboundIds)
    {
        this.host = host; this.bindings = bindings; ProjectAuthorized = projectAuthorized;
        ExecutionId = executionId; Session = session; Kind = kind; InboundIds = inboundIds;
        SessionRef = ShadowOutboundHost.Hash(JsonSerializer.Serialize(session));
    }
    internal void Recall(string node, TypedValue value)
    {
        if (!bindings.RecallNodes.Contains(node)) return;
        recall = value; compiled = null; approved = null; revision = null;
    }
    internal void Compiled(string node, TypedValue input, TypedValue output)
    {
        if (!bindings.CompilerNodes.Contains(node)) return;
        compiled = ReferenceEquals(input, recall) ? output : null; approved = null; revision = null;
        boundarySetRef = compiled is null ? null : Guid.NewGuid().ToString("N");
    }
    internal void Generated(string node, IReadOnlyDictionary<string, TypedValue> outputs)
    {
        if (!bindings.CandidatePorts.TryGetValue(node, out var port)) return;
        current = outputs.TryGetValue(port, out var value) && value.Type is StringType ? value : null;
        generationRef = Guid.NewGuid().ToString("N");
        approved = null; revision = null;
        // Legitimate empty set does not require a model call or a fake "Allow" node.
        if (current is not null && compiled?.Value.GetArrayLength() == 0)
        { approved = current; revision = "trusted-empty-boundaries:v1"; }
    }
    internal void Enforced(string node, TypedValue candidate, TypedValue boundaries, bool allow, string enforcementRevision)
    {
        if (!bindings.EnforcementNodes.Contains(node)) return;
        approved = allow && current is not null && compiled is not null && ReferenceEquals(current, candidate) && ReferenceEquals(compiled, boundaries) ? candidate : null;
        revision = approved is null ? null : enforcementRevision;
    }
    internal ShadowSendResult Send(TypedValue candidate) => SendAsync(candidate, CancellationToken.None).GetAwaiter().GetResult();
    internal Task<ShadowSendResult> SendAsync(TypedValue candidate, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref attempts) != 1) return Task.FromResult(new ShadowSendResult("blocked", "duplicate_attempt"));
        var provenance = current is not null && approved is not null && ReferenceEquals(current, candidate) && ReferenceEquals(approved, candidate);
        return host.GuardAsync(this, provenance, revision, provenance ? candidate.Value.GetString() ?? "" : "", cancellationToken);
    }
    internal void HistoryCommitted(bool transitionApplied) => host.HistoryCommitted(this, transitionApplied);
    internal void Reserved(string attemptRef) => AttemptRef = attemptRef;
    internal void Confirmed(string providerMessageId) => ProviderMessageId = providerMessageId;
    internal void ReceiptAccount(string verdict) => receiptAccount = verdict;
    internal void RecordGuardAudit(OutboundMode mode, bool allowed, bool provenance)
    { decisionMode = mode; allowlisted = allowed; provenanceDecision = provenance; }
    internal IReadOnlyDictionary<string, string> Trace(ShadowSendResult result) => new Dictionary<string, string>
    {
        ["execution"] = ExecutionId, ["sessionRef"] = SessionRef, ["channel"] = Session.ChannelId,
        ["deploymentMode"] = decisionMode.ToString().ToLowerInvariant(), ["attemptOrdinal"] = attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["allowlistDecision"] = allowlisted ? "allowed" : "denied",
        ["provenanceDecision"] = provenanceDecision ? "approved" : "denied",
        ["generationRef"] = generationRef ?? "none", ["boundarySetRef"] = boundarySetRef ?? "none",
        ["enforcementRevision"] = revision ?? "none", ["journalRef"] = result.JournalRef ?? "none",
        ["result"] = result.Status, ["diagnosticCode"] = result.DiagnosticCode,
        ["wouldTransitionTo"] = result.WouldTransitionTo ?? "none",
        ["providerRef"] = result.ProviderMessageId is null ? "none" : ShadowOutboundHost.Hash(SessionRef + "\n" + result.ProviderMessageId),
        ["receiptAccount"] = receiptAccount
    };
}
