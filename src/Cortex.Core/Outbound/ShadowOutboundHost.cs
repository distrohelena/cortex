using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cortex.Core.Conversations;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Outbound;

public enum OutboundMode { Disabled, Shadow, Enabled }

public sealed record TransportSafetyCapabilities(bool StableOrigin, bool StableInboundIdentity,
    bool RouteBinding, bool SelfSuppression, bool SendReceipt, bool EchoCorrelation, bool SinglePhysicalMessage)
{
    public static TransportSafetyCapabilities Unproven { get; } = new(false, false, false, false, false, false, false);
    public bool Complete => StableOrigin && StableInboundIdentity && RouteBinding && SelfSuppression && SendReceipt && EchoCorrelation && SinglePhysicalMessage;
}

// Trusted host configuration, never deserialized from a graph. IDs identify reviewed roles, not titles.
public sealed record OutboundBindings(IReadOnlyList<string> RecallNodes, IReadOnlyList<string> CompilerNodes,
    IReadOnlyDictionary<string, string> CandidatePorts, IReadOnlyList<string> EnforcementNodes, string? WouldTransitionTo)
{
    public IReadOnlyDictionary<string, string> ReplyTargetPorts { get; init; } = new Dictionary<string, string>();
}

// What the transport reports about a route before dispatch. Missing fields are missing, never inferred.
public sealed record TransportRouteDescription(string? Channel, string? AccountId, string? Recipient);

/// <summary>Legacy route lookup and send. Separate calls do not prove atomic destination binding.</summary>
public interface IOutboundTransport : IConversationSender
{
    Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken);
}

// All fields come from the host's reviewed binding and durable reservation, never from generation.
public sealed record BoundSendRequest(string AttemptRef, string TransportRoute, string Channel,
    string AccountId, string Recipient, string Text, int MaxSinglePartCharacters)
{
    public string? ReplyToMessageId { get; init; }
    public MemberDmBoundTextBinding? MemberDm { get; init; }
}

/// <summary>Validated native membership and route facts for one enrolled member-DM send.</summary>
public sealed record MemberDmBoundTextBinding(string NativeConversationId, string ScopeRef,
    string RouteBindingRef, string EffectiveConfigRevision, string MembershipLeaseRef);

/// <summary>
/// Trusted sender contract: compare the expected destination at the physical dispatch boundary,
/// carry the durable attempt identity, and send at most one physical message without hidden retries.
/// A lookup followed by the legacy SendTextAsync does not implement this contract.
/// </summary>
public interface IBoundOutboundTransport : IOutboundTransport
{
    Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken cancellationToken);
}

// How the host's own confirmed speech is identified in local history. Host-owned, never chosen by the project.
public sealed record OutboundSpeaker(string Name, string StableId);

// Administrative, host-owned binding of exactly one allowlisted session to one transport route and its expected
// exact destination. The project/model cannot construct, widen or replace it.
public sealed record LiveRouteBinding(SessionKey Session, string TransportRoute, string Channel, string AccountId,
    string Recipient, OutboundSpeaker Speaker, int MaxSinglePartCharacters)
{
    public bool AllowExactGroupDelivery { get; init; }
}

public sealed record ShadowSendResult(string Status, string DiagnosticCode, string? JournalRef = null, string? WouldTransitionTo = null,
    string? ProviderMessageId = null, OutboundSpeaker? Speaker = null)
{
    internal string? RecoveredText { get; init; }
    public string? ReplyToMessageId { get; init; }
}

