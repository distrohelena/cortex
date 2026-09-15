using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed record ShadowSendResult(string Status, string DiagnosticCode, string? JournalRef = null, string? WouldTransitionTo = null);

/// <summary>Phase 0 host authority. Deliberately has NO transport or history writer dependency.</summary>
public sealed class ShadowOutboundHost : IDisposable
{
    private readonly object gate = new();
    private readonly OutboundJournal journal;
    private readonly string projectHash;
    private readonly OutboundBindings bindings;
    private readonly HashSet<SessionKey> sessions;
    private readonly TransportSafetyCapabilities capabilities;
    private OutboundMode mode = OutboundMode.Disabled;
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
    // Mode is the operational request; Phase 0 never has an effective Enabled permission.
    public OutboundMode EffectiveMode { get { lock (gate) return mode == OutboundMode.Shadow && !disposed ? OutboundMode.Shadow : OutboundMode.Disabled; } }

    // Explicit operational rearm. Restart never restores a mode from project or journal.
    public void Rearm(OutboundMode requested)
    {
        if (!Enum.IsDefined(requested)) throw new ArgumentOutOfRangeException(nameof(requested));
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); mode = requested; }
    }

    internal OutboundExecution Begin(JsonElement project, string executionId, SessionKey session, SessionEntryKind kind, IEnumerable<string> inboundIds) =>
        new(this, bindings, Fingerprint(project) == projectHash, executionId, session, kind, inboundIds.ToArray());

    internal ShadowSendResult Guard(OutboundExecution execution, bool provenance, string? enforcementRevision)
    {
        // Authorization is sampled at the sink, not at generation. Rearm is serialized with this decision.
        lock (gate)
        {
            execution.RecordGuardAudit(mode, sessions.Contains(execution.Session), provenance);
            if (disposed || mode == OutboundMode.Disabled) return new("disabled", "outbound_disabled");
            if (mode == OutboundMode.Enabled) return new("blocked", capabilities.Complete ? "phase0_dispatch_unavailable" : "transport_capabilities_incomplete");
            if (!execution.ProjectAuthorized) return new("blocked", "project_not_authorized");
            if (execution.Kind != SessionEntryKind.Dm) return new("blocked", "dm_only");
            if (!sessions.Contains(execution.Session)) return new("blocked", "session_not_allowlisted");
            if (!provenance) return new("blocked", "invalid_provenance");
            if (execution.InboundIds.Count == 0 || execution.InboundIds.Any(string.IsNullOrWhiteSpace)) return new("blocked", "invalid_origin_identity");
            try
            {
                var result = journal.RecordShadow(execution.ExecutionId, execution.SessionRef,
                    execution.InboundIds.Select(id => Hash(execution.SessionRef + "\n" + id)).ToArray(), enforcementRevision!);
                return result with { WouldTransitionTo = result.Status == "shadowed" ? bindings.WouldTransitionTo : null };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return new("failed", "journal_unavailable");
            }
        }
    }

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
    internal bool ProjectAuthorized { get; }
    internal string ExecutionId { get; }
    internal SessionKey Session { get; }
    internal string SessionRef { get; }
    internal SessionEntryKind Kind { get; }
    internal IReadOnlyList<string> InboundIds { get; }
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
    internal ShadowSendResult Send(TypedValue candidate)
    {
        if (Interlocked.Increment(ref attempts) != 1) return new("blocked", "duplicate_attempt");
        return host.Guard(this, current is not null && approved is not null && ReferenceEquals(current, candidate) && ReferenceEquals(approved, candidate), revision);
    }
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
        ["wouldTransitionTo"] = result.WouldTransitionTo ?? "none"
    };
}
