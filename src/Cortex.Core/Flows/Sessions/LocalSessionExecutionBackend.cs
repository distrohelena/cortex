using System.Text.Json;
using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Sessions;

/// <summary>Compatibility harness. Durable human waits belong to the Temporal backend.</summary>
public sealed class LocalSessionExecutionBackend(SessionFlowRuntime runtime) : ISessionExecutionBackend
{
    private readonly SemaphoreSlim administration = new(1, 1);
    private SessionFlowProject? published;
    private SessionFlowProjectSnapshot? publishedSnapshot;
    public async Task<SessionFlowProjectSnapshot> PublishAsync(SessionFlowProject project, CancellationToken token)
    {
        await administration.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (published is not null && published.ProjectId == project.ProjectId && published.ProjectVersion == project.ProjectVersion && published.ProjectRevision == project.ProjectRevision)
            {
                if (!JsonElement.DeepEquals(published.Document, project.Document)) throw new InvalidDataException("Conflicting project publication.");
                return publishedSnapshot!;
            }
            publishedSnapshot = runtime.UpdateProject(project);
            published = project with { Document = project.Document.Clone() };
            return publishedSnapshot;
        }
        finally { administration.Release(); }
    }
    public async Task<SessionAdmission> ReceiveAsync(SessionKey session, SessionMessageEnvelope message, CancellationToken token) =>
        await runtime.ReceiveAsync(session, message, token).ConfigureAwait(false);
    public Task<SessionSnapshot?> ReadSessionAsync(SessionKey session, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(runtime.ReadSession(session)); }
    public Task<ArtifactPage<SessionHistoryEntry>> ReadHistoryAsync(SessionKey session, PageRequest page, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(LocalBackendPaging.Page(session, "history", runtime.ReadHistory(session), page, x => x.ProviderMessageId)); }
    public Task<ArtifactPage<SessionFlowOutcome>> ReadOutcomesAsync(SessionKey session, PageRequest page, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(LocalBackendPaging.Page(session, "outcomes", runtime.ReadOutcomes(session), page, x => x.ExecutionId)); }
    public Task<ArtifactPage<SessionTraceEvent>> ReadTraceAsync(SessionKey session, PageRequest page, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(LocalBackendPaging.Page(session, "traces", runtime.ReadTrace(session), page, x => x.ExecutionId + ":" + x.EventName + ":" + x.Timestamp.ToString("O"))); }
    public Task WaitForIdleAsync(SessionKey session, CancellationToken token) => runtime.WaitForIdleAsync(session, token);
    public Task CancelExecutionAsync(SessionKey session, string executionId, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromException(new NotSupportedException("The local harness does not expose per-execution cancellation.")); }
    public Task<ArtifactPage<FeedbackDescriptor>> ReadFeedbackAsync(SessionKey session, PageRequest page, CancellationToken token)
    { token.ThrowIfCancellationRequested(); page.Validate(); return Task.FromResult(new ArtifactPage<FeedbackDescriptor>([], null)); }
    public Task<FeedbackReceipt> SubmitFeedbackAsync(FeedbackSubmission submission, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(new FeedbackReceipt(submission.OperationId, submission.RequestId, "rejected", "human-feedback-backend-required")); }
    public async Task<SessionSnapshot> ImportIdleAsync(SessionImportRequest request, CancellationToken token)
    {
        await administration.WaitAsync(token).ConfigureAwait(false);
        try
        {
            DurableIdentity.Require(request.CutoverId); DurableIdentity.Require(request.InstallationId);
            var context = new SessionRuntimeContext(request.Snapshot, request.History, request.SeenProviderIds);
            var existing = runtime.CaptureContext(request.Snapshot.Session);
            if (existing is not null)
            {
                if (JsonSerializer.Serialize(existing) != JsonSerializer.Serialize(context)) throw new InvalidDataException("Cannot overwrite a divergent session.");
                return existing.Snapshot;
            }
            var kind = request.History.Count > 0 ? request.History[0].EntryKind
                : request.Snapshot.Session.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal) ? SessionEntryKind.Group : SessionEntryKind.Dm;
            runtime.RestoreContext(request.Snapshot.Session, context, kind);
            return runtime.ReadSession(request.Snapshot.Session)!;
        }
        finally { administration.Release(); }
    }
    public ValueTask DisposeAsync() => runtime.DisposeAsync();
}