// A durable provider receipt whose speech has not yet been committed to local history (crash or restart in between).
public sealed record PendingSpeechCommit(string AttemptRef, string ProviderMessageId, string Text, OutboundSpeaker Speaker)
{
    public string? ReplyToMessageId { get; init; }
}

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
            new Dictionary<string, string>(bindings.CandidatePorts), bindings.EnforcementNodes.ToArray(), bindings.WouldTransitionTo)
        { ReplyTargetPorts = new Dictionary<string, string>(bindings.ReplyTargetPorts) };
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
                return capabilities.Complete && live is not null && transport is IBoundOutboundTransport ? OutboundMode.Enabled : OutboundMode.Disabled;
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
        if (binding.AllowExactGroupDelivery && (binding.Channel != "whatsapp"
            || binding.AccountId != binding.Session.AccountId || binding.Channel != binding.Session.ChannelId
            || binding.Recipient != binding.Session.NativeConversationId
            || !System.Text.RegularExpressions.Regex.IsMatch(binding.Recipient, @"\A[0-9]+(-[0-9]+)?@g\.us\z")))
            throw new ArgumentException("Group delivery requires the exact host-bound WhatsApp group.", nameof(binding));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!sessions.Contains(binding.Session)) throw new InvalidOperationException("A live route must bind an allowlisted exact session.");
            live = binding;
            this.transport = transport;
        }
    }

    internal OutboundExecution Begin(JsonElement project, string executionId, SessionKey session, SessionEntryKind kind,
        IEnumerable<string> inboundIds, IEnumerable<string>? replyTargetIds = null, bool shadowOnly = false,
        Action? validateCurrentContext = null) =>
        new(this, bindings, Fingerprint(project) == projectHash, Fingerprint(project), executionId, session, kind, inboundIds.ToArray(),
            replyTargetIds?.ToHashSet(StringComparer.Ordinal) ?? [], shadowOnly, validateCurrentContext);

    internal OutboundExecution RestoreExecution(ExecutionHostBinding binding, OutboundExecutionEvidence evidence,
        IReadOnlyDictionary<PortRef, TypedValue> values)
    {
        if (evidence.Version != 1 || binding.OutboundHost != this || binding.Scope.Session != evidence.Session
            || binding.Scope.ExecutionId != evidence.ExecutionId || Fingerprint(binding.Project) != evidence.ProjectFingerprint
            || Hash(JsonSerializer.Serialize(bindings)) != evidence.RolesFingerprint || evidence.Attempts < 0)
            throw new InvalidDataException("Outbound evidence does not match the current host binding.");
        var execution = Begin(binding.Project, evidence.ExecutionId, evidence.Session, evidence.Kind,
            evidence.InboundIds, evidence.ReplyTargetIds, evidence.ShadowOnly, binding.ValidateCurrentContext);
        execution.RestoreEvidence(evidence, values);
        _ = RecoverExecutionSend(execution);
        return execution;
    }

    internal ShadowSendResult? RecoverExecutionSend(OutboundExecution execution)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var recovered = journal.RecoverExecution(execution.SessionRef, execution.ExecutionId);
            if (recovered is not null) execution.Recovered(recovered);
            return recovered;
        }
    }

    internal async Task<ShadowSendResult> GuardAsync(OutboundExecution execution, bool provenance, string? enforcementRevision, string text, CancellationToken cancellationToken)
    {
        LiveRouteBinding binding;
        IBoundOutboundTransport sender;
        string attempt;
        // Runtime-owned narrowing only. Invoke outside the host lock to avoid runtime/host lock inversion.
        if (!execution.ContextStillValid()) return new("blocked", "context_stale_before_reservation");
        // Authorization is sampled at the sink, not at generation. Rearm is serialized with this decision.
        lock (gate)
        {
            execution.RecordGuardAudit(mode, sessions.Contains(execution.Session), provenance);
            if (disposed || mode == OutboundMode.Disabled) return new("disabled", "outbound_disabled");
            if (execution.ShadowOnly && mode != OutboundMode.Shadow) return new("disabled", "shadow_preview_requires_shadow");
            if (!execution.ProjectAuthorized) return new("blocked", "project_not_authorized");
            var groupSession = execution.Session.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal);
            if (execution.Kind is not (SessionEntryKind.Dm or SessionEntryKind.Group)
                || (execution.Kind == SessionEntryKind.Group) != groupSession)
                return new("blocked", "dm_only");
            if (!sessions.Contains(execution.Session)) return new("blocked", "session_not_allowlisted");
            if (!provenance) return new("blocked", "invalid_provenance");
            if (string.IsNullOrWhiteSpace(text)) return new("blocked", "empty_candidate");
            if (!execution.ReplySelectionValid) return new("blocked", "invalid_reply_target");
            if (execution.InboundIds.Count == 0 || execution.InboundIds.Any(string.IsNullOrWhiteSpace)) return new("blocked", "invalid_origin_identity");
            var inputRefs = execution.InboundIds.Select(id => Hash(execution.SessionRef + "\n" + id)).ToArray();
            if (mode == OutboundMode.Shadow)
            {
                try
                {
                    var result = journal.RecordShadow(execution.ExecutionId, execution.SessionRef, inputRefs, enforcementRevision!, execution.ReplyToMessageId);
                    return result with { WouldTransitionTo = result.Status == "shadowed" ? bindings.WouldTransitionTo : null };
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    return new("failed", "journal_unavailable");
                }
            }
            // Group transport delivery is not authorized by a shadow receipt.
            if (execution.Kind == SessionEntryKind.Group && live?.AllowExactGroupDelivery != true)
                return new("blocked", "dm_only");
            if (!capabilities.Complete) return new("blocked", "transport_capabilities_incomplete");
            if (live is null || transport is null || live.Session != execution.Session) return new("blocked", "route_not_bound");
            if (transport is not IBoundOutboundTransport boundSender) return new("blocked", "bound_transport_required");
            if (text.Length > live.MaxSinglePartCharacters) return new("blocked", "payload_exceeds_single_part");
            ShadowSendResult reservation;
            try { reservation = journal.Reserve(execution.ExecutionId, execution.SessionRef, inputRefs, enforcementRevision!, text, live.Speaker, execution.ReplyToMessageId); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return new("failed", "journal_unavailable");
            }
            if (reservation.Status != "pending") return reservation;
            attempt = reservation.JournalRef!;
            execution.Reserved(attempt);
            binding = live;
            sender = boundSender;
        }

        // Pre-send destination check, outside the lock. Any doubt here is proof that nothing was dispatched.
        TransportRouteDescription description;
        try { description = await sender.DescribeRouteAsync(binding.TransportRoute, cancellationToken).ConfigureAwait(false); }
        catch (Exception) { return Resolve(attempt, "not_sent", "route_unresolved", "failed"); }
        if (description is null || !Same(description.Channel, binding.Channel, ignoreCase: true) ||
            !Same(description.AccountId, binding.AccountId, ignoreCase: false) || !Same(description.Recipient, binding.Recipient, ignoreCase: false))
            return Resolve(attempt, "not_sent", "route_mismatch", "failed");

        if (!execution.ContextStillValid()) return Resolve(attempt, "not_sent", "context_stale_before_dispatch", "blocked");
        // Short fence: kill switch and cancellation are rechecked immediately before the external call.
        lock (gate)
        {
            if (disposed || mode != OutboundMode.Enabled || !ReferenceEquals(live, binding) || !ReferenceEquals(transport, sender))
                return Resolve(attempt, "not_sent", "disarmed_before_dispatch", "disabled");
            if (cancellationToken.IsCancellationRequested) return Resolve(attempt, "not_sent", "cancelled_before_dispatch", "failed");
        }

        SendReceipt receipt;
        try
        {
            receipt = await sender.SendBoundTextAsync(new(attempt, binding.TransportRoute, binding.Channel,
                binding.AccountId, binding.Recipient, text, binding.MaxSinglePartCharacters)
            { ReplyToMessageId = execution.ReplyToMessageId }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return Indeterminate(attempt, "cancelled_after_dispatch"); }
        catch (Exception) { return Indeterminate(attempt, "transport_failure"); }

        // Receipt evidence is checked after the fact; a mismatch cannot be undone, so it latches the host.
        if (receipt is null || string.IsNullOrWhiteSpace(receipt.ProviderMessageId)) return Indeterminate(attempt, "receipt_missing");
        if (receipt.Channel is not null && !Same(receipt.Channel, binding.Channel, ignoreCase: true) ||
            receipt.Recipient is not null && !Same(receipt.Recipient, binding.Recipient, ignoreCase: false) ||
            receipt.AccountId is not null && !Same(receipt.AccountId, binding.AccountId, ignoreCase: false))
            return Indeterminate(attempt, "receipt_route_mismatch");
        if (receipt.Channel is null || receipt.Recipient is null || receipt.AccountId is null) return Indeterminate(attempt, "receipt_route_unverified");
        if (receipt.ReplyToMessageId != execution.ReplyToMessageId) return Indeterminate(attempt, "receipt_reply_unverified");
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
        return new("sent", "provider_accepted", attempt, bindings.WouldTransitionTo, receipt.ProviderMessageId, binding.Speaker)
        { ReplyToMessageId = execution.ReplyToMessageId };
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
                .Select(send => new PendingSpeechCommit(send.AttemptRef, send.ProviderMessageId, send.Text, new(send.SpeakerName, send.SpeakerStableId))
                { ReplyToMessageId = send.ReplyToMessageId }).ToArray();
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
    private readonly string projectFingerprint;
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
    internal bool ShadowOnly { get; }
    internal string ExecutionId { get; }
    internal SessionKey Session { get; }
    internal string SessionRef { get; }
    internal SessionEntryKind Kind { get; }
    internal IReadOnlyList<string> InboundIds { get; }
    internal string? AttemptRef { get; private set; }
    internal string? ProviderMessageId { get; private set; }
    private readonly HashSet<string> replyTargetIds;
    private readonly Action? validateCurrentContext;
    internal bool SupportsReplySelection => bindings.ReplyTargetPorts.Count > 0;
    internal bool ReplySelectionValid { get; private set; } = true;
    internal string? ReplyToMessageId { get; private set; }
    internal OutboundExecution(ShadowOutboundHost host, OutboundBindings bindings, bool projectAuthorized, string projectFingerprint,
        string executionId, SessionKey session, SessionEntryKind kind, string[] inboundIds, HashSet<string> replyTargetIds,
        bool shadowOnly, Action? validateCurrentContext = null)
    {
        this.host = host; this.bindings = bindings; ProjectAuthorized = projectAuthorized;
        this.projectFingerprint = projectFingerprint;
        ShadowOnly = shadowOnly;
        ExecutionId = executionId; Session = session; Kind = kind; InboundIds = inboundIds;
        this.replyTargetIds = replyTargetIds;
        this.validateCurrentContext = validateCurrentContext;
        SessionRef = ShadowOutboundHost.Hash(JsonSerializer.Serialize(session));
    }
    internal OutboundExecutionEvidence CaptureEvidence(IReadOnlyDictionary<PortRef, TypedValue> values)
    {
        PortRef? Key(TypedValue? value) => value is null ? null : values.FirstOrDefault(x => ReferenceEquals(x.Value, value)).Key
            ?? throw new InvalidDataException("Outbound provenance is absent from the checkpoint.");
        return new(1, projectFingerprint, ShadowOutboundHost.Hash(JsonSerializer.Serialize(bindings)), ExecutionId,
            Session, Kind, InboundIds.ToArray(), replyTargetIds.Order(StringComparer.Ordinal).ToArray(), ShadowOnly,
            Key(recall), Key(compiled), Key(current), Key(approved), revision, attempts, AttemptRef, ProviderMessageId,
            ReplySelectionValid, ReplyToMessageId, generationRef, boundarySetRef, receiptAccount);
    }
    internal void RestoreEvidence(OutboundExecutionEvidence evidence, IReadOnlyDictionary<PortRef, TypedValue> values)
    {
        TypedValue? Value(PortRef? key) => key is null ? null : values.TryGetValue(key, out var value) ? value
            : throw new InvalidDataException("Missing outbound provenance value.");
        recall = Value(evidence.Recall); compiled = Value(evidence.Compiled); current = Value(evidence.Current); approved = Value(evidence.Approved);
        revision = evidence.EnforcementRevision; attempts = evidence.Attempts; AttemptRef = evidence.AttemptRef;
        ProviderMessageId = evidence.ProviderMessageId; ReplySelectionValid = evidence.ReplySelectionValid;
        ReplyToMessageId = evidence.ReplyToMessageId; generationRef = evidence.GenerationRef; boundarySetRef = evidence.BoundarySetRef;
        receiptAccount = evidence.ReceiptAccount;
    }
    internal void Recovered(ShadowSendResult result)
    {
        attempts = Math.Max(attempts, 1);
        AttemptRef = result.JournalRef;
        ProviderMessageId = result.ProviderMessageId;
    }
    internal bool ContextStillValid()
    {
        try { validateCurrentContext?.Invoke(); return true; }
        catch (Exception) { return false; }
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
    internal void Generated(string node, IReadOnlyDictionary<string, TypedValue> outputs, IReadOnlyDictionary<string, string>? references = null)
    {
        if (!bindings.CandidatePorts.TryGetValue(node, out var port)) return;
        current = outputs.TryGetValue(port, out var value) && value.Type is StringType ? value : null;
        generationRef = Guid.NewGuid().ToString("N");
        approved = null; revision = null;
        // Regeneration replaces both the text and its selection, including null.
        ReplyToMessageId = null;
        ReplySelectionValid = true;
        if (bindings.ReplyTargetPorts.TryGetValue(node, out var replyPort))
        {
            ReplySelectionValid = outputs.TryGetValue(replyPort, out var reply) &&
                reply.Type is NullableType { InnerType: StringType };
            if (ReplySelectionValid && reply!.Value.ValueKind != JsonValueKind.Null)
            {
                var matches = references?.Where(pair => pair.Key.StartsWith("message:", StringComparison.Ordinal)
                    && pair.Value == reply.Value.GetString()).Select(pair => pair.Key[8..]).ToArray() ?? [];
                ReplySelectionValid = Kind == SessionEntryKind.Group && matches.Length == 1 && replyTargetIds.Contains(matches[0]);
                if (ReplySelectionValid) ReplyToMessageId = matches[0];
            }
        }
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
        ["execution"] = ExecutionId,
        ["sessionRef"] = SessionRef,
        ["channel"] = Session.ChannelId,
        ["deploymentMode"] = decisionMode.ToString().ToLowerInvariant(),
        ["attemptOrdinal"] = attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["allowlistDecision"] = allowlisted ? "allowed" : "denied",
        ["provenanceDecision"] = provenanceDecision ? "approved" : "denied",
        ["generationRef"] = generationRef ?? "none",
        ["boundarySetRef"] = boundarySetRef ?? "none",
        ["enforcementRevision"] = revision ?? "none",
        ["journalRef"] = result.JournalRef ?? "none",
        ["result"] = result.Status,
        ["diagnosticCode"] = result.DiagnosticCode,
        ["wouldTransitionTo"] = result.WouldTransitionTo ?? "none",
        ["providerRef"] = result.ProviderMessageId is null ? "none" : ShadowOutboundHost.Hash(SessionRef + "\n" + result.ProviderMessageId),
        ["receiptAccount"] = receiptAccount,
        ["replyTargetRef"] = ReplyToMessageId is null ? "none" : ShadowOutboundHost.Hash(SessionRef + "\n" + ReplyToMessageId),
        ["replySelection"] = ReplySelectionValid ? "valid" : "invalid"
    };
}
