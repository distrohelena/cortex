using System.Diagnostics;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;

namespace Cortex.Core.Shadow;

public sealed record ObservedShadowInteraction(string RunId, double DurationMilliseconds,
    IReadOnlyList<ShadowInboundDecision> Decisions, IReadOnlyList<SessionFlowOutcome> Outcomes,
    IReadOnlyList<SessionTraceEvent> Trace)
{
    public IReadOnlyList<PendingParticipationReview> ParticipationReviews { get; init; } = [];
    public IReadOnlyList<PendingParticipationReview> InitialInterpretations { get; init; } = [];
    public bool FailuresHandled { get; init; }
}

public sealed class ObservedShadowRun : IAsyncDisposable
{
    private readonly long initialTail;
    private readonly ShadowSessionBinding binding;
    private readonly CompiledSessionFlowProject project;
    private readonly string epoch;
    private readonly ShadowInboundJournal inbox;
    private readonly ShadowOutboundHost host;
    private readonly RuntimeHandoffCore handoff;
    private readonly LiveSessionContextStore? contextStore;
    private readonly OutboundMode ceiling;
    private readonly int liveInteractionLimit;
    private readonly bool continuous;
    private int liveInteractions;
    private readonly SemaphoreSlim serial = new(1, 1);
    private long cursor;
    private readonly Dictionary<long, string> observedSequences = new();
    private int stopped;
    private int disposed;
    private PendingParticipationReviews? participationObserver;
    private Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>>? reassess;
    private Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>>? initialInterpretation;
    private readonly CancellationTokenSource participationLifetime = new();
    private bool previewResponses;
    private bool continueResponses;

    public ObservedShadowRun(PinnedShadowProject project, ShadowSessionBinding binding, string journalDirectory,
        OutboundBindings roles, IFlowCompletion completion, IMemoryReader memory, string nativeEpoch, long initialTail)
        : this(project, binding, journalDirectory, roles, completion, memory, nativeEpoch, initialTail, null) { }

