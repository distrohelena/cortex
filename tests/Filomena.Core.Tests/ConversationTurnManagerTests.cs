using Filomena.Core.Arbitration;
using Filomena.Core.Conversations;
using Xunit;

namespace Filomena.Core.Tests;

public sealed class ConversationTurnManagerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthorizedDmGreetingStillRequiresCompletenessArbiter(bool authorized)
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(authorized
            ? new TurnDecision(TurnAction.Respond, "hello", Actor.StableId, TurnReason.ThoughtComplete)
            : new TurnDecision(TurnAction.Ignore, null, null, TurnReason.NoUsefulContribution));
        var dm = GroupA with
        {
            Kind = ConversationKind.DirectMessage,
            Id = GroupA.Id with { NativeId = authorized ? "5511944558218@s.whatsapp.net" : "other", Namespace = "test-dm" },
        };
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);
        await manager.EnqueueAsync(Message(dm, "hello", "Hey Filooo"), TestContext.Current.CancellationToken);
        var result = await ReadResultByAdvancingClockAsync(manager, clock);
        if (authorized)
        {
            Assert.Equal(TurnAction.Respond, result.Decision.Decision);
            Assert.Equal(TurnReason.ThoughtComplete, result.Decision.Reason);
            Assert.Equal("hello", result.Decision.AnchorRef);
            Assert.Equal(Actor.StableId, result.Decision.TargetRef);
            Assert.Single(arbiter.Requests);
            Assert.NotNull(arbiter.Requests[0].DirectMessageTiming);
        }
        else
        {
            Assert.Single(arbiter.Requests);
            Assert.Equal(TurnAction.Ignore, result.Decision.Decision);
        }
    }

    [Fact]
    public async Task DmWaitUsesSuggestedDelayThenAllowsOneContextualFollowUp()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(
            new(TurnAction.Wait, null, null, TurnReason.ThoughtIncomplete) { RecheckAfterSeconds = 45 },
            new(TurnAction.Respond, "m1", Actor.StableId, TurnReason.IncompleteThoughtFollowUp) { ResponseKind = TurnResponseKind.FollowUp });
        var executor = new ImmediateExecutor();
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock, executor);
        await manager.EnqueueAsync(Message(AuthorizedDm, "m1", "eu queria te contar uma coisa..."), TestContext.Current.CancellationToken);
        var waiting = await AdvanceNextDelayAndReadAsync(manager, clock);
        Assert.Equal(TurnAction.Wait, waiting.Decision.Decision);
        await clock.WaitForDelayAsync();
        Assert.Equal(TimeSpan.FromSeconds(45), clock.LastRequestedDelay);
        clock.Advance(TimeSpan.FromSeconds(45));
        var result = await ReadResultAsync(manager);
        Assert.Equal(TurnResponseKind.FollowUp, result.Decision.ResponseKind);
        Assert.NotNull(result.SentTurn);
        Assert.Single(executor.Requests);
        Assert.True(arbiter.Requests[^1].DirectMessageTiming!.FollowUpAllowed);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(manager.Results.TryRead(out _));
        Assert.Single(executor.Requests);
    }

    [Fact]
    public async Task DmWaitParksWithoutIgnoreAndResumesWithPendingContext()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(
            new(TurnAction.Wait, null, null, TurnReason.ThoughtIncomplete) { RecheckAfterSeconds = 10 },
            new(TurnAction.Wait, null, null, TurnReason.WaitingForContinuation) { RecheckAfterSeconds = 10 },
            new(TurnAction.Respond, "m2", Actor.StableId, TurnReason.ThoughtComplete));
        await using var manager = new ConversationTurnManager(arbiter,
            Options() with { DmMaxTimedReevaluations = 1 }, clock);
        await manager.EnqueueAsync(Message(AuthorizedDm, "m1", "queria perguntar..."), TestContext.Current.CancellationToken);
        Assert.Equal(TurnAction.Wait, (await AdvanceNextDelayAndReadAsync(manager, clock)).Decision.Decision);
        var parked = await AdvanceNextDelayAndReadAsync(manager, clock);
        Assert.Equal(TurnAction.Wait, parked.Decision.Decision);
        Assert.True(parked.AwaitingInbound);
        clock.Advance(TimeSpan.FromHours(1));
        await manager.EnqueueAsync(Message(AuthorizedDm, "m2", "sobre o roteador", clock.UtcNow), TestContext.Current.CancellationToken);
        var result = await AdvanceNextDelayAndReadAsync(manager, clock);
        Assert.Equal(TurnAction.Respond, result.Decision.Decision);
        Assert.Contains(arbiter.Requests[^1].Messages, message => message.ProviderMessageId == "m1");
        Assert.Contains(arbiter.Requests[^1].Messages, message => message.ProviderMessageId == "m2");
    }

    [Fact]
    public async Task DmNewMessageDuringArbitrationInvalidatesOldResponse()
    {
        var clock = new ManualConversationClock();
        var arbiter = new BlockingArbiter();
        var executor = new ImmediateExecutor();
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock, executor);
        await manager.EnqueueAsync(Message(AuthorizedDm, "m1", "eu queria..."), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();
        clock.Advance(clock.LastRequestedDelay);
        await arbiter.WaitUntilCalledAsync();
        await manager.EnqueueAsync(Message(AuthorizedDm, "m2", "pera, ainda estou escrevendo"), TestContext.Current.CancellationToken);
        arbiter.Release(new(TurnAction.Respond, "m1", Actor.StableId, TurnReason.ThoughtComplete));
        _ = await ReadResultByAdvancingClockAsync(manager, clock);
        Assert.Empty(executor.Requests);
    }

    private static ConversationInfo AuthorizedDm => GroupA with
    {
        Id = new("whatsapp", "default", "5511944558218@s.whatsapp.net", "whatsapp-dm-5511944558218-s-whatsapp-net"),
        Kind = ConversationKind.DirectMessage,
    };

    [Theory]
    [InlineData(1, 5)]
    [InlineData(9999, 120)]
    public async Task DmSuggestedWaitIsClampedToConfiguredLimits(int suggested, int expected)
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(
            new TurnDecision(TurnAction.Wait, null, null, TurnReason.ThoughtIncomplete) { RecheckAfterSeconds = suggested });
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);
        await manager.EnqueueAsync(Message(AuthorizedDm, "m1", "eu queria..."), TestContext.Current.CancellationToken);
        var result = await AdvanceNextDelayAndReadAsync(manager, clock);
        Assert.Equal(expected, result.Decision.RecheckAfterSeconds);
        await clock.WaitForDelayAsync();
        Assert.Equal(TimeSpan.FromSeconds(expected), clock.LastRequestedDelay);
    }

    [Fact]
    public async Task NewMessageInterruptsAdaptiveWaitAndRestartsQuietWindow()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(
            new(TurnAction.Wait, null, null, TurnReason.ThoughtIncomplete) { RecheckAfterSeconds = 90 },
            new(TurnAction.Respond, "m2", Actor.StableId, TurnReason.ThoughtComplete));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);
        await manager.EnqueueAsync(Message(AuthorizedDm, "m1", "queria perguntar..."), TestContext.Current.CancellationToken);
        _ = await AdvanceNextDelayAndReadAsync(manager, clock);
        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        var previousRequests = clock.RequestCount;
        await manager.EnqueueAsync(Message(AuthorizedDm, "m2", "como funciona DNS?", clock.UtcNow), TestContext.Current.CancellationToken);
        await clock.WaitForRequestCountAsync(previousRequests + 1);
        Assert.Equal(TimeSpan.FromSeconds(1.8), clock.LastRequestedDelay);
        clock.Advance(clock.LastRequestedDelay);
        var result = await ReadResultAsync(manager);
        Assert.Equal(TurnAction.Respond, result.Decision.Decision);
        Assert.Equal(2, arbiter.Requests.Count);
        Assert.Equal(1.8, arbiter.Requests[^1].DirectMessageTiming!.SilenceSeconds, precision: 3);
    }

    [Fact]
    public async Task PendingDmDoesNotBlockAnotherConversationAndCancelsCleanly()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(
            new(TurnAction.Wait, null, null, TurnReason.ThoughtIncomplete) { RecheckAfterSeconds = 120 },
            new(TurnAction.Ignore, null, null, TurnReason.NoUsefulContribution));
        var manager = new ConversationTurnManager(arbiter, Options(), clock);
        await manager.EnqueueAsync(Message(AuthorizedDm, "m1", "espera..."), TestContext.Current.CancellationToken);
        _ = await AdvanceNextDelayAndReadAsync(manager, clock);
        await clock.WaitForDelayAsync();
        var previousRequests = clock.RequestCount;
        await manager.EnqueueAsync(Message(GroupB, "group1", "outro chat"), TestContext.Current.CancellationToken);
        await clock.WaitForRequestCountAsync(previousRequests + 1);
        clock.Advance(TimeSpan.FromSeconds(3.5));
        Assert.Equal(GroupB.Id, (await ReadResultAsync(manager)).Conversation.Id);
        await manager.DisposeAsync();
        Assert.True(manager.Results.Completion.IsCompletedSuccessfully);
    }

    private sealed class ImmediateExecutor : IConversationTurnExecutor
    {
        public List<ConversationGenerationRequest> Requests { get; } = [];
        public Task<SentConversationTurn?> ExecuteAsync(ConversationGenerationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<SentConversationTurn?>(new("sent", "Você ia completar?", DateTimeOffset.Parse("2026-09-04T18:01:00Z")));
        }
    }

    private static async Task<ConversationTurnResult> AdvanceNextDelayAndReadAsync(
        ConversationTurnManager manager, ManualConversationClock clock)
    {
        await clock.WaitForDelayAsync();
        clock.Advance(clock.LastRequestedDelay);
        return await ReadResultAsync(manager);
    }

    [Fact]
    public async Task TenDmMessagesTwoSecondsApartWaitForTheFinalQuietWindow()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter();
        var options = Options() with
        {
            AllowlistedDirectMessageQuietWindow = TimeSpan.FromSeconds(3),
            AllowlistedDirectMessageMaxBurst = TimeSpan.FromSeconds(60),
            MaxBurstMessages = 50,
        };
        var dm = GroupA with { Kind = ConversationKind.DirectMessage };
        await using var manager = new ConversationTurnManager(arbiter, options, clock);
        await manager.EnqueueAsync(Message(dm, "m1", "primeiro trecho"), TestContext.Current.CancellationToken);
        await clock.WaitForRequestCountAsync(1);
        for (var index = 2; index <= 10; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            var previous = clock.RequestCount;
            await manager.EnqueueAsync(Message(dm, $"m{index}", "mais um trecho"), TestContext.Current.CancellationToken);
            await clock.WaitForRequestCountAsync(previous + 1);
            Assert.Empty(arbiter.Requests);
        }
        clock.Advance(TimeSpan.FromSeconds(3));
        var result = await ReadResultAsync(manager);
        Assert.Equal(10, result.PendingMessageIds.Count);
        Assert.Single(arbiter.Requests);
    }

    [Fact]
    public async Task MessageCountCapEndsBurstWithoutWaitingForSilence()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter();
        await using var manager = new ConversationTurnManager(arbiter, Options() with { MaxBurstMessages = 2 }, clock);
        await manager.EnqueueAsync(Message(GroupA, "m1", "um"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();
        await manager.EnqueueAsync(Message(GroupA, "m2", "dois"), TestContext.Current.CancellationToken);
        var result = await ReadResultAsync(manager);
        Assert.Equal(2, result.PendingMessageIds.Count);
        Assert.Single(arbiter.Requests);
    }

    [Fact]
    public async Task GenerationBlocksOnlyItsConversationAndRecordsConfirmedTurn()
    {
        var clock = new ManualConversationClock();
        var executor = new BlockingExecutor();
        var arbiter = new RecordingArbiter(
            new TurnDecision(TurnAction.Respond, null, null, TurnReason.UsefulContribution),
            new TurnDecision(TurnAction.Ignore, null, null, TurnReason.NoUsefulContribution),
            new TurnDecision(TurnAction.Ignore, null, null, TurnReason.NoUsefulContribution));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock, executor);
        await manager.EnqueueAsync(Message(GroupA, "a1", "oi"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(3.5));
        await executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await manager.EnqueueAsync(Message(GroupA, "a2", "mais"), TestContext.Current.CancellationToken);
        await manager.EnqueueAsync(Message(GroupB, "b1", "outro grupo"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(3.5));
        var other = await ReadResultAsync(manager);
        Assert.Equal(GroupB.Id, other.Conversation.Id);
        Assert.Equal(2, arbiter.Requests.Count);
        executor.Release.SetResult(new SentConversationTurn("sent-1", "Filomena: oi", clock.UtcNow));
        var sent = await ReadResultAsync(manager);
        Assert.Equal("sent-1", sent.SentTurn?.ProviderMessageId);
        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(3.5));
        _ = await ReadResultAsync(manager);
        Assert.NotNull(arbiter.Requests[^1].Signals.SinceLastFilomenaTurn);
        Assert.Single(arbiter.Requests[^1].RecentOutbound);
        Assert.Single(executor.Requests);
    }

    private sealed class BlockingExecutor : IConversationTurnExecutor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SentConversationTurn?> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ConversationGenerationRequest> Requests { get; } = [];
        public async Task<SentConversationTurn?> ExecuteAsync(ConversationGenerationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Entered.TrySetResult();
            return await Release.Task.WaitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task FailedExecutionDoesNotKillConversationOrClaimConfirmedHistory()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(
            new TurnDecision(TurnAction.Respond, null, null, TurnReason.UsefulContribution),
            new TurnDecision(TurnAction.Ignore, null, null, TurnReason.NoUsefulContribution));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock, new FailingExecutor());
        await manager.EnqueueAsync(Message(GroupA, "first", "oi"), TestContext.Current.CancellationToken);
        var failed = await ReadResultByAdvancingClockAsync(manager, clock);
        Assert.Equal("IOException", failed.ExecutionError);
        Assert.Null(failed.SentTurn);
        await manager.EnqueueAsync(Message(GroupA, "next", "oi de novo"), TestContext.Current.CancellationToken);
        var next = await ReadResultByAdvancingClockAsync(manager, clock);
        Assert.Null(next.ExecutionError);
        Assert.Null(arbiter.Requests[^1].Signals.SinceLastFilomenaTurn);
        Assert.Empty(arbiter.Requests[^1].RecentOutbound);
    }

    private sealed class FailingExecutor : IConversationTurnExecutor
    {
        public Task<SentConversationTurn?> ExecuteAsync(ConversationGenerationRequest request, CancellationToken cancellationToken) =>
            throw new IOException("test send failure");
    }

    [Fact]
    public async Task AggregatesOneConversationBurstAfterItsQuietWindow()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(new TurnDecision(
            TurnAction.Respond,
            "m3",
            Actor.StableId,
            TurnReason.DirectAddress));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);

        await manager.EnqueueAsync(Message(GroupA, "m1", "@Filomena"), TestContext.Current.CancellationToken);
        await manager.EnqueueAsync(Message(GroupA, "m2", "olha isso"), TestContext.Current.CancellationToken);
        await manager.EnqueueAsync(Message(GroupA, "m3", "o que você acha?"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();

        Assert.Equal(TimeSpan.FromSeconds(1.5), clock.LastRequestedDelay);
        clock.Advance(TimeSpan.FromSeconds(1.5));
        var result = await ReadResultAsync(manager);

        Assert.Equal(["m1", "m2", "m3"], result.PendingMessageIds);
        Assert.Single(arbiter.Requests);
        Assert.Equal(["m1", "m2", "m3"], arbiter.Requests[0].Messages.Select(item => item.ProviderMessageId));
    }

    [Fact]
    public async Task ProcessesConversationsIndependentlyWithoutMixingBuffers()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(new TurnDecision(
            TurnAction.Ignore,
            null,
            null,
            TurnReason.NoUsefulContribution));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);

        await manager.EnqueueAsync(Message(GroupA, "a1", "uma"), TestContext.Current.CancellationToken);
        await manager.EnqueueAsync(Message(GroupB, "b1", "duas"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayCountAsync(2);
        clock.Advance(TimeSpan.FromSeconds(3.5));

        var results = new[] { await ReadResultAsync(manager), await ReadResultAsync(manager) };
        Assert.Equal(2, results.Select(item => item.Conversation.Id.Namespace).Distinct().Count());
        Assert.All(results, result => Assert.Single(result.PendingMessageIds));
        Assert.DoesNotContain(results, result =>
            result.PendingMessageIds.Contains("a1") && result.PendingMessageIds.Contains("b1"));
    }

    [Fact]
    public async Task MessageArrivingDuringArbitrationStartsTheNextBurst()
    {
        var clock = new ManualConversationClock();
        var arbiter = new BlockingArbiter();
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);

        await manager.EnqueueAsync(Message(GroupA, "m1", "primeira"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(3.5));
        await arbiter.WaitUntilCalledAsync();

        await manager.EnqueueAsync(Message(GroupA, "m2", "chegou durante o arbiter"), TestContext.Current.CancellationToken);
        arbiter.Release(new TurnDecision(TurnAction.Ignore, null, null, TurnReason.NoUsefulContribution));
        var first = await ReadResultAsync(manager);
        Assert.Equal(["m1"], first.PendingMessageIds);

        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(3.5));
        var second = await ReadResultAsync(manager);
        Assert.Equal(["m2"], second.PendingMessageIds);
    }

    [Fact]
    public async Task ReevaluatesWaitOnceThenReturnsTheNextDecision()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(
            new TurnDecision(TurnAction.Wait, null, null, TurnReason.ConversationStillFlowing),
            new TurnDecision(TurnAction.Respond, "m1", Actor.StableId, TurnReason.UsefulContribution));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);

        await manager.EnqueueAsync(Message(GroupA, "m1", "@Filomena responde?"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(1.5));
        await arbiter.WaitForCallCountAsync(1);
        await clock.WaitForDelayAsync();
        Assert.Equal(TimeSpan.FromSeconds(2), clock.LastRequestedDelay);

        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await ReadResultAsync(manager);

        Assert.Equal(TurnAction.Respond, result.Decision.Decision);
        Assert.Equal(2, arbiter.Requests.Count);
        Assert.Equal(["m1"], result.PendingMessageIds);
    }

    [Fact]
    public async Task SecondWaitFailsClosedAndDoesNotLeaveAnImmortalTurn()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(
            new TurnDecision(TurnAction.Wait, null, null, TurnReason.ConversationStillFlowing),
            new TurnDecision(TurnAction.Wait, null, null, TurnReason.ConversationStillFlowing));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);

        await manager.EnqueueAsync(Message(GroupA, "m1", "espera"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(3.5));
        await arbiter.WaitForCallCountAsync(1);
        await clock.WaitForDelayAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await ReadResultAsync(manager);

        Assert.Equal(TurnAction.Ignore, result.Decision.Decision);
        Assert.Equal(TurnReason.WaitLimitReached, result.Decision.Reason);
    }

    [Fact]
    public async Task RecentContextHonorsMessageCharacterAndTimeLimits()
    {
        var clock = new ManualConversationClock();
        var options = Options() with
        {
            RecentMessageLimit = 2,
            RecentCharacterLimit = 20,
            RecentWindow = TimeSpan.FromMinutes(5),
        };
        var arbiter = new RecordingArbiter(new TurnDecision(
            TurnAction.Ignore,
            null,
            null,
            TurnReason.NoUsefulContribution));
        await using var manager = new ConversationTurnManager(arbiter, options, clock);

        await manager.EnqueueAsync(Message(GroupA, "old", "fora da janela", clock.UtcNow - TimeSpan.FromMinutes(6)), TestContext.Current.CancellationToken);
        await manager.EnqueueAsync(Message(GroupA, "m1", "1234567890"), TestContext.Current.CancellationToken);
        await manager.EnqueueAsync(Message(GroupA, "m2", "abcdefghij"), TestContext.Current.CancellationToken);
        await manager.EnqueueAsync(Message(GroupA, "m3", "última"), TestContext.Current.CancellationToken);
        _ = await ReadResultByAdvancingClockAsync(manager, clock);

        Assert.Equal(["m2", "m3"], arbiter.Requests.Single().Messages.Select(item => item.ProviderMessageId));
        Assert.True(arbiter.Requests.Single().Messages.Sum(item => item.Content.Length) <= 20);
    }

    [Fact]
    public async Task AllowlistedDirectMessageUsesItsShortQuietWindow()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(new TurnDecision(
            TurnAction.Ignore,
            null,
            null,
            TurnReason.NoUsefulContribution));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);
        var dm = GroupA with
        {
            Id = new ConversationId(
                "whatsapp",
                "default",
                "5511944558218@s.whatsapp.net",
                "whatsapp-dm-5511944558218-s-whatsapp-net"),
            Kind = ConversationKind.DirectMessage,
        };

        await manager.EnqueueAsync(Message(dm, "dm1", "oi"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();

        Assert.Equal(TimeSpan.FromSeconds(1.8), clock.LastRequestedDelay);
    }

    [Fact]
    public async Task ContinuousGroupActivityCannotExtendABurstPastItsMaximum()
    {
        var clock = new ManualConversationClock();
        var arbiter = new RecordingArbiter(new TurnDecision(
            TurnAction.Ignore,
            null,
            null,
            TurnReason.NoUsefulContribution));
        await using var manager = new ConversationTurnManager(arbiter, Options(), clock);

        await manager.EnqueueAsync(Message(GroupA, "m1", "um"), TestContext.Current.CancellationToken);
        await clock.WaitForRequestCountAsync(1);
        for (var index = 2; index <= 4; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            var previousRequestCount = clock.RequestCount;
            await manager.EnqueueAsync(
                Message(GroupA, $"m{index}", $"mensagem {index}"),
                TestContext.Current.CancellationToken);
            await clock.WaitForRequestCountAsync(previousRequestCount + 1);
        }

        Assert.Equal(TimeSpan.FromSeconds(1), clock.LastRequestedDelay);
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await ReadResultAsync(manager);

        Assert.Equal(4, result.PendingMessageIds.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-09-04T18:00:10Z"), result.EvaluatedAt);
    }

    [Fact]
    public async Task DisposalCancelsPendingConversationTimers()
    {
        var clock = new ManualConversationClock();
        var manager = new ConversationTurnManager(
            new RecordingArbiter(),
            Options(),
            clock);
        await manager.EnqueueAsync(Message(GroupA, "m1", "pendente"), TestContext.Current.CancellationToken);
        await clock.WaitForDelayAsync();

        await manager.DisposeAsync();

        Assert.True(manager.Results.Completion.IsCompletedSuccessfully);
    }

    private static readonly ActorRef Actor = new(
        "whatsapp",
        "default",
        "5511944558218@s.whatsapp.net",
        "whatsapp:5511944558218@s.whatsapp.net",
        "Helena");

    private static readonly ConversationInfo GroupA = Conversation(
        "5511981042361-1589823314@g.us",
        "whatsapp-group-5511981042361-1589823314-g-us");

    private static readonly ConversationInfo GroupB = Conversation(
        "120363318973251043@g.us",
        "whatsapp-group-120363318973251043-g-us");

    private static ConversationTurnOptions Options() => new()
    {
        AllowlistedDirectMessageNativeIds = ["5511944558218@s.whatsapp.net"],
        FilomenaAliases = ["@Filomena"],
        AllowlistedDirectMessageQuietWindow = TimeSpan.FromSeconds(1.8),
        AllowlistedDirectMessageMaxBurst = TimeSpan.FromSeconds(6),
        DirectAddressGroupQuietWindow = TimeSpan.FromSeconds(1.5),
        DirectAddressGroupMaxBurst = TimeSpan.FromSeconds(8),
        GroupQuietWindow = TimeSpan.FromSeconds(3.5),
        GroupMaxBurst = TimeSpan.FromSeconds(10),
        WaitReevaluationDelay = TimeSpan.FromSeconds(2),
        MaxWaitReevaluations = 1,
        RecentMessageLimit = 16,
        RecentCharacterLimit = 3000,
        RecentWindow = TimeSpan.FromMinutes(10),
    };

    private static ConversationInfo Conversation(string nativeId, string conversationNamespace) => new(
        new ConversationId("whatsapp", "default", nativeId, conversationNamespace),
        $"agent:main:whatsapp:group:{nativeId}",
        ConversationKind.Group,
        null,
        null,
        null,
        null,
        null);

    private static InboundConversationEvent Message(
        ConversationInfo conversation,
        string id,
        string content,
        DateTimeOffset? timestamp = null) => new(
            id.GetHashCode(StringComparison.Ordinal),
            conversation.SessionKey,
            conversation,
            id,
            content,
            Actor,
            timestamp ?? DateTimeOffset.Parse("2026-09-04T18:00:00Z"),
            null,
            null,
            new Dictionary<string, object?>());

    private static async Task<ConversationTurnResult> ReadResultAsync(ConversationTurnManager manager)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await manager.Results.ReadAsync(timeout.Token);
    }

    private static async Task<ConversationTurnResult> ReadResultByAdvancingClockAsync(
        ConversationTurnManager manager,
        ManualConversationClock clock)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var resultTask = manager.Results.ReadAsync(timeout.Token).AsTask();
        while (!resultTask.IsCompleted)
        {
            var delayReady = clock.WaitForDelayAsync();
            if (await Task.WhenAny(resultTask, delayReady) == resultTask)
            {
                break;
            }
            await delayReady;
            clock.Advance(clock.LastRequestedDelay);
            await Task.Yield();
        }

        return await resultTask;
    }

    private sealed class RecordingArbiter(params TurnDecision[] decisions) : ITurnArbiter
    {
        private readonly Queue<TurnDecision> decisions = new(decisions);
        private readonly TaskCompletionSource callsChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<TurnArbitrationRequest> Requests { get; } = [];

        public Task<TurnDecision> DecideAsync(
            TurnArbitrationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            callsChanged.TrySetResult();
            return Task.FromResult(decisions.Count > 0
                ? decisions.Dequeue()
                : new TurnDecision(TurnAction.Ignore, null, null, TurnReason.NoUsefulContribution));
        }

        public async Task WaitForCallCountAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (Requests.Count < count)
            {
                await Task.Delay(1, timeout.Token);
            }
        }
    }

    private sealed class BlockingArbiter : ITurnArbiter
    {
        private readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<TurnDecision> released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;

        public Task<TurnDecision> DecideAsync(
            TurnArbitrationRequest request,
            CancellationToken cancellationToken)
        {
            callCount++;
            if (callCount == 1)
            {
                called.TrySetResult();
                return released.Task.WaitAsync(cancellationToken);
            }

            return Task.FromResult(new TurnDecision(
                TurnAction.Ignore,
                null,
                null,
                TurnReason.NoUsefulContribution));
        }

        public Task WaitUntilCalledAsync() => called.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void Release(TurnDecision decision) => released.TrySetResult(decision);
    }

    private sealed class ManualConversationClock : IConversationClock
    {
        private readonly object gate = new();
        private readonly List<ScheduledDelay> delays = [];

        public DateTimeOffset UtcNow { get; private set; } =
            DateTimeOffset.Parse("2026-09-04T18:00:00Z");

        public TimeSpan LastRequestedDelay { get; private set; }

        public int RequestCount { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                LastRequestedDelay = delay;
                RequestCount++;
                var scheduled = new ScheduledDelay(UtcNow + delay);
                if (cancellationToken.CanBeCanceled)
                {
                    scheduled.Registration = cancellationToken.Register(
                        () => scheduled.Completion.TrySetCanceled(cancellationToken));
                }

                delays.Add(scheduled);
                return scheduled.Completion.Task;
            }
        }

        public void Advance(TimeSpan by)
        {
            ScheduledDelay[] due;
            lock (gate)
            {
                UtcNow += by;
                due = delays.Where(item => item.DueAt <= UtcNow).ToArray();
                foreach (var item in due)
                {
                    delays.Remove(item);
                }
            }

            foreach (var item in due)
            {
                item.Registration.Dispose();
                item.Completion.TrySetResult();
            }
        }

        public Task WaitForDelayAsync() => WaitForDelayCountAsync(1);

        public async Task WaitForRequestCountAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (RequestCount < count)
            {
                await Task.Delay(1, timeout.Token);
            }
        }

        public async Task WaitForDelayCountAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (true)
            {
                lock (gate)
                {
                    delays.RemoveAll(item => item.Completion.Task.IsCompleted);
                    if (delays.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(1, timeout.Token);
            }
        }

        private sealed class ScheduledDelay(DateTimeOffset dueAt)
        {
            public DateTimeOffset DueAt { get; } = dueAt;

            public TaskCompletionSource Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public CancellationTokenRegistration Registration { get; set; }
        }
    }
}
