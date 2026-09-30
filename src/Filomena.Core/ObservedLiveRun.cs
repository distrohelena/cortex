using Filomena.Core.Conversations;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Outbound;

namespace Filomena.Core.Shadow;

public sealed record LiveParticipationOptions(ParticipationReviewDefinition Definition,
    Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>> AnalyzeInitial,
    Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>> Reassess,
    int MessageHorizon, int AttemptBudget, int Capacity, bool RespondToReassessedMessages = false);

internal sealed record ObservedLiveWindow(LiveRouteBinding Route, IBoundOutboundTransport Transport,
    TransportSafetyCapabilities Capabilities, int MaxInteractions, bool Continuous, LiveParticipationOptions? Participation);

/// <summary>Explicit host-only, bounded live window. No automatic listener, restart or rearm.</summary>
public sealed class ObservedLiveRun : IAsyncDisposable
{
    private readonly ObservedShadowRun run;

    // Preserve the ABI used by already deployed hosts predating participation review.
    // Optional parameters are a source convenience, not binary compatibility.
    public ObservedLiveRun(PinnedShadowProject project, ShadowSessionBinding binding, string journalDirectory,
        OutboundBindings roles, IFlowCompletion completion, IMemoryReader memory, string nativeEpoch, long initialTail,
        LiveRouteBinding route, IBoundOutboundTransport transport, TransportSafetyCapabilities capabilities,
        Func<CancellationToken, Task> beforeSend, int maxInteractions, bool continuousGroup,
        bool continuousDm, MemberDmBoundTextBinding? memberDmBinding)
        : this(project, binding, journalDirectory, roles, completion, memory, nativeEpoch, initialTail,
            route, transport, capabilities, beforeSend, maxInteractions, continuousGroup, continuousDm, memberDmBinding, null)
    {
    }

    public ObservedLiveRun(PinnedShadowProject project, ShadowSessionBinding binding, string journalDirectory,
        OutboundBindings roles, IFlowCompletion completion, IMemoryReader memory, string nativeEpoch, long initialTail,
        LiveRouteBinding route, IBoundOutboundTransport transport, TransportSafetyCapabilities capabilities,
        Func<CancellationToken, Task> beforeSend, int maxInteractions = 1, bool continuousGroup = false,
        bool continuousDm = false, MemberDmBoundTextBinding? memberDmBinding = null, LiveParticipationOptions? participation = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(beforeSend);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (maxInteractions is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(maxInteractions));
        if (continuousGroup && (binding.Kind != SessionEntryKind.Group || !route.AllowExactGroupDelivery))
            throw new InvalidOperationException("Continuous delivery requires exact group authority.");
        if (continuousDm && (binding.Kind != SessionEntryKind.Dm || route.AllowExactGroupDelivery))
            throw new InvalidOperationException("Continuous DM requires exact DM authority.");
        if (continuousGroup && continuousDm)
            throw new InvalidOperationException("Continuous group and DM authorities are mutually exclusive.");
        if (memberDmBinding is not null && (!continuousDm || binding.Kind != SessionEntryKind.Dm
                || memberDmBinding.NativeConversationId != binding.Session.NativeConversationId
                || route.Recipient != memberDmBinding.NativeConversationId
                || string.IsNullOrWhiteSpace(memberDmBinding.ScopeRef)
                || string.IsNullOrWhiteSpace(memberDmBinding.RouteBindingRef)
                || string.IsNullOrWhiteSpace(memberDmBinding.EffectiveConfigRevision)
                || string.IsNullOrWhiteSpace(memberDmBinding.MembershipLeaseRef)))
            throw new InvalidOperationException("Managed member-DM authority must be complete and exact.");
        if (!capabilities.Complete) throw new InvalidOperationException("Unproven live transport capabilities.");
        if (participation is not null)
        {
            if (binding.Kind != SessionEntryKind.Group || participation.Definition is null
                || participation.AnalyzeInitial is null || participation.Reassess is null
                || participation.MessageHorizon < 1 || participation.AttemptBudget < 1 || participation.Capacity < 1)
                throw new InvalidDataException("Invalid live participation configuration.");
            participation.Definition.Validate(project.Project, binding.Kind);
        }
        run = new(project, binding, journalDirectory, roles, completion, memory, nativeEpoch, initialTail,
            new(route, new BoundedAttemptTransport(transport, beforeSend, continuousGroup || continuousDm ? null : maxInteractions,
                    memberDmBinding),
                capabilities, maxInteractions, continuousGroup || continuousDm, participation));
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
    private sealed class BoundedAttemptTransport(IBoundOutboundTransport inner, Func<CancellationToken, Task> beforeSend,
        int? limit, MemberDmBoundTextBinding? memberDmBinding)
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
            if (memberDmBinding is not null)
                request = request with { MemberDm = memberDmBinding };
            return await inner.SendBoundTextAsync(request, token);
        }
    }
}