    // Live construction is internal and reachable only through the separate, bounded host wrapper.
    internal ObservedShadowRun(PinnedShadowProject project, ShadowSessionBinding binding, string journalDirectory,
        OutboundBindings roles, IFlowCompletion completion, IMemoryReader memory, string nativeEpoch, long initialTail,
        ObservedLiveWindow? live)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(memory);
        this.binding = binding;
        this.project = SessionFlowProjectCompiler.Compile(project.Project);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeEpoch);
        if (initialTail < 0) throw new ArgumentOutOfRangeException(nameof(initialTail));
        epoch = nativeEpoch;
        this.initialTail = cursor = initialTail;

        ceiling = live is null ? OutboundMode.Shadow : OutboundMode.Enabled;
        liveInteractionLimit = live?.MaxInteractions ?? 0;
        continuous = live?.Continuous == true;
        var prefix = live is null ? "observation-shadow" : "observation-live";
        host = new(Path.Combine(journalDirectory, prefix + "-outbound.jsonl"), project.Project.Document,
            roles, [binding.Session], live?.Capabilities ?? TransportSafetyCapabilities.Unproven);
        try
        {
            // The outbound owner holds the exclusive journal lease while fingerprints
            // and context are checked, before a new inbox boundary is appended.
            contextStore = live is null ? null : new(journalDirectory, binding, project);
            var context = contextStore?.Load();
            if (live is not null)
            {
                if (live.Route.Session != binding.Session || live.Route.Speaker.StableId != binding.SelfActorIds.Single())
                    throw new InvalidOperationException("Live route binding mismatch.");
                host.BindLiveRoute(live.Route, live.Transport);
            }
            host.Rearm(ceiling);
            inbox = ShadowInboundJournal.StartNonResumableRun(
                Path.Combine(journalDirectory, prefix + "-inbound.jsonl"), binding,
                project.Project.ProjectRevision, epoch, initialTail);
            RuntimeHandoffCore? created = null;
            try
            {
                handoff = created = live is null
                    ? new ShadowRuntimeHandoff(project, binding, inbox, host, completion, memory)
                    : new LiveRuntimeHandoff(project, binding, inbox, host, completion, memory, live.Participation);
                if (context is not null) handoff.RestoreContext(context);
                if (live?.Participation is { } participation)
                {
                    participationObserver = handoff.CreateParticipationObserver(participation.Definition,
                        participation.MessageHorizon, participation.AttemptBudget, participation.Capacity);
                    reassess = participation.Reassess;
                    initialInterpretation = participation.AnalyzeInitial;
                    continueResponses = participation.RespondToReassessedMessages;
                }
                contextStore?.Save(handoff.CaptureContext());
            }
            catch
            {
                try { created?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                finally { inbox.Dispose(); }
                throw;
            }
        }
        catch { host.Dispose(); throw; }
    }

    public string RunId => inbox.ShadowRunId!;
    public IReadOnlyList<SessionFlowOutcome> Outcomes => handoff.Outcomes;
    public IReadOnlyList<SessionHistoryEntry> History => handoff.History;
    public IReadOnlyList<SessionTraceEvent> Trace => handoff.Trace;
    public OutboundMode OutboundMode => host.EffectiveMode;

    public void ConfigureParticipationObservation(ParticipationReviewDefinition definition,
        Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>> analyze,
        int messageHorizon, int attemptBudget, int capacity,
        Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>>? analyzeInitial = null,
        bool previewResponses = false)
    {
        ArgumentNullException.ThrowIfNull(analyze);
        if (!serial.Wait(0)) throw new InvalidOperationException("Configure observation only while idle.");
        try
        {
            EnsureRunning();
            if (ceiling != OutboundMode.Shadow || participationObserver is not null)
                throw new InvalidOperationException("Participation observation requires an unconfigured shadow run.");
            var observer = handoff.CreateParticipationObserver(definition, messageHorizon, attemptBudget, capacity);
            if (analyzeInitial is not null) handoff.ConfigureInitialInterpretation(definition.ProjectRevision, analyzeInitial);
            participationObserver = observer;
            reassess = analyze;
            initialInterpretation = analyzeInitial;
            this.previewResponses = previewResponses;
        }
        finally { serial.Release(); }
    }

    public bool TrackParticipationReview(string originalMessageId)
    {
        if (!serial.Wait(0)) return false;
        try { EnsureRunning(); return participationObserver?.Track(originalMessageId) == true; }
        finally { serial.Release(); }
    }

    public async Task<ObservedShadowInteraction> AcceptAsync(IReadOnlyList<CanonicalShadowInbound> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        await serial.WaitAsync(cancellationToken);
        try
        {
            EnsureRunning();
            var watch = Stopwatch.StartNew();
            if (inputs.Count == 0) return Interaction(watch, [], [], []);

            if (ceiling == OutboundMode.Enabled && binding.Kind == SessionEntryKind.Dm && inputs.Where(i => i.Sequence > initialTail && !i.FromMe && !i.SkipNonText && !binding.IsSelf(i.ActorId))
                .Select(i => i.Message.ProviderMessageId).Distinct(StringComparer.Ordinal).Take(2).Count() > 1)
            {
                Stop();
                return Interaction(watch, inputs.Select(_ => new ShadowInboundDecision("rejected", "single_inbound_window_exceeded")).ToArray(), [], []);
            }

            if (FindBatchFailure(inputs) is { } failure)
            {
                var decisions = Enumerable.Repeat<ShadowInboundDecision>(
                    new("rejected", "batch_identity_validation_failed"), inputs.Count).ToArray();
                if (!failure.BindingFailure)
                {
                    inbox.RequireReconciliation(inputs[failure.Index].Message.ProviderMessageId);
                    decisions[failure.Index] = new("reconciliation_required", "invalid_batch_epoch_or_sequence");
                }
                Stop();
                return Interaction(watch, decisions, [], []);
            }

            var outcomesBefore = Outcomes.Count;
            if (ceiling == OutboundMode.Enabled && !continuous && binding.Kind == SessionEntryKind.Group
                && inputs.Where(i => i.Sequence > cursor && !i.FromMe && !i.SkipNonText && !binding.IsSelf(i.ActorId))
                    .Select(i => i.Message.ProviderMessageId).Distinct(StringComparer.Ordinal).Count()
                    > liveInteractionLimit - liveInteractions)
            {
                Stop();
                return Interaction(watch, inputs.Select(_ => new ShadowInboundDecision("rejected", "live_interaction_budget_exceeded")).ToArray(), [], []);
            }
            var traceBefore = Trace.Count;
            var decisionsForBatch = new List<ShadowInboundDecision>(inputs.Count);
            var admitted = false;
            try
            {
                foreach (var input in inputs)
                {
                    var decision = await handoff.AcceptAsync(input, cancellationToken);
                    decisionsForBatch.Add(decision);
                    AdvanceCursor(input, decision);
                    if (decision.Status == "admitted")
                    {
                        admitted = true;
                        if (ceiling == OutboundMode.Enabled) liveInteractions++;
                    }
                    if (decision.Status is "reconciliation_required" or "preserved" or "rejected")
                    {
                        Stop();
                        while (decisionsForBatch.Count < inputs.Count)
                            decisionsForBatch.Add(new("rejected", "batch_stopped_after_failure"));
                        return Interaction(watch, decisionsForBatch, Outcomes.Skip(outcomesBefore).ToArray(),
                            Trace.Skip(traceBefore).ToArray());
                    }
                }

                if (admitted)
                {
                    // Uses the normal session quiet/max-wait timers; this wrapper does not batch independently.
                    while (handoff.Snapshot is { OpenBufferCount: > 0 }) await Task.Delay(25, cancellationToken);
                    await handoff.DrainAsync(cancellationToken);
                }
            }
            catch
            {
                Stop();
                throw;
            }

            var outcomes = Outcomes.Skip(outcomesBefore).ToArray();
            var trace = Trace.Skip(traceBefore).ToArray();
            var failuresHandled = CanContinueReadOnlyFailure(outcomes, trace);
            if (outcomes.Any(outcome => !outcome.Success) && !failuresHandled || HasBadOutbound(trace) || OutboundMode != ceiling
                || (ceiling == OutboundMode.Enabled && host.RequiresReconciliation(binding.Session)))
                Stop();
            else
            {
                try { contextStore?.Save(handoff.CaptureContext()); }
                catch { Stop(); throw; }
            }
            using var reviewCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, participationLifetime.Token);
            var evaluated = outcomes.SelectMany(o => o.InitialInterpretations).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            var initial = !failuresHandled && participationObserver is not null && initialInterpretation is not null && Volatile.Read(ref stopped) == 0
                ? await participationObserver.ObserveNewAsync(outcomes.SelectMany(o => o.ProviderMessageIds).ToArray(),
                    (context, _) => Task.FromResult(evaluated.TryGetValue(context.Original.ProviderMessageId, out var analysis)
                        ? analysis : throw new InvalidDataException("Initial interpretation unavailable for this batch.")), reviewCancellation.Token)
                : [];
            // Reassessment is opportunistic, not an extra reply appended to a turn that
            // already spoke. Keep pending targets for later new context, without claiming
            // that a reply to another message answered them. Replay is not new context.
            var reviews = !failuresHandled && admitted && !trace.Any(t => t.EventName == "speech-committed")
                && participationObserver is not null && Volatile.Read(ref stopped) == 0
                ? await participationObserver.ReviewPendingAsync(reassess!, reviewCancellation.Token)
                : [];
            if (previewResponses)
            {
                var previews = new List<PendingParticipationReview>();
                foreach (var review in reviews)
                    previews.Add(review.Review is { Status: ParticipationReviewStatus.Completed, Decision: ParticipationReviewDecision.Respond } decision
                        ? review with { Preview = await handoff.PreviewResponseAsync(decision, review.Assessment?.Interpretation, reviewCancellation.Token) }
                        : review);
                reviews = previews;
            }
            if (continueResponses && Volatile.Read(ref stopped) == 0)
            {
                var continued = new List<PendingParticipationReview>();
                foreach (var review in reviews)
                {
                    if (Volatile.Read(ref stopped) == 0 && review.Review is
                        { Status: ParticipationReviewStatus.Completed, Decision: ParticipationReviewDecision.Respond } decision)
                    {
                        var response = await handoff.ContinueReviewedResponseAsync(decision, review.Assessment?.Interpretation, reviewCancellation.Token);
                        continued.Add(review with { Continuation = response });
                        if (response.Status == ParticipationReviewStatus.Failed || host.RequiresReconciliation(binding.Session)
                            || HasBadOutbound(Trace.Skip(traceBefore)) || OutboundMode != ceiling) Stop();
                        else contextStore?.Save(handoff.CaptureContext());
                    }
                    else continued.Add(review);
                }
                reviews = continued;
            }
            if (ceiling == OutboundMode.Enabled && !continuous && liveInteractions >= liveInteractionLimit) Stop();
            return Interaction(watch, decisionsForBatch, Outcomes.Skip(outcomesBefore).ToArray(), Trace.Skip(traceBefore).ToArray())
                with
            { ParticipationReviews = reviews, InitialInterpretations = initial, FailuresHandled = failuresHandled };
        }
        catch { Stop(); throw; }
        finally { serial.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Stop();
        try { await participationLifetime.CancelAsync(); }
        finally
        {
            try { await handoff.DisposeAsync(); }
            finally
            {
                // The observer belongs to AcceptAsync, not to the runtime's execution tail.
                // Drain it before disposing the run's serialization and journal resources.
                await serial.WaitAsync();
                inbox.Dispose();
                host.Dispose();
                participationLifetime.Dispose();
                serial.Dispose();
            }
        }
    }

    private void EnsureRunning()
    {
        if (Volatile.Read(ref stopped) != 0 || Volatile.Read(ref disposed) != 0 || OutboundMode != ceiling)
            throw new InvalidOperationException("Observed run stopped.");
    }

    private void Stop()
    {
        Interlocked.Exchange(ref stopped, 1);
        host.Rearm(OutboundMode.Disabled);
    }

    private void AdvanceCursor(CanonicalShadowInbound input, ShadowInboundDecision decision)
    {
        if (decision.Status is "self" or "observed" or "admitted" or "duplicate" or "ignored")
        {
            cursor = Math.Max(cursor, input.Sequence);
            observedSequences[input.Sequence] = input.Message.ProviderMessageId;
        }
    }

    private BatchFailure? FindBatchFailure(IReadOnlyList<CanonicalShadowInbound> inputs)
    {
        var plannedCursor = cursor;
        var plannedSequences = new Dictionary<long, string>(observedSequences);
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (!binding.Accepts(input)) return new(index, true);
            if (!StringComparer.Ordinal.Equals(input.StreamEpoch, epoch) || input.Sequence < 0)
                return new(index, false);
            if (input.Sequence <= initialTail) continue;
            if (plannedSequences.TryGetValue(input.Sequence, out var knownId)
                && !StringComparer.Ordinal.Equals(knownId, input.Message.ProviderMessageId))
                return new(index, false);
            if (input.Sequence > plannedCursor)
            {
                if (input.Sequence != plannedCursor + 1) return new(index, false);
                plannedCursor = input.Sequence;
                plannedSequences[input.Sequence] = input.Message.ProviderMessageId;
            }
            else if (!plannedSequences.ContainsKey(input.Sequence)) return new(index, false);
        }
        return null;
    }

    private bool HasBadOutbound(IEnumerable<SessionTraceEvent> trace) => trace.Any(item =>
        item.EventName == "node-outbound" &&
        (!item.MemoryMetadata?.TryGetValue("result", out var result) ?? true || result != (ceiling == OutboundMode.Shadow ? "shadowed" : "sent")));

    private bool CanContinueReadOnlyFailure(IReadOnlyList<SessionFlowOutcome> outcomes, IReadOnlyList<SessionTraceEvent> trace)
    {
        var failures = outcomes.Where(outcome => !outcome.Success).ToArray();
        if (!continuous || failures.Length == 0 || trace.Any(item => item.EventName is "node-outbound" or "speech-committed")) return false;
        foreach (var outcome in failures)
        {
            if (outcome.TransitionApplied || outcome.TransitionTargetStateId is not null
                || !project.States.TryGetValue(outcome.StateId, out var state)
                || !state.Entries.TryGetValue(outcome.EntryKind, out var flow)) return false;
            var execution = trace.Where(item => item.ExecutionId == outcome.ExecutionId).ToArray();
            var failed = execution.Where(item => item.EventName == "node-failed").ToArray();
            if (failed.Length != 1) return false;
            var category = failed[0].MemoryMetadata?.GetValueOrDefault("failureCategory");
            var containedTool = category == "contained_tool_completion_rejected"
                && failed[0].MemoryMetadata?.GetValueOrDefault("toolFailureContainmentVersion") == "scope-action-fence:v1"
                && failed[0].MemoryMetadata?.GetValueOrDefault("containedToolNodeId") == failed[0].NodeId;
            if (category != "read_only_model_response_rejected" && !containedTool) return false;
            var approvedNodes = new HashSet<string>(StringComparer.Ordinal);
            if (failed[0].MemoryMetadata?.GetValueOrDefault("readOnlyModelNodeIds") is { } approved)
            {
                try { approvedNodes.UnionWith(System.Text.Json.JsonSerializer.Deserialize<string[]>(approved) ?? []); }
                catch (System.Text.Json.JsonException) { return false; }
            }
            foreach (var started in execution.Where(item => item.EventName == "node-started"))
            {
                if (started.NodeId is null || !flow.Nodes.TryGetValue(started.NodeId, out var node) || node.HostEffect is not null) return false;
                if (node.Source.Kind == "prompt")
                {
                    // Jev/Laya's response boundary submits decisions only. A general
                    // generation provider may have called tools before rejecting output.
                    if (containedTool && started.NodeId == failed[0].NodeId
                        && TypedFlowRunner.OptionalText(node.Source.Config, "provider") is "openai" or "lmstudio") continue;
                    if (!approvedNodes.Contains(started.NodeId)
                        && TypedFlowRunner.OptionalText(node.Source.Config, "provider") is not ("jev" or "laya")) return false;
                }
                else if (node.Source.Kind is not ("dm" or "group" or "accumulate" or "history" or "memory"
                    or "interpretBoundaries" or "property" or "count" or "switch" or "branch" or "exit")) return false;
            }
        }
        return true;
    }

    private ObservedShadowInteraction Interaction(Stopwatch watch, IReadOnlyList<ShadowInboundDecision> decisions,
        IReadOnlyList<SessionFlowOutcome> outcomes, IReadOnlyList<SessionTraceEvent> trace) =>
        new(RunId, watch.Elapsed.TotalMilliseconds, decisions, outcomes, trace);

    private sealed record BatchFailure(int Index, bool BindingFailure);
}
