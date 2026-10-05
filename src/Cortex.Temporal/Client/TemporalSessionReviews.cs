using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Temporal.Workflows;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Cortex.Temporal.Client;

public sealed class TemporalSessionReviews(ISessionExecutionBackend backend, FileExecutionArtifactStore store,
    DurableSessionArtifacts artifacts, ITemporalClient client, string installationId, SessionReviewPolicy? policy = null) : ISessionReviewBackend
{
    public Task<SessionParticipationReviewResult> ReviewAsync(SessionKey session, string originalMessageId, ParticipationReviewDefinition definition, CancellationToken token)
        => ReviewCore(session, originalMessageId, definition, false, null, token);
    public Task<SessionParticipationReviewResult> ReassessAsync(SessionKey session, string originalMessageId, ParticipationReviewDefinition definition, SessionSnapshot expectedSnapshot, CancellationToken token)
        => ReviewCore(session, originalMessageId, definition, true, expectedSnapshot, token);
    private async Task<SessionParticipationReviewResult> ReviewCore(SessionKey session, string originalMessageId, ParticipationReviewDefinition definition, bool reassess, SessionSnapshot? expected, CancellationToken token)
    {
        try
        {
            var reference = await Execute(session, originalMessageId, SessionExecutionPurpose.ParticipationReview, definition, null, token, reassess, expected).ConfigureAwait(false);
            return await artifacts.ReadParticipationReviewAsync(reference, token).ConfigureAwait(false);
        }
        catch (WorkflowUpdateFailedException) { return new(ParticipationReviewStatus.Rejected, "review-not-completed", originalMessageId, null, null, [], new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>()); }
    }
    public async Task<SessionResponsePreviewResult> PreviewAsync(SessionKey session, ArtifactRef reviewRef, CancellationToken token)
    {
        CheckScope(session, reviewRef);
        var review = await artifacts.ReadParticipationReviewAsync(reviewRef, token).ConfigureAwait(false);
        var current = await backend.ReadSessionAsync(session, token).ConfigureAwait(false);
        if (review.Snapshot is null || current is null || !DurableSessionArtifacts.ReviewContextMatches(review.Snapshot, current))
            return new(ParticipationReviewStatus.Rejected, "preview-context-stale", review.OriginalMessageId, current, [], new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(), []);
        try
        {
            var reference = await Execute(session, review.OriginalMessageId, SessionExecutionPurpose.ResponsePreview, null, reviewRef, token).ConfigureAwait(false);
            return await artifacts.ReadResponsePreviewAsync(reference, token).ConfigureAwait(false);
        }
        catch (WorkflowUpdateFailedException) { return new(ParticipationReviewStatus.Rejected, "preview-not-completed", review.OriginalMessageId, current, [], new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(), []); }
    }
    public async Task<SessionReviewedResponseResult> ContinueAsync(SessionKey session, ArtifactRef reviewRef, CancellationToken token)
    {
        CheckScope(session, reviewRef);
        var review = await artifacts.ReadParticipationReviewAsync(reviewRef, token).ConfigureAwait(false);
        var current = await backend.ReadSessionAsync(session, token).ConfigureAwait(false);
        if (review.Snapshot is null || current is null || !DurableSessionArtifacts.ReviewContextMatches(review.Snapshot, current))
            return new(ParticipationReviewStatus.Rejected, "reviewed-response-context-stale");
        try
        {
            var reference = await Execute(session, review.OriginalMessageId, SessionExecutionPurpose.ReviewedResponse, null, reviewRef, token).ConfigureAwait(false);
            return await artifacts.ReadReviewedResponseAsync(reference, token).ConfigureAwait(false);
        }
        catch (WorkflowUpdateFailedException) { return new(ParticipationReviewStatus.Rejected, "enabled-runtime-review-required"); }
    }
    private void CheckScope(SessionKey session, ArtifactRef reference)
    {
        if (reference.Scope.InstallationId != installationId || reference.Scope.Session != session) throw new InvalidDataException("Foreign review scope.");
    }
    private async Task<ArtifactRef> Execute(SessionKey session, string originalMessageId, SessionExecutionPurpose purpose,
        ParticipationReviewDefinition? definition, ArtifactRef? reviewRef, CancellationToken token, bool reassess = false, SessionSnapshot? expectedSnapshot = null)
    {
        var snapshot = expectedSnapshot ?? await backend.ReadSessionAsync(session, token).ConfigureAwait(false) ?? throw new InvalidOperationException("session-unavailable");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { installationId, session, snapshot.ProjectRevision, snapshot.AdmissionWatermark, snapshot.StateVersion, originalMessageId, purpose, definition, reviewRef, reassess });
        var operationId = "cortex-review-v1-" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        var scope = new ArtifactScope(installationId, session, operationId, snapshot.ProjectRevision);
        var request = new SessionReviewOperationV1(operationId, originalMessageId, purpose, definition, reviewRef) { Policy = policy ?? new(), Reassess = reassess, ExpectedSnapshot = expectedSnapshot };
        var requestRef = await store.PutOnceAsync(scope, "review-operation", JsonSerializer.SerializeToElement(request), token).ConfigureAwait(false);
        var executionScope = scope with { ExecutionId = DurableIdentity.ExecutionId(installationId, session, operationId, "review-" + purpose) };
        var completed = await store.FindAsync(executionScope, "committed-review", token).ConfigureAwait(false);
        if (completed is not null)
            return ((await store.ReadAsync(completed, token).ConfigureAwait(false)).Deserialize<CommittedSessionReview>() ?? throw new InvalidDataException("Missing review receipt.")).ResultRef;
        return await client.GetWorkflowHandle<SessionWorkflowV1>(DurableIdentity.SessionWorkflowId(installationId, session))
            .ExecuteUpdateAsync(w => w.ReviewAsync(requestRef, operationId), new() { Rpc = new() { CancellationToken = token } }).ConfigureAwait(false);
    }
}
