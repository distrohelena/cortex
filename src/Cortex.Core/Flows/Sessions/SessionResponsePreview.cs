using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Outbound;

namespace Cortex.Core.Flows.Sessions;

public sealed record SessionResponsePreviewResult(ParticipationReviewStatus Status, string Diagnostic,
    string OriginalMessageId, SessionSnapshot? Snapshot, IReadOnlyList<TypedStep> Steps,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Outputs,
    IReadOnlyList<SessionTraceEvent> Trace);

public sealed partial class SessionFlowRuntime
{
    // Only the host-controlled shadow observer can request this. This is not admission or replay:
    // no inbox completion, ordinary outcome, confirmed speech or participation transition is committed.
    internal Task<SessionResponsePreviewResult> PreviewResponseAsync(SessionKey session,
        SessionParticipationReviewResult review, ParticipationInterpretation? interpretation, CancellationToken cancellationToken)
    {
        Task<SessionResponsePreviewResult> Reject(string reason) => Task.FromResult(
            PreviewResult(ParticipationReviewStatus.Rejected, reason, review.OriginalMessageId));
        if (review.Status != ParticipationReviewStatus.Completed || review.Decision != ParticipationReviewDecision.Respond
            || outboundHost?.EffectiveMode != OutboundMode.Shadow) return Reject("shadow-response-review-required");
        lock (administrationGate)
        {
            if (disposed || project is null || !sessions.TryGetValue(session, out var state)) return Reject("session-unavailable");
            lock (state.Gate)
            {
                var context = ReadReassessmentContext(session, review.OriginalMessageId);
                if (context is null || context.HasConfirmedSpeech || context.Snapshot != review.Snapshot)
                    return Reject("preview-context-stale");
                if (state.ParticipationReviewInProgress || state.PendingExecutions != 0 || state.Buffers.Count != 0)
                    return Reject("session-not-idle");
                if (!project.States.TryGetValue(state.CurrentStateId, out var selected)
                    || !selected.Entries.TryGetValue(state.EntryKind, out var flow)) return Reject("current-state-entry-unavailable");
                if (!state.ResponsePreviews.Add(review.OriginalMessageId)) return Reject("preview-already-attempted");
                var original = state.History.Single(item => item.Envelope.ProviderMessageId == review.OriginalMessageId);
                var batch = new FlowBatch(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), session,
                    project, flow, state.CurrentStateId, state.StateVersion, state.EntryKind,
                    flow.Nodes.Values.SingleOrDefault(n => n.Source.Kind == "accumulate")?.Source.Id,
                    [original], state.History.ToArray(), state.AdmissionWatermark, timeProvider.GetUtcNow(), SessionBatchCloseReason.Immediate);
                state.ParticipationReviewInProgress = true;
                var responses = new Dictionary<string, string>(review.CompletedPromptResponses, StringComparer.Ordinal);
                var task = Task.Run(() => RunResponsePreviewAsync(state, batch, context.Snapshot, interpretation, responses, cancellationToken));
                state.Tail = task;
                return task;
            }
        }
    }

    private async Task<SessionResponsePreviewResult> RunResponsePreviewAsync(RuntimeSession state, FlowBatch batch,
        SessionSnapshot snapshot, ParticipationInterpretation? interpretation, IReadOnlyDictionary<string, string> reviewedResponses,
        CancellationToken cancellationToken)
    {
        var originalId = batch.Messages[0].Envelope.ProviderMessageId;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var trace = new List<SessionTraceEvent>();
        void Record(SessionTraceEvent item) { lock (trace) trace.Add(item); }
        void CheckCurrent()
        {
            linked.Token.ThrowIfCancellationRequested();
            lock (administrationGate)
                lock (state.Gate)
                    if (!ReviewIsCurrent(state, batch)) throw new InvalidOperationException("preview-stale");
            if (outboundHost!.EffectiveMode != OutboundMode.Shadow) throw new InvalidOperationException("shadow-preview-disarmed");
        }
        try
        {
            CheckCurrent();
            var outbound = outboundHost!.Begin(batch.Project.Document, batch.ExecutionId, batch.Session, batch.EntryKind,
                [originalId], batch.History.Select(item => item.Envelope.ProviderMessageId), shadowOnly: true);
            var registry = new BoundaryExecutionRegistry(ConversationNamespace(batch.Session), timeProvider);
            var freshCompletion = new ParticipationReviewCompletion(CreateCompletion(batch), CheckCurrent,
                batch.Messages[0].Envelope.Timestamp, batch.ClosedAt, interpretation, participationOnly: false);
            var completion = new ReviewedParticipationCompletion(freshCompletion, reviewedResponses, CheckCurrent,
                id => Record(Trace(batch, "participation-decision-reused", id)));
            var result = await TypedFlowRunner.RunSessionAsync(batch.Flow,
                MessageWithReplyIdentity(batch.Messages[0].Envelope, outbound.SupportsReplySelection), completion,
                RuntimeOutputs(batch, outbound.SupportsReplySelection, participationReview: true), timeProvider,
                node => Record(Trace(batch, "node-" + node.Status, node.NodeId, node.Exit,
                    duration: node.Duration.TotalMilliseconds, errorCode: node.Error, timestamp: node.Timestamp) with
                { MemoryMetadata = node.Metadata }),
                async (node, persons, query, token) =>
                {
                    CheckCurrent();
                    var memories = await QueryMemoryAsync(batch, node, persons, query, registry, token, Record).ConfigureAwait(false);
                    CheckCurrent();
                    return memories;
                }, batch.Project.States[batch.StateId].InstructionContext, null,
                (_, _, _) => { CheckCurrent(); return Task.CompletedTask; }, registry, linked.Token, outbound).ConfigureAwait(false);
            lock (administrationGate)
                lock (state.Gate)
                {
                    CheckCurrent();
                    if (!result.Success || result.ConfirmedSend is not null)
                        return PreviewResult(ParticipationReviewStatus.Failed, "preview-not-completed", originalId, snapshot);
                    return new(ParticipationReviewStatus.Completed, "shadow-response-preview-only", originalId, snapshot,
                        result.Steps.ToArray(), CloneOutputs(result.Outputs), trace.ToArray());
                }
        }
        catch (OperationCanceledException)
        { return PreviewResult(ParticipationReviewStatus.Cancelled, "preview-cancelled", originalId, snapshot); }
        catch (Exception)
        {
            lock (administrationGate)
                lock (state.Gate)
                    return PreviewResult(disposed || linked.IsCancellationRequested ? ParticipationReviewStatus.Cancelled
                        : !ReviewIsCurrent(state, batch) ? ParticipationReviewStatus.Stale : ParticipationReviewStatus.Failed,
                        "preview-not-completed", originalId, snapshot);
        }
        finally { lock (state.Gate) state.ParticipationReviewInProgress = false; }
    }

    private static SessionResponsePreviewResult PreviewResult(ParticipationReviewStatus status,
        string diagnostic, string id, SessionSnapshot? snapshot = null) => new(status, diagnostic, id,
            snapshot, [], new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(), []);

    private sealed class ReviewedParticipationCompletion(IFlowCompletion inner, IReadOnlyDictionary<string, string> responses,
        Action checkCurrent, Action<string> reused) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            checkCurrent();
            if (!responses.TryGetValue(request.NodeId, out var response)) return inner.CompleteAsync(request, token);
            // Only completed prompts from the validated side-effect-free participation cut exist here.
            // The normal runner still validates their schema and executes branches; generation is never cached.
            reused(request.NodeId);
            return Task.FromResult(response);
        }
    }
}
