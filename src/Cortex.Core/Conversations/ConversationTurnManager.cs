using System.Collections.Concurrent;
using System.Threading.Channels;
using Cortex.Core.Arbitration;

namespace Cortex.Core.Conversations;

public sealed record ConversationTurnResult(
    ConversationInfo Conversation,
    IReadOnlyList<string> PendingMessageIds,
    TurnDecision Decision,
    DateTimeOffset EvaluatedAt)
{
    public SentConversationTurn? SentTurn { get; init; }
    public string? ExecutionError { get; init; }
    public bool AwaitingInbound { get; init; }
}

public sealed class ConversationTurnManager : IAsyncDisposable
{
    private readonly ITurnArbiter arbiter;
    private readonly ConversationTurnOptions options;
    private readonly IConversationClock clock;
    private readonly IConversationTurnExecutor? executor;
    private readonly CancellationTokenSource shutdown = new();
    private readonly ConcurrentDictionary<string, ConversationTurnState> states =
        new(StringComparer.Ordinal);
    private readonly Channel<ConversationTurnResult> results =
        Channel.CreateUnbounded<ConversationTurnResult>();
    private int disposed;

    public ConversationTurnManager(
        ITurnArbiter arbiter,
        ConversationTurnOptions options,
        IConversationClock? clock = null,
        IConversationTurnExecutor? executor = null)
    {
        ArgumentNullException.ThrowIfNull(arbiter);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.arbiter = arbiter;
        this.options = options;
        this.clock = clock ?? new TimeProviderConversationClock(TimeProvider.System);
        this.executor = executor;
    }

    public ChannelReader<ConversationTurnResult> Results => results.Reader;

