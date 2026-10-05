using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Sessions;

public interface ISessionExecutionBackend : IAsyncDisposable
{
    Task<SessionFlowProjectSnapshot> PublishAsync(SessionFlowProject project, CancellationToken token);
    Task<SessionAdmission> ReceiveAsync(SessionKey session, SessionMessageEnvelope message, CancellationToken token);
    Task<SessionSnapshot?> ReadSessionAsync(SessionKey session, CancellationToken token);
    Task<ArtifactPage<SessionHistoryEntry>> ReadHistoryAsync(SessionKey session, PageRequest page, CancellationToken token);
    Task<ArtifactPage<SessionFlowOutcome>> ReadOutcomesAsync(SessionKey session, PageRequest page, CancellationToken token);
    Task<ArtifactPage<SessionTraceEvent>> ReadTraceAsync(SessionKey session, PageRequest page, CancellationToken token);
    Task WaitForIdleAsync(SessionKey session, CancellationToken token);
    Task CancelExecutionAsync(SessionKey session, string executionId, CancellationToken token);
    Task<ArtifactPage<FeedbackDescriptor>> ReadFeedbackAsync(SessionKey session, PageRequest page, CancellationToken token);
    Task<FeedbackReceipt> SubmitFeedbackAsync(FeedbackSubmission submission, CancellationToken token);
    Task<SessionSnapshot> ImportIdleAsync(SessionImportRequest request, CancellationToken token);
}
