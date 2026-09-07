using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class SessionFlowRuntimeTests
{
    [Fact]
    public async Task OpenCountBatchKeepsItsProjectEntryRouteAcrossAStateTransition()
    {
        var key = SessionSliceAcceptanceSupport.Key("count-routing-boundary");
        var completion = new RecordingCompletion(
            [
                SessionSliceAcceptanceSupport.Response("atenta"),
                SessionSliceAcceptanceSupport.Response("inativa", "calar"),
                SessionSliceAcceptanceSupport.Response("atenta", "calar"),
            ],
            blockAtCall: 1);
        await using var runtime = new SessionFlowRuntime(new FakeTimeProvider(), completion);
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("routing-r1", root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, null, 3, null, "routing-r1");
            SessionSliceAcceptanceSupport.RenameAccumulator(root, "inativa", "accumulate-inativa");
            SessionSliceAcceptanceSupport.RenameAccumulator(root, "atenta", "accumulate-atenta");
        }));

        foreach (var id in new[] { "seed-1", "seed-2", "seed-3" })
            await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, id, SessionEntryKind.Group);
        await completion.BlockStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-1", SessionEntryKind.Group);
        completion.Release();
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal("atenta", runtime.ReadSession(key)!.CurrentStateId);
        Assert.Equal(1, runtime.ReadSession(key)!.StateVersion);

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-2", SessionEntryKind.Group);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-3", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        foreach (var id in new[] { "next-1", "next-2", "next-3" })
            await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, id, SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var outcomes = runtime.ReadOutcomes(key);
        Assert.Equal(3, outcomes.Count);
        Assert.Equal(["seed-1", "seed-2", "seed-3"], outcomes[0].ProviderMessageIds);
        Assert.Equal(["old-1", "old-2", "old-3"], outcomes[1].ProviderMessageIds);
        Assert.Equal(["next-1", "next-2", "next-3"], outcomes[2].ProviderMessageIds);
        Assert.Equal(9, outcomes.SelectMany(item => item.ProviderMessageIds).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("inativa", outcomes[1].StateId);
        Assert.Equal(0, outcomes[1].StateVersion);
        Assert.Equal(
            ["graph:inativa:routing-r1", "graph:inativa:routing-r1", "graph:atenta:routing-r1"],
            completion.Requests.Select(request => request.System));

        var divergentTrace = runtime.ReadTrace(key).Single(item =>
            item.BatchId == outcomes[1].BatchId && item.EventName == "batch-closed");
        Assert.Equal("inativa", divergentTrace.BatchStateId);
        Assert.Equal(0L, divergentTrace.BatchStateVersion);
        Assert.Equal("atenta", divergentTrace.CurrentSessionStateId);
        Assert.Equal(1L, divergentTrace.CurrentSessionStateVersion);
    }

    [Fact]
    public async Task OpenBatchIsAdmittedBeforeValidatingEntryOnTheNewState()
    {
        var key = SessionSliceAcceptanceSupport.Key("missing-new-entry");
        var completion = new RecordingCompletion(
            [
                SessionSliceAcceptanceSupport.Response("atenta"),
                SessionSliceAcceptanceSupport.Response("inativa", "calar"),
            ],
            blockAtCall: 1);
        await using var runtime = new SessionFlowRuntime(new FakeTimeProvider(), completion);
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("missing-entry-r1", root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, null, 3, null);
            RemoveEntry(SessionSliceAcceptanceSupport.Graph(SessionSliceAcceptanceSupport.State(root, "atenta")), "group-in");
        }));

        foreach (var id in new[] { "seed-1", "seed-2", "seed-3" })
            await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, id, SessionEntryKind.Group);
        await completion.BlockStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-1", SessionEntryKind.Group);
        completion.Release();
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal("atenta", runtime.ReadSession(key)!.CurrentStateId);

        var second = await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-2", SessionEntryKind.Group);
        var third = await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-3", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(SessionAdmissionStatus.Accepted, second.Status);
        Assert.Equal(SessionAdmissionStatus.Accepted, third.Status);
        Assert.Contains(runtime.ReadOutcomes(key), outcome =>
            outcome.ProviderMessageIds.SequenceEqual(["old-1", "old-2", "old-3"]));

        var next = await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "new-state", SessionEntryKind.Group);
        Assert.Equal(SessionAdmissionStatus.Rejected, next.Status);
        Assert.Equal("entry-not-available", next.Reason);
    }

    [Fact]
    public async Task AdministrativePublicationDoesNotJoinAnOlderProjectBuffer()
    {
        var key = SessionSliceAcceptanceSupport.Key("project-buffer-boundary");
        await using var runtime = new SessionFlowRuntime(
            new FakeTimeProvider(),
            new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]));
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project(
            "admin-r1",
            root => SessionSliceAcceptanceSupport.Configure(root, null, 3, null),
            version: 1));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "r1-open", SessionEntryKind.Group);

        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project(
            "admin-r2",
            root => SessionSliceAcceptanceSupport.Configure(root, null, 2, null),
            version: 2));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "r2-1", SessionEntryKind.Group);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "r2-2", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var outcome = Assert.Single(runtime.ReadOutcomes(key));
        Assert.Equal("admin-r2", outcome.ProjectRevision);
        Assert.Equal(["r2-1", "r2-2"], outcome.ProviderMessageIds);
        Assert.Equal(1, runtime.ReadSession(key)!.OpenBufferCount);
    }

    [Fact]
    public async Task QueuedCallbackFromClosedBufferCannotCloseReplacementWithSameKey()
    {
        var time = new ControlledTimerProvider();
        var key = SessionSliceAcceptanceSupport.Key("stale-timer");
        await using var runtime = new SessionFlowRuntime(
            time,
            new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]));
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("stale-timer-r1", root =>
            SessionSliceAcceptanceSupport.Configure(root, 1, null, null)));

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-buffer", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(1));
        time.FireQueuedCallback(0);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Single(runtime.ReadOutcomes(key));

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "new-buffer", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(1));
        time.FireQueuedCallback(0);

        Assert.Single(runtime.ReadOutcomes(key));
        Assert.Equal(1, runtime.ReadSession(key)!.OpenBufferCount);
    }

    [Fact]
    public async Task PrematureQuietCallbackReschedulesAgainstLatestAdmission()
    {
        var time = new ControlledTimerProvider();
        var key = SessionSliceAcceptanceSupport.Key("quiet-reset");
        await using var runtime = new SessionFlowRuntime(
            time,
            new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]));
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("quiet-reset-r1", root =>
            SessionSliceAcceptanceSupport.Configure(root, 5, null, null)));

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "quiet-1", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(4));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "quiet-2", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(1));
        time.FireQueuedCallback(0);

        Assert.Empty(runtime.ReadOutcomes(key));
        Assert.Equal(1, runtime.ReadSession(key)!.OpenBufferCount);

        time.Advance(TimeSpan.FromSeconds(4));
        time.FireQueuedCallback(0);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var outcome = Assert.Single(runtime.ReadOutcomes(key));
        Assert.Equal(SessionBatchCloseReason.QuietPeriod, outcome.CloseReason);
        Assert.Equal(["quiet-1", "quiet-2"], outcome.ProviderMessageIds);
    }

    [Fact]
    public async Task TraceRedactsArbitraryExecutionErrorsThatLookLikeExceptionNames()
    {
        const string secret = "private-completion-detail-Exception";
        var key = new SessionKey("runtime", "filomena", "whatsapp", "default", "redacted-error");
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new ThrowingCompletion(secret));
        runtime.UpdateProject(new SessionFlowProject(
            "filomena",
            1,
            "redaction-r1",
            Project("inactive", ("inactive", PromptGraph("inactive")))));

        await runtime.ReceiveAsync(key, Message(key, "redaction-message"), TestContext.Current.CancellationToken);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var trace = runtime.ReadTrace(key);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(trace));
        Assert.Equal("flow-execution-failed", trace.Last(item => item.EventName == "execution-failed").ErrorCode);
    }

    [Fact]
    public async Task ThroughCurrentBatchHistoryStopsAtThatBatchAdmissionWatermark()
    {
        var time = new FakeTimeProvider();
        var key = SessionSliceAcceptanceSupport.Key("through-watermark");
        await using var runtime = new SessionFlowRuntime(
            time,
            new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]));
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("through-r1", root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, 2, null, null);
            foreach (var state in SessionSliceAcceptanceSupport.States(root))
                SessionSliceAcceptanceSupport.Node(SessionSliceAcceptanceSupport.Graph(state), "history")["settings"]!["mode"] = "through-current-batch";
        }));

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-batch", SessionEntryKind.Group, "old batch");
        time.Advance(TimeSpan.FromSeconds(1));
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("through-r2", root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, 10, null, null);
            foreach (var state in SessionSliceAcceptanceSupport.States(root))
                SessionSliceAcceptanceSupport.Node(SessionSliceAcceptanceSupport.Graph(state), "history")["settings"]!["mode"] = "through-current-batch";
        }, version: 2));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "later-batch", SessionEntryKind.Group, "later batch");
        time.Advance(TimeSpan.FromSeconds(1));
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var oldBatch = Assert.Single(runtime.ReadOutcomes(key));
        Assert.Equal("through-r1", oldBatch.ProjectRevision);
        Assert.Equal(["old-batch"], oldBatch.ProviderMessageIds);
        var history = SessionSliceAcceptanceSupport.Texts(oldBatch.Outputs["history"]["messages"]);
        Assert.Equal(["old batch"], history);
        Assert.Equal(1, runtime.ReadSession(key)!.OpenBufferCount);
    }

    [Fact]
    public async Task DisposePublishesOneTaskBeforeCancellationCallbackReentersRuntime()
    {
        var key = new SessionKey("runtime", "filomena", "whatsapp", "default", "dispose-reentry");
        SessionFlowRuntime? runtime = null;
        var completion = new ReentrantCancellationCompletion(
            () => runtime!.ReadSession(key),
            () => runtime!.DisposeAsync().AsTask());
        runtime = new SessionFlowRuntime(TimeProvider.System, completion);
        runtime.UpdateProject(new SessionFlowProject(
            "filomena",
            1,
            "dispose-r1",
            Project("inactive", ("inactive", PromptGraph("engaged")), ("engaged", Graph("engaged")))));

        await runtime.ReceiveAsync(key, Message(key, "dispose-message"), TestContext.Current.CancellationToken);
        await completion.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var disposal = runtime.DisposeAsync().AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Same(disposal, completion.ReentrantDisposal);
        Assert.NotNull(completion.SnapshotReadDuringCancellation);
        var outcome = Assert.Single(runtime.ReadOutcomes(key));
        Assert.False(outcome.Success);
        Assert.False(outcome.TransitionApplied);
        Assert.Equal("inactive", runtime.ReadSession(key)!.CurrentStateId);
    }

    [Fact]
    public async Task ProjectPublicationRequiresIncreasingVersionAndUniqueRevision()
    {
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new NoCompletion());
        var document = Project("inactive", ("inactive", Graph("inactive")));
        runtime.UpdateProject(new SessionFlowProject("filomena", 2, "rev-2", document));

        Assert.Throws<InvalidOperationException>(() =>
            runtime.UpdateProject(new SessionFlowProject("filomena", 2, "rev-3", document)));
        Assert.Throws<InvalidOperationException>(() =>
            runtime.UpdateProject(new SessionFlowProject("filomena", 3, "rev-2", document)));
    }

    [Fact]
    public async Task AcceptedInboundMessageRunsImmediatelyAndAppliesSuccessfulExit()
    {
        var key = new SessionKey("runtime", "filomena", "whatsapp", "default", "conversation");
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new NoCompletion());
        runtime.UpdateProject(new SessionFlowProject(
            "filomena",
            1,
            "rev-1",
            Project("inactive", ("inactive", Graph("engaged")), ("engaged", Graph("engaged")))));

        var admission = await runtime.ReceiveAsync(key, Message(key, "message-1"), TestContext.Current.CancellationToken);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(SessionAdmissionStatus.Accepted, admission.Status);
        Assert.Equal(1, admission.AdmissionWatermark);
        var session = Assert.IsType<SessionSnapshot>(runtime.ReadSession(key));
        Assert.Equal("engaged", session.CurrentStateId);
        Assert.Equal(1, session.StateVersion);
        var outcome = Assert.Single(runtime.ReadOutcomes(key));
        Assert.True(outcome.Success);
        Assert.True(outcome.TransitionApplied);
        Assert.Equal("engaged", outcome.TransitionTargetStateId);
        Assert.Equal(["message-1"], outcome.ProviderMessageIds);
    }

    [Fact]
    public async Task UpdateProjectCompilesAndPublishesVersionedImmutableSnapshot()
    {
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new NoCompletion());
        var document = Project("inactive", ("inactive", Graph("inactive")), ("engaged", Graph("engaged")));

        var snapshot = runtime.UpdateProject(new SessionFlowProject("filomena", 17, "rev-17", document));

        Assert.Equal("filomena", snapshot.ProjectId);
        Assert.Equal(17, snapshot.ProjectVersion);
        Assert.Equal("rev-17", snapshot.ProjectRevision);
        Assert.Equal("inactive", snapshot.InitialStateId);
        Assert.Equal(["inactive", "engaged"], snapshot.StateIds);
    }

    private static JsonElement Project(string initialStateId, params (string Id, JsonElement Graph)[] states) =>
        JsonSerializer.SerializeToElement(new
        {
            format = "filomena-local-project",
            project = new
            {
                version = 1,
                stateScope = "session",
                initialStateId,
                states = states.Select(state => new { id = state.Id, name = state.Id, graph = state.Graph }).ToArray(),
            },
        });

    private static JsonElement Graph(string targetStateId) => JsonSerializer.SerializeToElement(new
    {
        version = 3,
        nodes = new object[]
        {
            new { id = "dm", kind = "dm" },
            new { id = "exit", kind = "exit", settings = new { mode = "switch", targetStateId } },
        },
        edges = Array.Empty<object>(),
        controlEdges = new[]
        {
            new { source = "dm", sourcePort = "then", target = "exit", targetPort = "exec" },
        },
    });

    private static JsonElement PromptGraph(string targetStateId) => JsonSerializer.SerializeToElement(new
    {
        version = 3,
        nodes = new object[]
        {
            new { id = "dm", kind = "dm" },
            new
            {
                id = "prompt",
                kind = "prompt",
                prompt = "Return one message.",
                inputs = Array.Empty<object>(),
                responseFields = Array.Empty<object>(),
            },
            new { id = "exit", kind = "exit", settings = new { mode = "switch", targetStateId } },
        },
        edges = Array.Empty<object>(),
        controlEdges = new object[]
        {
            new { source = "dm", sourcePort = "then", target = "prompt", targetPort = "exec" },
            new { source = "prompt", sourcePort = "then", target = "exit", targetPort = "exec" },
        },
    });

    private static SessionMessageEnvelope Message(SessionKey key, string id) => new(
        id,
        key.ChannelId,
        key.AccountId,
        key.NativeConversationId,
        DateTimeOffset.Parse("2026-09-07T00:00:00Z"),
        SessionMessageDirection.Inbound,
        SessionEntryKind.Dm,
        JsonSerializer.SerializeToElement(new
        {
            texto = "hello",
            remetente = new { nome = "Helena", stableId = "fixture:helena" },
            canal = new { nome = key.ChannelId, nativeId = key.NativeConversationId },
            reply = (object?)null,
        }));

    private static void RemoveEntry(System.Text.Json.Nodes.JsonObject graph, string entryId)
    {
        var nodes = graph["nodes"]!.AsArray();
        nodes.Remove(nodes.Single(node => node!["id"]!.GetValue<string>() == entryId));
        foreach (var collectionName in new[] { "edges", "controlEdges" })
        {
            var edges = graph[collectionName]!.AsArray();
            foreach (var edge in edges.Where(item =>
                         item!["source"]!.GetValue<string>() == entryId ||
                         item["target"]!.GetValue<string>() == entryId).ToArray())
                edges.Remove(edge);
        }
    }

    private sealed class NoCompletion : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            throw new InvalidOperationException("No completion expected.");
    }

    private sealed class ThrowingCompletion(string message) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            throw new InvalidOperationException(message);
    }

    private sealed class ReentrantCancellationCompletion(
        Func<SessionSnapshot?> readSnapshot,
        Func<Task> disposeAgain) : IFlowCompletion
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SessionSnapshot? SnapshotReadDuringCancellation { get; private set; }
        public Task? ReentrantDisposal { get; private set; }

        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            using var registration = token.Register(() =>
            {
                SnapshotReadDuringCancellation = readSnapshot();
                ReentrantDisposal = disposeAgain();
            });
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return "never";
        }
    }

    private sealed class ControlledTimerProvider(DateTimeOffset? start = null) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ControlledTimer> timers = [];
        private DateTimeOffset now = start ?? DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate) return now;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (gate) return now.UtcDateTime.Ticks;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(this, callback, state, dueTime, period);
            lock (gate) timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            lock (gate) now += elapsed;
        }

        public void FireQueuedCallback(int timerIndex)
        {
            TimerCallback callback;
            object? state;
            lock (gate)
            {
                callback = timers[timerIndex].Callback;
                state = timers[timerIndex].State;
            }
            callback(state);
        }

        private sealed class ControlledTimer(
            ControlledTimerProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) : ITimer
        {
            private bool disposed;

            internal TimerCallback Callback { get; } = callback;
            internal object? State { get; } = state;
            internal DateTimeOffset Due { get; private set; } =
                dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.GetUtcNow() + dueTime;
            internal TimeSpan Period { get; private set; } = period;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.gate)
                {
                    if (disposed) return false;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.now + dueTime;
                    Period = period;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate) disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
