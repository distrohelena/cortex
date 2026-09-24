using System.Security.Cryptography;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Outbound;

namespace Filomena.Core.Shadow;

internal static class ShadowInboundCanonicalizer
{
    internal static SessionMessageEnvelope Normalize(CanonicalShadowInbound input)
    {
        var payload = input.Message.Message;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("texto", out var text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString()))
            throw new InvalidDataException("Inbound text missing.");
        var person = MemoryIdentity.Person(input.ConversationNamespace, input.ActorId, null);
        var name = payload.TryGetProperty("remetente", out var sender) && sender.ValueKind == JsonValueKind.Object
            && sender.TryGetProperty("nome", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() : "";
        // Only display text is retained. Structural refs are exclusively from the trusted binding.
        var reply = payload.TryGetProperty("reply", out var quote) ? quote.Clone() : JsonSerializer.SerializeToElement<object?>(null);
        FlowDomain.Check(reply, new NullableType(FlowDomain.Reply));
        return input.Message with
        {
            Message = JsonSerializer.SerializeToElement(new
            {
                texto = text.GetString(),
                remetente = new { nome = name, stableId = person.ActorStableId },
                canal = new { nome = input.Kind == SessionEntryKind.Group ? "Grupo" : "DM", nativeId = input.Session.NativeConversationId },
                reply
            })
        };
    }
}

public sealed record ShadowIngressAudit(string Status, string SessionRef, string CanonicalInputRef, string? BatchId, string? ExecutionId);

/// <summary>Detached immutable revision. No file watcher, reload, model call or project authority.</summary>
public sealed class PinnedShadowProject
{
    public SessionFlowProject Project { get; }
    public PinnedShadowProject(SessionFlowProject project)
    {
        var compiled = SessionFlowProjectCompiler.Compile(project);
        Project = new(compiled.ProjectId, compiled.ProjectVersion, compiled.ProjectRevision, compiled.Document.Clone());
    }
    public static PinnedShadowProject FromBytes(byte[] bytes, string projectId, long version, string expectedSha256)
    {
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        if (!StringComparer.Ordinal.Equals(digest, expectedSha256)) throw new InvalidDataException("Project digest mismatch.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var project = root.TryGetProperty("format", out var format) && format.GetString() == "filomena-local-project" ? root.GetProperty("project") : root;
        return new(new(projectId, version, digest, project.Clone()));
    }
}

// Common composition (pinned project, trusted inbox, host, serial admission) behind two fixed ceilings.
// The Shadow wrapper requires an effective Shadow host and therefore can never send; the Live wrapper requires an
// effective Enabled host, which only exists with complete capabilities and an administratively bound route.
// Neither starts a transport listener; opening a live source remains a separate reviewed change.
internal abstract class RuntimeHandoffCore : IAsyncDisposable
{
    private protected readonly SessionFlowRuntime runtime;
    private protected readonly ShadowSessionBinding binding;
    private protected readonly ShadowOutboundHost host;
    private readonly ShadowInboundJournal inbox;
    private readonly OutboundMode ceiling;
    private readonly SemaphoreSlim admissionGate = new(1);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentQueue<ShadowIngressAudit> audit = new();
    internal IReadOnlyList<ShadowIngressAudit> Audit => audit.ToArray();
    internal IReadOnlyList<SessionFlowOutcome> Outcomes => runtime.ReadOutcomes(binding.Session);
    internal IReadOnlyList<SessionHistoryEntry> History => runtime.ReadHistory(binding.Session);
    internal IReadOnlyList<SessionTraceEvent> Trace => runtime.ReadTrace(binding.Session);
    internal SessionSnapshot? Snapshot => runtime.ReadSession(binding.Session);
    internal SessionRuntimeContext? CaptureContext() => runtime.CaptureContext(binding.Session);
    internal void RestoreContext(SessionRuntimeContext context) => runtime.RestoreContext(binding.Session, context, binding.Kind);
    private bool Armed => host.EffectiveMode == ceiling;

    private protected RuntimeHandoffCore(OutboundMode ceiling, PinnedShadowProject project, ShadowSessionBinding binding, ShadowInboundJournal inbox,
        ShadowOutboundHost host, IFlowCompletion completion, IMemoryReader memory)
    {
        if (ceiling is not (OutboundMode.Shadow or OutboundMode.Enabled)) throw new ArgumentOutOfRangeException(nameof(ceiling));
        if (!inbox.IsBoundTo(binding, project.Project.ProjectRevision)) throw new InvalidOperationException("Inbox binding mismatch.");
        if (inbox.RequiresRuntimeRestore) throw new InvalidOperationException("Runtime context restoration required; no automatic replay.");
        if (host.EffectiveMode != ceiling)
            throw new InvalidOperationException(ceiling == OutboundMode.Shadow ? "Shadow ceiling required." : "Effective Enabled host required for live admission.");
        this.ceiling = ceiling; this.binding = binding; this.inbox = inbox; this.host = host;
        runtime = new(TimeProvider.System, completion, memory, outboundHost: host);
        runtime.UpdateProject(project.Project);
    }

    private protected void RecordAudit(ShadowIngressAudit item) => audit.Enqueue(item);

