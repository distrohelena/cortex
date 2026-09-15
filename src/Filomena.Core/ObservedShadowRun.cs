using System.Diagnostics;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Outbound;

namespace Filomena.Core.Shadow;

public sealed record ObservedShadowInteraction(string RunId, double DurationMilliseconds,
    IReadOnlyList<ShadowInboundDecision> Decisions, IReadOnlyList<SessionFlowOutcome> Outcomes,
    IReadOnlyList<SessionTraceEvent> Trace);

public sealed class ObservedShadowRun : IAsyncDisposable
{
    private readonly long initialTail;
    private readonly ShadowSessionBinding binding;
    private readonly string epoch;
    private readonly ShadowInboundJournal inbox;
    private readonly ShadowOutboundHost host;
    private readonly ShadowRuntimeHandoff handoff;
    private readonly SemaphoreSlim serial = new(1, 1);
    private long cursor;
    private readonly Dictionary<long, string> observedSequences = new();
    private int stopped;
    private int disposed;

    public ObservedShadowRun(PinnedShadowProject project, ShadowSessionBinding binding, string journalDirectory,
        OutboundBindings roles, IFlowCompletion completion, IMemoryReader memory, string nativeEpoch, long initialTail)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(memory);
        this.binding = binding;
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeEpoch);
        if (initialTail < 0) throw new ArgumentOutOfRangeException(nameof(initialTail));
        epoch = nativeEpoch;
        this.initialTail = cursor = initialTail;

        host = new(Path.Combine(journalDirectory, "observation-shadow-outbound.jsonl"), project.Project.Document,
            roles, [binding.Session], TransportSafetyCapabilities.Unproven);
        try
        {
            host.Rearm(OutboundMode.Shadow);
            inbox = ShadowInboundJournal.StartNonResumableRun(
                Path.Combine(journalDirectory, "observation-shadow-inbound.jsonl"), binding,
                project.Project.ProjectRevision, epoch, initialTail);
            try { handoff = new(project, binding, inbox, host, completion, memory); }
            catch { inbox.Dispose(); throw; }
        }
        catch { host.Dispose(); throw; }
    }

    public string RunId => inbox.ShadowRunId!;
    public IReadOnlyList<SessionFlowOutcome> Outcomes => handoff.Outcomes;
    public IReadOnlyList<SessionHistoryEntry> History => handoff.History;
    public IReadOnlyList<SessionTraceEvent> Trace => handoff.Trace;
    public OutboundMode OutboundMode => host.EffectiveMode;

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
                    if (decision.Status == "admitted") admitted = true;
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
            if (outcomes.Any(outcome => !outcome.Success) || HasBadOutbound(trace) || OutboundMode != OutboundMode.Shadow)
                Stop();
            return Interaction(watch, decisionsForBatch, outcomes, trace);
        }
        finally { serial.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Stop();
        try { await handoff.DisposeAsync(); }
        finally
        {
            inbox.Dispose();
            host.Dispose();
            serial.Dispose();
        }
    }

    private void EnsureRunning()
    {
        if (Volatile.Read(ref stopped) != 0 || Volatile.Read(ref disposed) != 0 || OutboundMode != OutboundMode.Shadow)
            throw new InvalidOperationException("Observed shadow run stopped.");
    }

    private void Stop()
    {
        Interlocked.Exchange(ref stopped, 1);
        if (host.EffectiveMode == OutboundMode.Shadow)
            host.Rearm(OutboundMode.Disabled);
    }

    private void AdvanceCursor(CanonicalShadowInbound input, ShadowInboundDecision decision)
    {
        if (decision.Status is "self" or "observed" or "admitted" or "duplicate")
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

    private static bool HasBadOutbound(IEnumerable<SessionTraceEvent> trace) => trace.Any(item =>
        item.EventName == "node-outbound" &&
        (!item.MemoryMetadata?.TryGetValue("result", out var result) ?? true || result != "shadowed"));

    private ObservedShadowInteraction Interaction(Stopwatch watch, IReadOnlyList<ShadowInboundDecision> decisions,
        IReadOnlyList<SessionFlowOutcome> outcomes, IReadOnlyList<SessionTraceEvent> trace) =>
        new(RunId, watch.Elapsed.TotalMilliseconds, decisions, outcomes, trace);

    private sealed record BatchFailure(int Index, bool BindingFailure);
}
