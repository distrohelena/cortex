using System.Text.Json;

namespace Filomena.Core.Flows.Sessions;

public enum ParticipationReviewDecision { Respond, Wait, Silence }
public enum ParticipationReviewStatus { Completed, Rejected, Stale, Failed, Cancelled }

// Host-selected cut of a pinned graph, never model input or send authorization.
public sealed record ParticipationReviewBoundary(string NodeId, string Exit, ParticipationReviewDecision Decision);
public sealed record ParticipationReviewDefinition(string ProjectRevision,
    IReadOnlyList<string> PromptNodeIds, IReadOnlyList<ParticipationReviewBoundary> Boundaries);

public sealed record SessionParticipationReviewResult(
    ParticipationReviewStatus Status, string Diagnostic, string OriginalMessageId,
    SessionSnapshot? Snapshot, ParticipationReviewDecision? Decision,
    IReadOnlyList<TypedStep> Steps,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Outputs);

public sealed partial class SessionFlowRuntime
{
    /// <summary>
    /// Evaluates a host-approved participation cut against retained original inbound and current context.
    /// A completed decision is diagnostic only, never permission to send or to reopen handled work.
    /// </summary>
    public Task<SessionParticipationReviewResult> ReviewParticipationAsync(SessionKey session,
        string originalMessageId, ParticipationReviewDefinition definition, CancellationToken cancellationToken = default)
    {
        Task<SessionParticipationReviewResult> Reject(string diagnostic) => Task.FromResult(
            ReviewResult(ParticipationReviewStatus.Rejected, diagnostic, originalMessageId));
        if (string.IsNullOrWhiteSpace(originalMessageId)) return Reject("original-message-required");
        lock (administrationGate)
        {
            if (disposed) return Reject("runtime-disposed");
            if (project is null || definition.ProjectRevision != project.ProjectRevision)
                return Reject("review-project-revision-mismatch");
            if (!sessions.TryGetValue(session, out var state)) return Reject("session-unavailable");
            lock (state.Gate)
            {
                if (state.ParticipationReviewInProgress) return Reject("review-in-progress");
                if (state.PendingExecutions != 0 || state.Buffers.Count != 0) return Reject("session-not-idle");
                var original = state.History.SingleOrDefault(item => item.Envelope.ProviderMessageId == originalMessageId);
                if (original is null || original.Envelope.Direction != SessionMessageDirection.Inbound)
                    return Reject("retained-inbound-unavailable");
                if (!project.States.TryGetValue(state.CurrentStateId, out var selected)
                    || !selected.Entries.TryGetValue(state.EntryKind, out var flow))
                    return Reject("current-state-entry-unavailable");
                ParticipationReviewCut cut;
                try { cut = ParticipationReviewCut.Create(flow, definition); }
                catch (InvalidDataException) { return Reject("unsafe-participation-review-cut"); }
                var snapshot = ReadSessionCore(session)! with
                {
                    ProjectId = project.ProjectId,
                    ProjectVersion = project.ProjectVersion,
                    ProjectRevision = project.ProjectRevision
                };
                var batch = new FlowBatch(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"),
                    session, project, cut.Flow, state.CurrentStateId, state.StateVersion, state.EntryKind,
                    cut.Flow.Nodes.Values.SingleOrDefault(n => n.Source.Kind == "accumulate")?.Source.Id,
                    [original], state.History.ToArray(), state.AdmissionWatermark, timeProvider.GetUtcNow(),
                    SessionBatchCloseReason.Immediate);
                state.ParticipationReviewInProgress = true;
                // Shares the normal session scheduling frontier, not admission, outcomes or state mutation.
                var task = Task.Run(() => RunParticipationReviewAsync(state, batch, cut, snapshot, cancellationToken));
                state.Tail = task;
                return task;
            }
        }
    }