    internal async Task<ShadowInboundDecision> AcceptAsync(CanonicalShadowInbound input, CancellationToken cancellationToken)
    {
        await admissionGate.WaitAsync(cancellationToken);
        try
        {
            var observed = inbox.Observe(input);
            if (observed.CanonicalRef is { } inputRef) audit.Enqueue(new(observed.Status, binding.OpaqueRef, inputRef, null, null));
            if (observed.Status != "observed") return observed;
            // Once durably observed, cancellation/failure leaves an unresolved record, never a hidden retry.
            if (!Armed || host.RequiresReconciliation(binding.Session))
            {
                inbox.RequireReconciliation(input.Message.ProviderMessageId);
                return new("preserved", "deployment_disarmed_or_unresolved", observed.CanonicalRef);
            }
            SessionAdmission admission;
            try { admission = await runtime.ReceiveAsync(binding.Session, ShadowInboundCanonicalizer.Normalize(input), cancellationToken); }
            catch { inbox.RequireReconciliation(input.Message.ProviderMessageId); throw; }
            if (admission.Status != SessionAdmissionStatus.Accepted || admission.AdmissionWatermark is not { } watermark)
            {
                inbox.RequireReconciliation(input.Message.ProviderMessageId);
                return new("preserved", "runtime_handoff_unresolved", observed.CanonicalRef);
            }
            inbox.Admitted(input.Message.ProviderMessageId, watermark);
            return new("admitted", "normal_session_runtime", observed.CanonicalRef);
        }
        finally { admissionGate.Release(); }
    }

    // Explicit checkpoint; does not poll a transport or implement a second batching algorithm.
    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        await runtime.WaitForIdleAsync(binding.Session, cancellationToken);
        await admissionGate.WaitAsync(cancellationToken);
        try
        {
            foreach (var outcome in Outcomes.Where(o => !completed.Contains(o.ExecutionId)))
            {
                foreach (var providerId in outcome.ProviderMessageIds)
                {
                    inbox.Completed(providerId, outcome.BatchId, outcome.ExecutionId, outcome.Success);
                    audit.Enqueue(new(outcome.Success ? "completed" : "execution_failed", binding.OpaqueRef,
                        ShadowSessionBinding.Ref(binding.OpaqueRef + "\n" + providerId), outcome.BatchId, outcome.ExecutionId));
                }
                completed.Add(outcome.ExecutionId);
            }
        }
        finally { admissionGate.Release(); }
    }
    public async ValueTask DisposeAsync() { await runtime.DisposeAsync(); admissionGate.Dispose(); }
}

// Shadow: no sender can be reached from here. The host's effective mode must be Shadow at construction and at every admission.
internal sealed class ShadowRuntimeHandoff(PinnedShadowProject project, ShadowSessionBinding binding, ShadowInboundJournal inbox,
    ShadowOutboundHost host, IFlowCompletion completion, IMemoryReader memory)
    : RuntimeHandoffCore(OutboundMode.Shadow, project, binding, inbox, host, completion, memory);

// Live: requires an effective Enabled host. Before admitting anything it completes durable receipts whose speech
// never reached local history (once, keyed by provider ID, without any resend) and refuses to start while an
// attempt still needs explicit reconciliation. The FSM transition of a recovered speech is not reapplied: the
// session context is not restored across restarts, so it is recorded as not applied.
internal sealed class LiveRuntimeHandoff : RuntimeHandoffCore
{
    internal LiveRuntimeHandoff(PinnedShadowProject project, ShadowSessionBinding binding, ShadowInboundJournal inbox,
        ShadowOutboundHost host, IFlowCompletion completion, IMemoryReader memory)
        : base(OutboundMode.Enabled, project, binding, inbox, host, completion, memory)
    {
        foreach (var commit in host.PendingSpeechCommits(binding.Session))
        {
            var speech = new SessionMessageEnvelope(commit.ProviderMessageId, binding.Session.ChannelId, binding.Session.AccountId,
                binding.Session.NativeConversationId, DateTimeOffset.UtcNow, SessionMessageDirection.Outbound, binding.Kind,
                JsonSerializer.SerializeToElement(new
                {
                    texto = commit.Text,
                    remetente = new { nome = commit.Speaker.Name, stableId = commit.Speaker.StableId },
                    canal = new { nome = binding.Kind == SessionEntryKind.Group ? "Grupo" : "DM", nativeId = binding.Session.NativeConversationId },
                    reply = commit.ReplyToMessageId is null ? null : new { id = commit.ReplyToMessageId }
                }));
            var admission = runtime.ReceiveAsync(binding.Session, speech).AsTask().GetAwaiter().GetResult();
            if (admission.Status == SessionAdmissionStatus.Rejected)
                throw new InvalidOperationException("Durable speech could not be restored into history: " + admission.Reason);
            host.CompleteSpeechCommit(binding.Session, commit.AttemptRef, transitionApplied: false);
            RecordAudit(new("speech_recovered", binding.OpaqueRef, ShadowSessionBinding.Ref(binding.OpaqueRef + "\n" + commit.ProviderMessageId), null, commit.AttemptRef));
        }
        if (host.RequiresReconciliation(binding.Session))
            throw new InvalidOperationException("Explicit reconciliation of an uncertain attempt is required before live admission.");
    }
}

public static class LiveRouteBindings
{
    // Everything except the opaque transport route, the speaker's display name and the verified single-part ceiling
    // comes from the reviewed session binding. Nothing is transcribed by hand or inferred from a session label.
    public static LiveRouteBinding FromShadow(ShadowSessionBinding binding, string transportRoute, string nativeRecipient, string speakerName, int maxSinglePartCharacters)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.SelfActorIds.Count != 1) throw new InvalidOperationException("Exactly one host self identity is required for a live route.");
        return new(binding.Session, transportRoute, binding.Session.ChannelId, binding.Session.AccountId, nativeRecipient,
            new(speakerName, binding.SelfActorIds.Single()), maxSinglePartCharacters);
    }
}
