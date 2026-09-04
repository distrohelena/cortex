using System.Collections.Concurrent;
using System.Threading.Channels;
using Filomena.Core.Arbitration;

namespace Filomena.Core.Conversations;

public sealed record ConversationTurnResult(
    ConversationInfo Conversation,
    IReadOnlyList<string> PendingMessageIds,
    TurnDecision Decision,
    DateTimeOffset EvaluatedAt);

public sealed class ConversationTurnManager : IAsyncDisposable
{
    private readonly ITurnArbiter arbiter;
    private readonly ConversationTurnOptions options;
    private readonly IConversationClock clock;
    private readonly CancellationTokenSource shutdown = new();
    private readonly ConcurrentDictionary<string, ConversationTurnState> states =
        new(StringComparer.Ordinal);
    private readonly Channel<ConversationTurnResult> results =
        Channel.CreateUnbounded<ConversationTurnResult>();
    private int disposed;

    public ConversationTurnManager(
        ITurnArbiter arbiter,
        ConversationTurnOptions options,
        IConversationClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(arbiter);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.arbiter = arbiter;
        this.options = options;
        this.clock = clock ?? new TimeProviderConversationClock(TimeProvider.System);
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
        return state.Inbox.Writer.WriteAsync(message, cancellationToken);
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
            var decision = await DecideAsync(state, pending, cancellationToken);
            while (decision.Decision == TurnAction.Wait && waitCount < options.MaxWaitReevaluations)
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

            await results.Writer.WriteAsync(
                new ConversationTurnResult(
                    state.Conversation,
                    pending.Select(message => message.ProviderMessageId).ToArray(),
                    decision,
                    clock.UtcNow),
                cancellationToken);
        }
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
            if (remainingMax <= TimeSpan.Zero)
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
        var request = new TurnArbitrationRequest(state.Conversation, recent, signals);
        return await arbiter.DecideAsync(request, cancellationToken);
    }

    private void AppendAvailable(
        ConversationTurnState state,
        ICollection<InboundConversationEvent> pending)
    {
        while (state.Inbox.Reader.TryRead(out var message))
        {
            pending.Add(message);
            state.History.Add(new ObservedInbound(message, message.Timestamp ?? clock.UtcNow));
        }
    }

    private IReadOnlyList<InboundConversationEvent> BuildRecentContext(
        ConversationTurnState state)
    {
        var cutoff = clock.UtcNow - options.RecentWindow;
        state.History.RemoveAll(item => item.ObservedAt < cutoff);

        var selected = new List<InboundConversationEvent>(options.RecentMessageLimit);
        var characters = 0;
        for (var index = state.History.Count - 1;
             index >= 0 && selected.Count < options.RecentMessageLimit;
             index--)
        {
            var message = state.History[index].Message;
            var remaining = options.RecentCharacterLimit - characters;
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
        ChannelReader<InboundConversationEvent> reader,
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