    public ValueTask EnqueueAsync(
        InboundConversationEvent message,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(message);
        var conversation = message.Conversation ?? throw new ArgumentException(
            "A canonical conversation is required for turn scheduling.",
            nameof(message));
        ArgumentException.ThrowIfNullOrWhiteSpace(conversation.Id.Namespace);

        var state = states.GetOrAdd(
            conversation.Id.Namespace,
            _ => new ConversationTurnState(conversation));
        _ = state.Start(() => ProcessConversationAsync(state, shutdown.Token));
        return state.Inbox.Writer.WriteAsync(new QueuedInbound(message, clock.UtcNow), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        shutdown.Cancel();
        foreach (var state in states.Values)
        {
            state.Inbox.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(states.Values.Select(state => state.Start(() => Task.CompletedTask)));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            results.Writer.TryComplete();
            shutdown.Dispose();
        }
    }

    private async Task ProcessConversationAsync(
        ConversationTurnState state,
        CancellationToken cancellationToken)
    {
        var reader = state.Inbox.Reader;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            var pending = new List<InboundConversationEvent>();
            AppendAvailable(state, pending);
            var burstStartedAt = clock.UtcNow;
            await CollectBurstAsync(state, pending, burstStartedAt, cancellationToken);

            var waitCount = 0;
            var adaptiveDm = BuildSignals(state, pending).IsAllowlistedDirectMessage;
            var decision = adaptiveDm
                ? await DecideDirectMessageAsync(state, pending, burstStartedAt, cancellationToken)
                : await DecideAsync(state, pending, cancellationToken);
            while (!adaptiveDm && decision.Decision == TurnAction.Wait && waitCount < options.MaxWaitReevaluations)
            {
                waitCount++;
                var receivedMore = await WaitForMoreOrDelayAsync(
                    reader,
                    options.WaitReevaluationDelay,
                    cancellationToken);
                if (receivedMore)
                {
                    AppendAvailable(state, pending);
                    await CollectBurstAsync(state, pending, burstStartedAt, cancellationToken);
                }

                decision = await DecideAsync(state, pending, cancellationToken);
            }

            if (decision.Decision == TurnAction.Wait)
            {
                decision = new TurnDecision(
                    TurnAction.Ignore,
                    null,
                    null,
                    TurnReason.WaitLimitReached);
            }

            var evaluatedAt = clock.UtcNow;
            SentConversationTurn? sent = null;
            string? executionError = null;
            if (decision.Decision == TurnAction.Respond && executor is not null)
            {
                try
                {
                    sent = await executor.ExecuteAsync(new ConversationGenerationRequest(
                        state.Conversation,
                        BuildRecentContext(state, generation: true, pending),
                        RecentOutbound(state, options.GenerationWindow),
                        pending.Select(message => message.ProviderMessageId).ToArray(),
                        decision)
                    {
                        IsSuperseded = adaptiveDm ? () => state.Inbox.Reader.TryPeek(out _) : null,
                    }, cancellationToken);
                    if (sent is not null)
                    {
                        state.LastFilomenaTurnAt = sent.SentAt;
                        state.OutboundHistory.Add(sent);
                        state.OutboundHistory.RemoveAll(item => item.SentAt < clock.UtcNow - options.GenerationWindow);
                        if (state.OutboundHistory.Count > options.GenerationMessageLimit)
                            state.OutboundHistory.RemoveRange(0, state.OutboundHistory.Count - options.GenerationMessageLimit);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    // Never replay an uncertain send. Report the type without leaking prompt/tool content.
                    executionError = exception.GetType().Name;
                }
            }

            await results.Writer.WriteAsync(
                new ConversationTurnResult(
                    state.Conversation,
                    pending.Select(message => message.ProviderMessageId).ToArray(),
                    decision,
                    evaluatedAt)
                { SentTurn = sent, ExecutionError = executionError },
                cancellationToken);
        }
    }

    private async Task<TurnDecision> DecideDirectMessageAsync(
        ConversationTurnState state, List<InboundConversationEvent> pending,
        DateTimeOffset pendingStartedAt, CancellationToken cancellationToken)
    {
        var timedRechecks = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.Inbox.Reader.TryPeek(out _))
            {
                await CollectDmContinuationAsync(state, pending, cancellationToken);
                timedRechecks = 0;
            }
            var silence = Math.Max(0, (clock.UtcNow - (state.LastInboundObservedAt ?? clock.UtcNow)).TotalSeconds);
            var request = new TurnArbitrationRequest(state.Conversation,
                BuildRecentContext(state, pending: pending), BuildSignals(state, pending))
            {
                PendingMessageIds = pending.Select(message => message.ProviderMessageId).ToArray(),
                RecentOutbound = RecentOutbound(state, options.RecentWindow),
                DirectMessageTiming = new(silence, Math.Max(0, (clock.UtcNow - pendingStartedAt).TotalSeconds),
                    Math.Max(0, options.DmMaxTimedReevaluations - timedRechecks),
                    silence >= options.DmFollowUpAfterSilenceSeconds,
                    options.DmDefaultRecheckSeconds, options.DmMinRecheckSeconds, options.DmMaxRecheckSeconds),
            };
            var decision = await arbiter.DecideAsync(request, cancellationToken);
            // A decision based on an older snapshot cannot authorize a DM send.
            if (state.Inbox.Reader.TryPeek(out _)) continue;
            if (!TurnDecisionPolicy.IsValid(decision, request))
                decision = TurnDecisionPolicy.InvalidOutputFallback(request);
            if (decision.Decision != TurnAction.Wait) return decision;

            var park = timedRechecks >= options.DmMaxTimedReevaluations;
            var seconds = Math.Clamp(decision.RecheckAfterSeconds ?? options.DmDefaultRecheckSeconds,
                options.DmMinRecheckSeconds, options.DmMaxRecheckSeconds);
            decision = decision with { RecheckAfterSeconds = park ? null : seconds };
            await results.Writer.WriteAsync(new(state.Conversation,
                pending.Select(message => message.ProviderMessageId).ToArray(), decision, clock.UtcNow)
            { AwaitingInbound = park }, cancellationToken);

            var hasMore = park
                ? await state.Inbox.Reader.WaitToReadAsync(cancellationToken)
                : await WaitForMoreOrDelayAsync(state.Inbox.Reader, TimeSpan.FromSeconds(seconds), cancellationToken);
            if (hasMore)
            {
                await CollectDmContinuationAsync(state, pending, cancellationToken);
                timedRechecks = 0;
            }
            else timedRechecks++;
        }
    }

    private async Task CollectDmContinuationAsync(ConversationTurnState state,
        List<InboundConversationEvent> pending, CancellationToken cancellationToken)
    {
        var continuation = new List<InboundConversationEvent>();
        AppendAvailable(state, continuation);
        await CollectBurstAsync(state, continuation, clock.UtcNow, cancellationToken);
        pending.AddRange(continuation);
        if (pending.Count > options.MaxBurstMessages)
            pending.RemoveRange(0, pending.Count - options.MaxBurstMessages);
    }

    private async Task CollectBurstAsync(
        ConversationTurnState state,
        List<InboundConversationEvent> pending,
        DateTimeOffset burstStartedAt,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var timing = ResolveTiming(state.Conversation, pending);
            var elapsed = clock.UtcNow - burstStartedAt;
            var remainingMax = timing.MaxBurst - elapsed;
            if (remainingMax <= TimeSpan.Zero || pending.Count >= options.MaxBurstMessages)
            {
                return;
            }

            var delay = timing.QuietWindow < remainingMax
                ? timing.QuietWindow
                : remainingMax;
            if (!await WaitForMoreOrDelayAsync(state.Inbox.Reader, delay, cancellationToken))
            {
                return;
            }

            AppendAvailable(state, pending);
        }
    }

    private async Task<TurnDecision> DecideAsync(
        ConversationTurnState state,
        IReadOnlyList<InboundConversationEvent> pending,
        CancellationToken cancellationToken)
    {
        var recent = BuildRecentContext(state);
        var signals = BuildSignals(state, pending);
        var request = new TurnArbitrationRequest(state.Conversation, recent, signals)
        {
            PendingMessageIds = pending.Select(message => message.ProviderMessageId).ToArray(),
            RecentOutbound = RecentOutbound(state, options.RecentWindow),
        };
        return await arbiter.DecideAsync(request, cancellationToken);
    }

    private void AppendAvailable(
        ConversationTurnState state,
        ICollection<InboundConversationEvent> pending)
    {
        while (pending.Count < options.MaxBurstMessages && state.Inbox.Reader.TryRead(out var queued))
        {
            var message = queued.Message;
            pending.Add(message);
            state.LastInboundObservedAt = queued.ReceivedAt;
            state.Conversation = message.Conversation!;
            state.History.Add(new ObservedInbound(message, message.Timestamp ?? clock.UtcNow));
            var retainedLimit = Math.Max(options.RecentMessageLimit, options.GenerationMessageLimit);
            if (state.History.Count > retainedLimit)
                state.History.RemoveRange(0, state.History.Count - retainedLimit);
        }
    }

    private IReadOnlyList<InboundConversationEvent> BuildRecentContext(
        ConversationTurnState state, bool generation = false, IReadOnlyList<InboundConversationEvent>? pending = null)
    {
        var window = generation ? options.GenerationWindow : options.RecentWindow;
        var messageLimit = generation ? options.GenerationMessageLimit : options.RecentMessageLimit;
        var characterLimit = generation ? options.GenerationCharacterLimit : options.RecentCharacterLimit;
        var cutoff = clock.UtcNow - window;

        var selected = new List<InboundConversationEvent>(messageLimit);
        var characters = 0;
        for (var index = state.History.Count - 1;
             index >= 0 && selected.Count < messageLimit;
             index--)
        {
            var message = state.History[index].Message;
            if (state.History[index].ObservedAt < cutoff &&
                pending?.Any(item => item.ProviderMessageId == message.ProviderMessageId) != true) continue;
            var remaining = characterLimit - characters;
            if (remaining <= 0)
            {
                break;
            }

            if (message.Content.Length > remaining)
            {
                if (selected.Count == 0)
                {
                    selected.Add(message with { Content = message.Content[^remaining..] });
                }

                break;
            }

            selected.Add(message);
            characters += message.Content.Length;
        }

        selected.Reverse();
        return selected;
    }

    private IReadOnlyList<SentConversationTurn> RecentOutbound(ConversationTurnState state, TimeSpan window) =>
        state.OutboundHistory.Where(item => item.SentAt >= clock.UtcNow - window).TakeLast(4).ToArray();

    private TurnSignals BuildSignals(
        ConversationTurnState state,
        IReadOnlyList<InboundConversationEvent> pending)
    {
        var isDirectMessage = state.Conversation.Kind == ConversationKind.DirectMessage;
        var isAllowlisted = isDirectMessage && options.AllowlistedDirectMessageNativeIds.Any(id =>
            string.Equals(id, state.Conversation.Id.NativeId, StringComparison.OrdinalIgnoreCase));
        var directAddressed = pending.Any(message => options.FilomenaAliases.Any(alias =>
            !string.IsNullOrWhiteSpace(alias) &&
            message.Content.Contains(alias, StringComparison.OrdinalIgnoreCase)));
        var hasQuestion = pending.Any(message => message.Content.Contains('?'));
        var hasStructuralReply = pending.Any(message =>
            message.StructuralMetadata.TryGetValue("reply_to_filomena", out var value) &&
            value is true);

        return new TurnSignals(
            isDirectMessage,
            isAllowlisted,
            directAddressed,
            hasQuestion,
            hasStructuralReply,
            state.LastFilomenaTurnAt is { } lastTurn ? clock.UtcNow - lastTurn : null);
    }

    private BurstTiming ResolveTiming(
        ConversationInfo conversation,
        IReadOnlyList<InboundConversationEvent> pending)
    {
        if (conversation.Kind == ConversationKind.DirectMessage)
        {
            return new BurstTiming(
                options.AllowlistedDirectMessageQuietWindow,
                options.AllowlistedDirectMessageMaxBurst);
        }

        var directlyAddressed = pending.Any(message => options.FilomenaAliases.Any(alias =>
            !string.IsNullOrWhiteSpace(alias) &&
            message.Content.Contains(alias, StringComparison.OrdinalIgnoreCase)));
        return directlyAddressed
            ? new BurstTiming(
                options.DirectAddressGroupQuietWindow,
                options.DirectAddressGroupMaxBurst)
            : new BurstTiming(options.GroupQuietWindow, options.GroupMaxBurst);
    }

    private async Task<bool> WaitForMoreOrDelayAsync(
        ChannelReader<QueuedInbound> reader,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var inputTask = reader.WaitToReadAsync(raceCancellation.Token).AsTask();
        var delayTask = clock.DelayAsync(delay, raceCancellation.Token);
        var completed = await Task.WhenAny(inputTask, delayTask);
        await raceCancellation.CancelAsync();

        if (completed == inputTask)
        {
            return await inputTask;
        }

        await delayTask;
        return false;
    }

    private sealed record BurstTiming(TimeSpan QuietWindow, TimeSpan MaxBurst);
}
