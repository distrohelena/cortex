using Filomena.Core.Conversations;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Outbound;

namespace Filomena.Core.Shadow;

internal sealed record ObservedLiveWindow(LiveRouteBinding Route, IBoundOutboundTransport Transport,
    TransportSafetyCapabilities Capabilities, int MaxInteractions, bool ContinuousGroup);

/// <summary>Explicit host-only, bounded live window. No automatic listener, restart or rearm.</summary>
public sealed class ObservedLiveRun : IAsyncDisposable
{
    private readonly ObservedShadowRun run;

    public ObservedLiveRun(PinnedShadowProject project, ShadowSessionBinding binding, string journalDirectory,
        OutboundBindings roles, IFlowCompletion completion, IMemoryReader memory, string nativeEpoch, long initialTail,
        LiveRouteBinding route, IBoundOutboundTransport transport, TransportSafetyCapabilities capabilities,
        Func<CancellationToken, Task> beforeSend, int maxInteractions = 1, bool continuousGroup = false)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(beforeSend);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (maxInteractions is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(maxInteractions));
        if (continuousGroup && (binding.Kind != SessionEntryKind.Group || !route.AllowExactGroupDelivery))
            throw new InvalidOperationException("Continuous delivery requires exact group authority.");
        if (!capabilities.Complete) throw new InvalidOperationException("Unproven live transport capabilities.");
        run = new(project, binding, journalDirectory, roles, completion, memory, nativeEpoch, initialTail,
            new(route, new BoundedAttemptTransport(transport, beforeSend, continuousGroup ? null : maxInteractions),
                capabilities, maxInteractions, continuousGroup));
    }

    public string RunId => run.RunId;
    public OutboundMode OutboundMode => run.OutboundMode;
    public IReadOnlyList<SessionHistoryEntry> History => run.History;
    public IReadOnlyList<SessionFlowOutcome> Outcomes => run.Outcomes;
    public IReadOnlyList<SessionTraceEvent> Trace => run.Trace;
    public Task<ObservedShadowInteraction> AcceptAsync(IReadOnlyList<CanonicalShadowInbound> inputs, CancellationToken token)
        => run.AcceptAsync(inputs, token);
    public ValueTask DisposeAsync() => run.DisposeAsync();

    // The durable host still reserves/reconciles the attempt. This fence only narrows the operational window.
    private sealed class BoundedAttemptTransport(IBoundOutboundTransport inner, Func<CancellationToken, Task> beforeSend, int? limit)
        : IBoundOutboundTransport
    {
        private int used;
        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken token)
            => inner.DescribeRouteAsync(route, token);
        public Task<SendReceipt> SendTextAsync(string route, string text, CancellationToken token)
            => throw new NotSupportedException("Unbound send is unavailable.");
        public async Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken token)
        {
            if (limit is { } maximum && Interlocked.Increment(ref used) > maximum)
                throw new InvalidOperationException("Live window already consumed.");
            await beforeSend(token);
            token.ThrowIfCancellationRequested();
            return await inner.SendBoundTextAsync(request, token);
        }
    }
}
