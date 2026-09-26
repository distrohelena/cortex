namespace Filomena.Core.Flows.Sessions;

public enum ReassessmentOpportunity { Uncertain, Relevant, Obsolete }
public sealed record ParticipationReassessmentContext(SessionSnapshot Snapshot,
    SessionHistoryEntry Original, IReadOnlyList<SessionHistoryEntry> History, bool HasConfirmedSpeech);
public sealed record ParticipationReassessment(bool Confident, ReassessmentOpportunity Opportunity,
    IReadOnlyList<string> EvidenceMessageIds)
{
    public ParticipationInterpretation? Interpretation { get; init; }
}
public sealed record ParticipationInterpretation(string Reading, string? Alternative, string Confidence, string Explanation);
public sealed record PendingParticipationReview(string OriginalMessageId, string Status,
    SessionParticipationReviewResult? Review = null)
{
    // Experimental/private report data. Not a permanent participant profile or an instruction.
    public ParticipationReassessment? Assessment { get; init; }
}

/// <summary>Host-owned, process-local observation controller. No admission or sending authority.</summary>
public sealed class PendingParticipationReviews
{
    private readonly SessionFlowRuntime runtime;
    private readonly SessionKey session;
    private readonly ParticipationReviewDefinition definition;
    private readonly int messageHorizon, attemptBudget, capacity;
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private int running;