    private async Task<SessionParticipationReviewResult> RunParticipationReviewAsync(RuntimeSession state,
        FlowBatch batch, ParticipationReviewCut cut, SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var originalId = batch.Messages[0].Envelope.ProviderMessageId;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        void CheckCurrent()
        {
            linked.Token.ThrowIfCancellationRequested();
            lock (administrationGate)
                lock (state.Gate)
                    if (!ReviewIsCurrent(state, batch)) throw new InvalidOperationException("review-stale");
        }
        try
        {
            CheckCurrent();
            var completion = new ParticipationReviewCompletion(completionFactory(batch.Session), CheckCurrent,
                batch.Messages[0].Envelope.Timestamp, batch.ClosedAt);
            var result = await TypedFlowRunner.RunSessionAsync(cut.Flow, batch.Messages[0].Envelope.Message,
                completion, RuntimeOutputs(batch, false, participationReview: true), timeProvider, _ => { },
                (_, _, _, _) => throw new InvalidOperationException("Memory is outside the review boundary."),
                batch.Project.States[batch.StateId].InstructionContext, null,
                (_, _, _) => { CheckCurrent(); return Task.CompletedTask; },
                new Boundaries.BoundaryExecutionRegistry("", timeProvider), linked.Token).ConfigureAwait(false);
            lock (administrationGate)
                lock (state.Gate)
                {
                    if (disposed || linked.IsCancellationRequested)
                        return ReviewResult(ParticipationReviewStatus.Cancelled, "review-cancelled", originalId, snapshot);
                    if (!ReviewIsCurrent(state, batch))
                        return ReviewResult(ParticipationReviewStatus.Stale, "review-stale", originalId, snapshot);
                    var last = result.Steps.LastOrDefault();
                    if (!result.Success || result.TargetStateId is not null || result.ConfirmedSend is not null
                        || last?.Exit is null || !cut.Decisions.TryGetValue(new(last.NodeId, last.Exit), out var decision))
                        return ReviewResult(ParticipationReviewStatus.Failed, "review-decision-unavailable", originalId, snapshot);
                    return new(ParticipationReviewStatus.Completed, "participation-review-only", originalId,
                        snapshot, decision, result.Steps.ToArray(), CloneOutputs(result.Outputs));
                }
        }
        catch (OperationCanceledException)
        { return ReviewResult(ParticipationReviewStatus.Cancelled, "review-cancelled", originalId, snapshot); }
        catch (Exception)
        {
            lock (administrationGate)
                lock (state.Gate)
                    return ReviewResult(disposed || linked.IsCancellationRequested ? ParticipationReviewStatus.Cancelled
                        : !ReviewIsCurrent(state, batch) ? ParticipationReviewStatus.Stale : ParticipationReviewStatus.Failed,
                        "review-not-completed", originalId, snapshot);
        }
        finally { lock (state.Gate) state.ParticipationReviewInProgress = false; }
    }

    // Call only with administrationGate and state.Gate held, in that order.
    private bool ReviewIsCurrent(RuntimeSession state, FlowBatch batch) => !disposed
        && ReferenceEquals(project, batch.Project) && state.AdmissionWatermark == batch.HistoryWatermark
        && state.StateVersion == batch.StateVersion && state.CurrentStateId == batch.StateId;

    private static SessionParticipationReviewResult ReviewResult(ParticipationReviewStatus status,
        string diagnostic, string id, SessionSnapshot? snapshot = null) => new(status, diagnostic, id,
            snapshot, null, [], new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>());

    private sealed class ParticipationReviewCompletion(IFlowCompletion inner, Action checkCurrent,
        DateTimeOffset originalAt, DateTimeOffset contextAt, ParticipationInterpretation? interpretation = null) : IFlowCompletion
    {
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            checkCurrent();
            var inputs = request.Inputs.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            // Host metadata, not a fabricated new inbound or an instruction appended to the frozen prompt.
            inputs.Add("reavaliacao", JsonSerializer.SerializeToElement(new
            {
                tipo = "mensagem_original_retida",
                contexto_pode_ser_posterior = true,
                mensagem_original_em = originalAt,
                contexto_capturado_em = contextAt,
                interpretacao_contextual = interpretation,
                autoriza_envio = false
            }));
            var output = await inner.CompleteAsync(request with { Inputs = JsonSerializer.SerializeToElement(inputs) }, token).ConfigureAwait(false);
            checkCurrent();
            return output;
        }
    }
}
