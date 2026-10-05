using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Outbound;

namespace Cortex.Core.Shadow;

public sealed class DurableObservedRun : IAsyncDisposable
{
    private readonly ISessionExecutionBackend backend;
    private readonly ISessionReviewBackend reviews;
    private readonly ShadowSessionBinding binding;
    private readonly ShadowInboundJournal inbox;
    private readonly ShadowOutboundHost host;
    private readonly OutboundMode ceiling;
    private readonly SemaphoreSlim admissionGate = new(1);
    private bool disposed;

    private DurableObservedRun(ISessionExecutionBackend backend, ISessionReviewBackend reviews, ShadowSessionBinding binding,
        ShadowInboundJournal inbox, ShadowOutboundHost host, OutboundMode ceiling)
    { this.backend = backend; this.reviews = reviews; this.binding = binding; this.inbox = inbox; this.host = host; this.ceiling = ceiling; }

    public static async Task<DurableObservedRun> CreateAsync(ISessionExecutionBackend backend, ISessionReviewBackend reviews,
        PinnedShadowProject project, ShadowSessionBinding binding, ShadowInboundJournal inbox, ShadowOutboundHost host, OutboundMode ceiling, CancellationToken token)
    {
        await ValidateRestoreAsync(backend, project, binding, inbox, host, ceiling, token).ConfigureAwait(false);
        await backend.PublishAsync(project.Project, token).ConfigureAwait(false);
        return new(backend, reviews, binding, inbox, host, ceiling);
    }

    public static async Task ValidateRestoreAsync(ISessionExecutionBackend backend,
        PinnedShadowProject project, ShadowSessionBinding binding, ShadowInboundJournal inbox, ShadowOutboundHost host, OutboundMode ceiling, CancellationToken token)
    {
        if (ceiling is not (OutboundMode.Shadow or OutboundMode.Enabled) || host.EffectiveMode != ceiling || !inbox.IsBoundTo(binding, project.Project.ProjectRevision))
            throw new InvalidOperationException("Pinned inbox and exact host ceiling are required.");
        if (host.RequiresReconciliation(binding.Session)) throw new InvalidOperationException("Outbound reconciliation is required.");
        if (inbox.RequiresRuntimeRestore)
        {
            var snapshot = await backend.ReadSessionAsync(binding.Session, token).ConfigureAwait(false);
            if (snapshot is null || snapshot.ProjectId != project.Project.ProjectId || snapshot.ProjectVersion != project.Project.ProjectVersion || snapshot.ProjectRevision != project.Project.ProjectRevision)
                throw new InvalidOperationException("Durable context restoration required; no automatic inbox replay.");
            var history = new List<SessionHistoryEntry>(); string? cursor = null;
            do
            {
                var page = await backend.ReadHistoryAsync(binding.Session, new(cursor, 1000), token).ConfigureAwait(false);
                history.AddRange(page.Items); cursor = page.NextCursor;
            } while (cursor is not null);
            inbox.ConfirmDurableRestore(history);
        }
    }

    public async Task<ShadowInboundDecision> AcceptAsync(CanonicalShadowInbound input, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await admissionGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var observed = inbox.Observe(input);
            if (observed.Status != "observed") return observed;
            if (host.EffectiveMode != ceiling || host.RequiresReconciliation(binding.Session))
            {
                inbox.RequireReconciliation(input.Message.ProviderMessageId);
                return new("preserved", "deployment_disarmed_or_unresolved", observed.CanonicalRef);
            }
            SessionAdmission admission;
            try { admission = await backend.ReceiveAsync(binding.Session, ShadowInboundCanonicalizer.Normalize(input), token).ConfigureAwait(false); }
            catch { inbox.RequireReconciliation(input.Message.ProviderMessageId); throw; }
            if (admission.Status != SessionAdmissionStatus.Accepted || admission.AdmissionWatermark is not { } watermark)
            {
                inbox.RequireReconciliation(input.Message.ProviderMessageId);
                return new("preserved", "runtime_handoff_unresolved", observed.CanonicalRef);
            }
            inbox.Admitted(input.Message.ProviderMessageId, watermark);
            return new("admitted", "durable_session_runtime", observed.CanonicalRef);
        }
        finally { admissionGate.Release(); }
    }

    public async Task DrainAsync(CancellationToken token)
    {
        await backend.WaitForIdleAsync(binding.Session, token).ConfigureAwait(false);
        await admissionGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string? cursor = null;
            do
            {
                var page = await backend.ReadOutcomesAsync(binding.Session, new(cursor, 1000), token).ConfigureAwait(false);
                foreach (var outcome in page.Items.Where(o => o.ReassessmentOfMessageId is null))
                    foreach (var id in outcome.ProviderMessageIds) inbox.CompletedOnce(id, outcome.BatchId, outcome.ExecutionId, outcome.Success);
                cursor = page.NextCursor;
            } while (cursor is not null);
        }
        finally { admissionGate.Release(); }
    }
    public Task<SessionSnapshot?> ReadSessionAsync(CancellationToken token) => backend.ReadSessionAsync(binding.Session, token);
    public Task<ArtifactPage<SessionHistoryEntry>> ReadHistoryAsync(PageRequest page, CancellationToken token) => backend.ReadHistoryAsync(binding.Session, page, token);
    public Task<ArtifactPage<SessionFlowOutcome>> ReadOutcomesAsync(PageRequest page, CancellationToken token) => backend.ReadOutcomesAsync(binding.Session, page, token);
    public Task<ArtifactPage<SessionTraceEvent>> ReadTraceAsync(PageRequest page, CancellationToken token) => backend.ReadTraceAsync(binding.Session, page, token);
    public Task<SessionParticipationReviewResult> ReviewAsync(string original, ParticipationReviewDefinition definition, CancellationToken token) => reviews.ReviewAsync(binding.Session, original, definition, token);
    public Task<SessionResponsePreviewResult> PreviewAsync(ArtifactRef reviewRef, CancellationToken token)
    {
        if (ceiling != OutboundMode.Shadow) throw new InvalidOperationException("Shadow preview ceiling required.");
        return reviews.PreviewAsync(binding.Session, reviewRef, token);
    }
    public Task<SessionReviewedResponseResult> ContinueAsync(ArtifactRef reviewRef, CancellationToken token)
    {
        if (ceiling != OutboundMode.Enabled || host.EffectiveMode != ceiling) throw new InvalidOperationException("Effective Enabled host required.");
        return reviews.ContinueAsync(binding.Session, reviewRef, token);
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        await backend.DisposeAsync().ConfigureAwait(false);
        // Client disposal leaves Workflows and pending human requests alive. The worker/store have a separate owner.
    }
}