    public PendingParticipationReviews(SessionFlowRuntime runtime, SessionKey session,
        ParticipationReviewDefinition definition, int messageHorizon, int attemptBudget, int capacity)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(definition);
        if (messageHorizon < 1 || attemptBudget < 1 || capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(messageHorizon));
        this.runtime = runtime;
        this.session = session;
        this.definition = definition with { PromptNodeIds = definition.PromptNodeIds.ToArray(), Boundaries = definition.Boundaries.ToArray() };
        this.messageHorizon = messageHorizon;
        this.attemptBudget = attemptBudget;
        this.capacity = capacity;
    }

    // Registration is an explicit host decision after an uncertain reading, never automatic history recovery.
    public bool Track(string originalMessageId)
    {
        if (string.IsNullOrWhiteSpace(originalMessageId)) return false;
        lock (gate)
        {
            var context = runtime.ReadReassessmentContext(session, originalMessageId);
            if (context is null || context.HasConfirmedSpeech || context.Snapshot.ProjectRevision != definition.ProjectRevision
                || entries.Count >= capacity || entries.ContainsKey(originalMessageId)) return false;
            entries.Add(originalMessageId, new(context.Snapshot.AdmissionWatermark));
            return true;
        }
    }

    public void Close(string originalMessageId)
    {
        lock (gate)
            if (entries.TryGetValue(originalMessageId, out var entry)) entry.Closed = true;
    }

    // No timer, listener or background task. The host invokes this after its normal batch drain.
    public async Task<IReadOnlyList<PendingParticipationReview>> ReviewPendingAsync(
        Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>> analyze,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(analyze);
        token.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref running, 1) != 0) return [];
        try
        {
            KeyValuePair<string, Entry>[] pending;
            lock (gate) pending = entries.Where(p => !p.Value.Closed).ToArray();
            var results = new List<PendingParticipationReview>();
            foreach (var (id, entry) in pending)
            {
                token.ThrowIfCancellationRequested();
                ParticipationReassessmentContext? context;
                lock (gate)
                {
                    if (entry.Closed) continue;
                    context = runtime.ReadReassessmentContext(session, id);
                    string? closed = context is null ? "context_unavailable"
                        : context.HasConfirmedSpeech ? "already_answered"
                        : context.Snapshot.ProjectRevision != definition.ProjectRevision ? "stale"
                        : context.Snapshot.AdmissionWatermark - context.Original.AdmissionWatermark >= messageHorizon ? "distance_exceeded"
                        : entry.Attempts >= attemptBudget && context.Snapshot.AdmissionWatermark > entry.LastWatermark ? "budget_exhausted" : null;
                    if (closed is not null)
                    {
                        entry.Closed = true;
                        results.Add(new(id, closed));
                        continue;
                    }
                    if (context!.Snapshot.OpenBufferCount != 0 || context.Snapshot.PendingExecutionCount != 0
                        || context.Snapshot.AdmissionWatermark <= entry.LastWatermark) continue;
                    entry.LastWatermark = context.Snapshot.AdmissionWatermark;
                    entry.Attempts++;
                }
                ParticipationReassessment analysis;
                try
                {
                    analysis = await analyze(context!, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    var evidence = context!.History.Select(m => m.ProviderMessageId).ToHashSet(StringComparer.Ordinal);
                    if (analysis is null || !Enum.IsDefined(analysis.Opportunity) || analysis.EvidenceMessageIds is null
                        || analysis.EvidenceMessageIds.Any(id => !evidence.Contains(id)))
                        throw new InvalidDataException("Invalid reassessment evidence.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    results.Add(new(id, "analysis_failed"));
                    continue;
                }
                lock (gate)
                {
                    if (Invalidated(id, entry, context!.Snapshot) is { } invalid)
                    {
                        results.Add(new(id, invalid));
                        continue;
                    }
                    if (analysis.Opportunity == ReassessmentOpportunity.Obsolete)
                    {
                        entry.Closed = true;
                        results.Add(new(id, "obsolete") { Assessment = analysis });
                        continue;
                    }
                    if (!analysis.Confident || analysis.Opportunity != ReassessmentOpportunity.Relevant)
                    {
                        results.Add(new(id, "pending") { Assessment = analysis });
                        continue;
                    }
                }
                var review = await runtime.ReviewParticipationAsync(session, id, definition, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    var invalid = Invalidated(id, entry, context!.Snapshot);
                    if (invalid is not null) results.Add(new(id, invalid));
                    else if (review.Status == ParticipationReviewStatus.Completed && review.Snapshot == context.Snapshot)
                    {
                        entry.Closed = true;
                        results.Add(new(id, "reviewed", review) { Assessment = analysis });
                    }
                    else results.Add(new(id, review.Status == ParticipationReviewStatus.Stale || review.Snapshot != context.Snapshot
                        ? "stale" : "review_failed"));
                }
            }
            return results;
        }
        finally { Volatile.Write(ref running, 0); }
    }

    private string? Invalidated(string id, Entry entry, SessionSnapshot expected)
    {
        if (entry.Closed) return "closed";
        var current = runtime.ReadReassessmentContext(session, id);
        if (current?.HasConfirmedSpeech == true) { entry.Closed = true; return "already_answered"; }
        if (current is null || current.Snapshot != expected) return "stale";
        return null;
    }

    // Closed entries remain tombstones within the bounded run; repeated registration cannot reopen them.
    private sealed class Entry(long watermark)
    {
        internal long LastWatermark = watermark;
        internal int Attempts;
        internal bool Closed;
    }
}

public sealed partial class SessionFlowRuntime
{
    internal ParticipationReassessmentContext? ReadReassessmentContext(SessionKey session, string originalId)
    {
        // Absence of committed speech is not proof of non-delivery after an uncertain attempt.
        if (outboundHost?.RequiresReconciliation(session) == true) return null;
        lock (administrationGate)
        {
            if (disposed || project is null || !sessions.TryGetValue(session, out var state)) return null;
            lock (state.Gate)
            {
                var history = ReadHistoryCore(session);
                var original = history.SingleOrDefault(m => m.ProviderMessageId == originalId);
                if (original is null || original.Direction != SessionMessageDirection.Inbound) return null;
                var executions = state.Outcomes.Where(o => o.ProviderMessageIds.Contains(originalId)).Select(o => o.ExecutionId).ToHashSet();
                var answered = state.Trace.Any(t => t.EventName == "speech-committed" && executions.Contains(t.ExecutionId));
                var snapshot = ReadSessionCore(session)! with
                {
                    ProjectId = project.ProjectId,
                    ProjectVersion = project.ProjectVersion,
                    ProjectRevision = project.ProjectRevision
                };
                return new(snapshot, original, history, answered);
            }
        }
    }
}
