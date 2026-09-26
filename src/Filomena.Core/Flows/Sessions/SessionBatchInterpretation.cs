using System.Text.Json;

namespace Filomena.Core.Flows.Sessions;

public sealed partial class SessionFlowRuntime
{
    private InitialInterpretationBinding? initialInterpretationBinding;

    internal void ConfigureInitialInterpretation(SessionKey session, string revision,
        Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>> analyze)
    {
        ArgumentNullException.ThrowIfNull(analyze);
        lock (administrationGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (initialInterpretationBinding is not null || !sessions.IsEmpty || project?.ProjectRevision != revision)
                throw new InvalidOperationException("Initial interpretation must be pinned before session admission.");
            initialInterpretationBinding = new(session, revision, analyze);
        }
    }

    private async Task<IReadOnlyDictionary<string, ParticipationReassessment>> InterpretBatchAsync(FlowBatch batch, CancellationToken token)
    {
        InitialInterpretationBinding? binding;
        lock (administrationGate) binding = initialInterpretationBinding;
        var results = new Dictionary<string, ParticipationReassessment>(StringComparer.Ordinal);
        if (binding is null || binding.Session != batch.Session) return results;
        if (binding.Revision != batch.Project.ProjectRevision) throw new InvalidDataException("Initial interpretation revision mismatch.");
        foreach (var original in batch.Messages)
        {
            token.ThrowIfCancellationRequested();
            // Prefix of the pinned batch context, not whatever arrived while its model calls were in flight.
            var history = batch.History.Where(item => item.Watermark <= original.Watermark)
                .Select(item => new SessionHistoryEntry(item.Watermark, item.Envelope.ProviderMessageId,
                    item.Envelope.Timestamp, item.Envelope.Direction, item.Envelope.EntryKind, item.Envelope.ChannelId,
                    item.Envelope.AccountId, item.Envelope.NativeConversationId, item.Envelope.Message.Clone())).ToArray();
            var target = history.Single(item => item.ProviderMessageId == original.Envelope.ProviderMessageId);
            var snapshot = new SessionSnapshot(batch.Session, batch.Project.ProjectId, batch.Project.ProjectVersion,
                batch.Project.ProjectRevision, batch.StateId, batch.StateVersion, batch.ClosedAt, original.Watermark,
                history.Length, 0, 1);
            var context = new ParticipationReassessmentContext(snapshot, target, history, false);
            var started = timeProvider.GetTimestamp();
            AddTrace(batch.Session, Trace(batch, "initial-interpretation-started"));
            var analysis = await binding.Analyze(context, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            PendingParticipationReviews.ValidateAnalysis(analysis, context);
            results.Add(original.Envelope.ProviderMessageId, analysis with { EvidenceMessageIds = analysis.EvidenceMessageIds.ToArray() });
            AddTrace(batch.Session, Trace(batch, "initial-interpretation-completed",
                duration: timeProvider.GetElapsedTime(started).TotalMilliseconds));
        }
        return results;
    }

    private sealed record InitialInterpretationBinding(SessionKey Session, string Revision,
        Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>> Analyze);

    private sealed class InitialInterpretationCompletion(IFlowCompletion inner, object interpretations) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            var inputs = request.Inputs.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            // Advisory interpretation only; no transport identity, personality fact, or sending authority.
            inputs.Add("interpretacoes_contextuais", JsonSerializer.SerializeToElement(interpretations));
            return inner.CompleteAsync(request with { Inputs = JsonSerializer.SerializeToElement(inputs) }, token);
        }
    }
}
