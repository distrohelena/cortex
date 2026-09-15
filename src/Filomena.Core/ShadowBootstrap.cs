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
            && sender.TryGetProperty("nome", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() : "Pessoa";
        // Only display text is retained. Structural refs are exclusively from the trusted binding.
        // Rich reply provenance is deliberately deferred, never guessed from transcript text.
        return input.Message with
        {
            Message = JsonSerializer.SerializeToElement(new
            {
                texto = text.GetString(),
                remetente = new { nome = name, stableId = person.ActorStableId },
                canal = new { nome = "DM", nativeId = input.Session.NativeConversationId },
                reply = (object?)null
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

// Internal handoff shared with the explicit manual harness; no transport listener is started here.
// Opening a future live source requires a separate reviewed change after all preflight proofs pass.
internal sealed class ShadowRuntimeHandoff : IAsyncDisposable
{
    private readonly SessionFlowRuntime runtime;
    private readonly ShadowSessionBinding binding;
    private readonly ShadowInboundJournal inbox;
    private readonly ShadowOutboundHost host;
    private readonly SemaphoreSlim admissionGate = new(1);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentQueue<ShadowIngressAudit> audit = new();
    internal IReadOnlyList<ShadowIngressAudit> Audit => audit.ToArray();
    internal IReadOnlyList<SessionFlowOutcome> Outcomes => runtime.ReadOutcomes(binding.Session);
    internal IReadOnlyList<SessionHistoryEntry> History => runtime.ReadHistory(binding.Session);
    internal IReadOnlyList<SessionTraceEvent> Trace => runtime.ReadTrace(binding.Session);
    internal SessionSnapshot? Snapshot => runtime.ReadSession(binding.Session);

    internal ShadowRuntimeHandoff(PinnedShadowProject project, ShadowSessionBinding binding, ShadowInboundJournal inbox,
        ShadowOutboundHost host, IFlowCompletion completion, IMemoryReader memory)
    {
        if (!inbox.IsBoundTo(binding, project.Project.ProjectRevision)) throw new InvalidOperationException("Inbox binding mismatch.");
        if (inbox.RequiresRuntimeRestore) throw new InvalidOperationException("Runtime context restoration required; no automatic replay.");
        if (host.EffectiveMode != OutboundMode.Shadow) throw new InvalidOperationException("Shadow ceiling required.");
        this.binding = binding; this.inbox = inbox; this.host = host;
        runtime = new(TimeProvider.System, completion, memory, outboundHost: host);
        runtime.UpdateProject(project.Project);
    }

    internal async Task<ShadowInboundDecision> AcceptAsync(CanonicalShadowInbound input, CancellationToken cancellationToken)
    {
        await admissionGate.WaitAsync(cancellationToken);
        try
        {
            var observed = inbox.Observe(input);
            if (observed.CanonicalRef is { } inputRef) audit.Enqueue(new(observed.Status, binding.OpaqueRef, inputRef, null, null));
            if (observed.Status != "observed") return observed;
            // Once durably observed, cancellation/failure leaves an unresolved record, never a hidden retry.
            if (host.EffectiveMode != OutboundMode.Shadow || host.RequiresReconciliation(binding.Session))
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
