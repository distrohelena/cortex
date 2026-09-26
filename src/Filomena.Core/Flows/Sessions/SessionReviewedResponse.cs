using System.Runtime.CompilerServices;
using Filomena.Core.Outbound;

[assembly: InternalsVisibleTo("Filomena.Llm.Tests")]

namespace Filomena.Core.Flows.Sessions;

public sealed record SessionReviewedResponseResult(ParticipationReviewStatus Status, string Diagnostic, SessionFlowOutcome? Outcome = null);

public sealed partial class SessionFlowRuntime
{
    internal Task<SessionReviewedResponseResult> ContinueReviewedResponseAsync(SessionKey session,
        SessionParticipationReviewResult review, ParticipationInterpretation? interpretation, CancellationToken token)
    {
        Task<SessionReviewedResponseResult> Reject(string reason) => Task.FromResult(
            new SessionReviewedResponseResult(ParticipationReviewStatus.Rejected, reason));
        if (review.Status != ParticipationReviewStatus.Completed || review.Decision != ParticipationReviewDecision.Respond
            || review.CompletedPromptResponses.Count == 0 || outboundHost?.EffectiveMode != OutboundMode.Enabled)
            return Reject("enabled-runtime-review-required");
        lock (administrationGate)
        {
            if (disposed || project is null || !sessions.TryGetValue(session, out var state)) return Reject("session-unavailable");
            lock (state.Gate)
            {
                var context = ReadReassessmentContext(session, review.OriginalMessageId);
                if (context is null || context.HasConfirmedSpeech || context.Snapshot != review.Snapshot)
                    return Reject("reviewed-response-context-stale");
                if (state.ParticipationReviewInProgress || state.PendingExecutions != 0 || state.Buffers.Count != 0)
                    return Reject("session-not-idle");
                if (!project.States.TryGetValue(state.CurrentStateId, out var selected)
                    || !selected.Entries.TryGetValue(state.EntryKind, out var flow)) return Reject("current-state-entry-unavailable");
                if (!state.ResponsePreviews.Add(review.OriginalMessageId)) return Reject("response-already-attempted");
                var original = state.History.Single(item => item.Envelope.ProviderMessageId == review.OriginalMessageId);
                var batch = new FlowBatch(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), session,
                    project, flow, state.CurrentStateId, state.StateVersion, state.EntryKind,
                    flow.Nodes.Values.SingleOrDefault(n => n.Source.Kind == "accumulate")?.Source.Id,
                    [original], state.History.ToArray(), state.AdmissionWatermark, timeProvider.GetUtcNow(), SessionBatchCloseReason.Immediate)
                {
                    ReviewedResponse = new(new Dictionary<string, string>(review.CompletedPromptResponses, StringComparer.Ordinal), interpretation, token)
                };
                state.ParticipationReviewInProgress = true;
                state.PendingExecutions++;
                var task = Task.Run(() => RunReviewedResponseAsync(state, batch));
                state.Tail = task;
                return task;
            }
        }
    }

    private async Task<SessionReviewedResponseResult> RunReviewedResponseAsync(RuntimeSession state, FlowBatch batch)
    {
        try
        {
            AddTrace(batch.Session, Trace(batch, "reviewed-response-started"));
            try { await ExecuteBatchAsync(batch).ConfigureAwait(false); }
            catch (Exception error) { RecordUnexpectedBatchFailure(batch, error); }
            lock (state.Gate)
            {
                var outcome = state.Outcomes.Single(item => item.ExecutionId == batch.ExecutionId);
                return new(outcome.Success ? ParticipationReviewStatus.Completed : ParticipationReviewStatus.Failed,
                    outcome.Error ?? "reviewed-response-completed", CloneOutcome(outcome));
            }
        }
        finally { lock (state.Gate) state.ParticipationReviewInProgress = false; }
    }

    private void CheckReviewedResponse(FlowBatch batch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (batch.ReviewedResponse is null) return;
        lock (administrationGate)
        {
            if (!sessions.TryGetValue(batch.Session, out var state)) throw new InvalidOperationException("reviewed-response-session-unavailable");
            lock (state.Gate)
                if (!ReviewIsCurrent(state, batch)) throw new InvalidOperationException("reviewed-response-stale");
        }
        if (outboundHost?.EffectiveMode != OutboundMode.Enabled) throw new InvalidOperationException("reviewed-response-disarmed");
    }

    private sealed record ReviewedResponseExecution(IReadOnlyDictionary<string, string> Responses,
        ParticipationInterpretation? Interpretation, CancellationToken Cancellation);
}
