using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class SessionSliceAcceptanceTests
{
    [Fact]
    public void FixtureUsesStableStateIdsAndOnlyTheApprovedSliceTwoNodes()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var project = document.RootElement.GetProperty("project");
        var states = project.GetProperty("states").EnumerateArray().ToArray();

        Assert.Equal(["inativa", "atenta", "engajada"], states.Select(state => state.GetProperty("id").GetString()));
        Assert.Equal("inativa", project.GetProperty("initialStateId").GetString());
        Assert.All(states, state => Assert.NotEqual(state.GetProperty("id").GetString(), state.GetProperty("name").GetString()));

        foreach (var state in states)
        {
            var graph = state.GetProperty("graph");
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToArray();
            Assert.Equal(["dm-in", "group-in", "accumulate", "history", "ai", "switch", "exit"], nodes.Select(node => node.GetProperty("id").GetString()));
            Assert.DoesNotContain(nodes, node => node.GetProperty("kind").GetString() is "send" or "input");

            var accumulator = Node(nodes, "accumulate").GetProperty("settings");
            Assert.Equal(2, accumulator.GetProperty("quietSeconds").GetInt32());
            Assert.Equal(3, accumulator.GetProperty("maxMessages").GetInt32());
            Assert.Equal(6, accumulator.GetProperty("maxWaitSeconds").GetInt32());

            var history = Node(nodes, "history").GetProperty("settings");
            Assert.Equal("before-current-batch", history.GetProperty("mode").GetString());
            Assert.Equal(JsonValueKind.Null, history.GetProperty("maxAgeMinutes").ValueKind);
            Assert.False(history.GetProperty("includeCharacterMessages").GetBoolean());

            var exit = Node(nodes, "exit").GetProperty("settings");
            Assert.Equal("switch", exit.GetProperty("mode").GetString());
            Assert.Equal("inativa", exit.GetProperty("stateMapping").GetProperty("inativa").GetString());
            Assert.Equal("atenta", exit.GetProperty("stateMapping").GetProperty("atenta").GetString());
            Assert.Equal("engajada", exit.GetProperty("stateMapping").GetProperty("engajada").GetString());
            Assert.DoesNotContain(graph.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("target").GetString() == "history");
            Assert.Equal(["accumulate"], graph.GetProperty("edges").EnumerateArray()
                .Where(edge => edge.GetProperty("target").GetString() == "ai" && edge.GetProperty("targetPort").GetString() == "batch")
                .Select(edge => edge.GetProperty("source").GetString()));
            Assert.Equal(["dm-in", "group-in"], graph.GetProperty("edges").EnumerateArray()
                .Where(edge => edge.GetProperty("target").GetString() == "accumulate" && edge.GetProperty("targetPort").GetString() == "message")
                .Select(edge => edge.GetProperty("source").GetString()));
            Assert.Contains(graph.GetProperty("edges").EnumerateArray(), edge =>
                edge.GetProperty("source").GetString() == "ai" && edge.GetProperty("sourcePort").GetString() == "next-state" &&
                edge.GetProperty("target").GetString() == "exit" && edge.GetProperty("targetPort").GetString() == "state");
            Assert.Contains(graph.GetProperty("controlEdges").EnumerateArray(), edge =>
                edge.GetProperty("source").GetString() == "switch" && edge.GetProperty("sourcePort").GetString() == "respond" &&
                edge.GetProperty("target").GetString() == "exit" && edge.GetProperty("targetPort").GetString() == "exec");
        }
    }

    [Fact]
    public void FixtureCompilesEveryStableStateThroughTypedFlows()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var project = document.RootElement.GetProperty("project");

        Assert.Equal("inativa", project.GetProperty("initialStateId").GetString());
        foreach (var state in project.GetProperty("states").EnumerateArray())
        {
            var graph = state.GetProperty("graph").Clone();
            foreach (var entryId in new[] { "dm-in", "group-in" })
            {
                var flow = TypedFlowCompiler.Compile(graph, entryId);

                Assert.Equal(entryId, flow.EntryId);
                Assert.Equal(FlowDomain.Message, flow.Nodes[entryId].Outputs.Single().Type);
                Assert.Contains("accumulate", flow.Nodes.Keys);
                Assert.Contains("history", flow.Nodes.Keys);
                Assert.Contains("ai", flow.Nodes.Keys);
                Assert.Contains("switch", flow.Nodes.Keys);
                Assert.Contains("exit", flow.Nodes.Keys);
            }
        }
    }

    [Fact]
    public async Task SessionsRemainIsolatedAndNextAdmissionUsesTheTransitionedStateGraph()
    {
        var time = new FakeTimeProvider();
        var drift = SessionSliceAcceptanceSupport.Key("drift");
        var plex = SessionSliceAcceptanceSupport.Key("plex");
        var dm = SessionSliceAcceptanceSupport.Key("dm", "dm");
        var completions = new Dictionary<SessionKey, RecordingCompletion>
        {
            [drift] = new([SessionSliceAcceptanceSupport.Response("engajada"), SessionSliceAcceptanceSupport.Response("atenta")]),
            [plex] = new([SessionSliceAcceptanceSupport.Response("inativa"), SessionSliceAcceptanceSupport.Response("inativa")]),
            [dm] = new([SessionSliceAcceptanceSupport.Response("atenta"), SessionSliceAcceptanceSupport.Response("engajada")]),
        };
        await using var runtime = new SessionFlowRuntime(time, key => completions[key]);
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("r1", root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null, "r1")));

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, drift, "drift-1", SessionEntryKind.Group);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, plex, "plex-1", SessionEntryKind.Group);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, dm, "dm-1", SessionEntryKind.Dm);
        await Idle(runtime, drift, plex, dm);
        Assert.Equal("engajada", runtime.ReadSession(drift)!.CurrentStateId);
        Assert.Equal("inativa", runtime.ReadSession(plex)!.CurrentStateId);
        Assert.Equal("atenta", runtime.ReadSession(dm)!.CurrentStateId);

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, drift, "drift-2", SessionEntryKind.Group);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, plex, "plex-2", SessionEntryKind.Group);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, dm, "dm-2", SessionEntryKind.Dm);
        await Idle(runtime, drift, plex, dm);
        Assert.Equal(["graph:inativa:r1", "graph:engajada:r1"], completions[drift].Requests.Select(request => request.System));
        Assert.Equal(["graph:inativa:r1", "graph:inativa:r1"], completions[plex].Requests.Select(request => request.System));
        Assert.Equal(["graph:inativa:r1", "graph:atenta:r1"], completions[dm].Requests.Select(request => request.System));
    }

    [Fact]
    public async Task RestartReturnsToProjectInitialState()
    {
        var time = new FakeTimeProvider();
        var key = SessionSliceAcceptanceSupport.Key("restart");
        await using (var first = new SessionFlowRuntime(time, new RecordingCompletion([SessionSliceAcceptanceSupport.Response("engajada")])))
        {
            first.UpdateProject(SessionSliceAcceptanceSupport.Project("r1", root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null)));
            await SessionSliceAcceptanceSupport.ReceiveAsync(first, key, "before", SessionEntryKind.Group);
            await first.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
            Assert.Equal("engajada", first.ReadSession(key)!.CurrentStateId);
        }

        await using var restarted = new SessionFlowRuntime(time, new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa")]));
        restarted.UpdateProject(SessionSliceAcceptanceSupport.Project("r1", root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null)));
        await SessionSliceAcceptanceSupport.ReceiveAsync(restarted, key, "after", SessionEntryKind.Group);
        await restarted.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal("inativa", Assert.Single(restarted.ReadOutcomes(key)).StateId);
    }

    [Fact]
    public async Task FirstThresholdWinsForQuietThenMaxWaitOnTheNextBatch()
    {
        var time = new FakeTimeProvider();
        var key = SessionSliceAcceptanceSupport.Key("quiet");
        await using var runtime = Start(time, new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa"), SessionSliceAcceptanceSupport.Response("inativa")]), root => SessionSliceAcceptanceSupport.Configure(root, 2, 5, 5));

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "q1", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(.5)); await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "q2", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(.5)); await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "q3", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(1.5)); Assert.Empty(runtime.ReadOutcomes(key));
        time.Advance(TimeSpan.FromSeconds(.5)); await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var outcome = Assert.Single(runtime.ReadOutcomes(key));
        Assert.Equal(SessionBatchCloseReason.QuietPeriod, outcome.CloseReason);
        Assert.Equal(["q1", "q2", "q3"], outcome.ProviderMessageIds);

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "w1", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(1.5)); await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "w2", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(1.5)); await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "w3", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(1.5)); await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "w4", SessionEntryKind.Group);
        time.Advance(TimeSpan.FromSeconds(.5)); await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        var maxWait = runtime.ReadOutcomes(key)[1];
        Assert.Equal(SessionBatchCloseReason.MaxWait, maxWait.CloseReason);
        Assert.Equal(["w1", "w2", "w3", "w4"], maxWait.ProviderMessageIds);
    }

    [Fact]
    public async Task MaxMessagesReleasesExactlyOnceAtItsFirstThreshold()
    {
        var countTime = new FakeTimeProvider();
        var countKey = SessionSliceAcceptanceSupport.Key("count");
        await using (var count = Start(countTime, new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa")]), root => SessionSliceAcceptanceSupport.Configure(root, 10, 3, 20)))
        {
            await SessionSliceAcceptanceSupport.ReceiveAsync(count, countKey, "c1", SessionEntryKind.Group);
            await SessionSliceAcceptanceSupport.ReceiveAsync(count, countKey, "c2", SessionEntryKind.Group);
            await SessionSliceAcceptanceSupport.ReceiveAsync(count, countKey, "c3", SessionEntryKind.Group);
            await count.WaitForIdleAsync(countKey, TestContext.Current.CancellationToken);
            Assert.Equal(SessionBatchCloseReason.MaxMessages, Assert.Single(count.ReadOutcomes(countKey)).CloseReason);
            countTime.Advance(TimeSpan.FromSeconds(30)); await count.WaitForIdleAsync(countKey, TestContext.Current.CancellationToken);
            Assert.Single(count.ReadOutcomes(countKey));
        }

    }

    [Fact]
    public async Task DeduplicationUsesProviderIdentityAndKeepsDistinctIdenticalText()
    {
        var key = SessionSliceAcceptanceSupport.Key("dedupe");
        await using var runtime = Start(new FakeTimeProvider(), new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa")]), root => SessionSliceAcceptanceSupport.Configure(root, null, 3, null));
        var first = await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "same-id", SessionEntryKind.Group, "same text");
        var duplicate = await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "same-id", SessionEntryKind.Group, "different text");
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "different-id", SessionEntryKind.Group, "same text");
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "third-id", SessionEntryKind.Group, "same text");
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(SessionAdmissionStatus.Accepted, first.Status);
        Assert.Equal(SessionAdmissionStatus.Duplicate, duplicate.Status);
        Assert.Null(duplicate.AdmissionWatermark);
        Assert.Equal(["same-id", "different-id", "third-id"], Assert.Single(runtime.ReadOutcomes(key)).ProviderMessageIds);
    }

    [Fact]
    public async Task OpenAccumulatorBatchKeepsItsStateSnapshotUntilCountBoundary()
    {
        var key = SessionSliceAcceptanceSupport.Key("two-buffers");
        var completion = new RecordingCompletion(
            [
                SessionSliceAcceptanceSupport.Response("atenta"),
                SessionSliceAcceptanceSupport.Response("inativa", "calar"),
                SessionSliceAcceptanceSupport.Response("atenta", "calar"),
            ],
            blockAtCall: 1);
        await using var runtime = Start(new FakeTimeProvider(), completion, root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, null, 3, null, "routing");
            SessionSliceAcceptanceSupport.RenameAccumulator(root, "inativa", "accumulate-inativa");
            SessionSliceAcceptanceSupport.RenameAccumulator(root, "atenta", "accumulate-atenta");
        });

        foreach (var id in new[] { "seed-1", "seed-2", "seed-3" })
            await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, id, SessionEntryKind.Group);
        await completion.BlockStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-1", SessionEntryKind.Group);
        completion.Release();
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal("atenta", runtime.ReadSession(key)!.CurrentStateId);

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-2", SessionEntryKind.Group);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-3", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        foreach (var id in new[] { "next-1", "next-2", "next-3" })
            await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, id, SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var outcomes = runtime.ReadOutcomes(key);
        Assert.Equal(["old-1", "old-2", "old-3"], outcomes[1].ProviderMessageIds);
        Assert.Equal("inativa", outcomes[1].StateId);
        Assert.Equal(["graph:inativa:routing", "graph:inativa:routing", "graph:atenta:routing"],
            completion.Requests.Select(request => request.System));
        Assert.Equal(9, outcomes.SelectMany(outcome => outcome.ProviderMessageIds).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ABlockedBatchDoesNotAbsorbLaterAdmissionAndHistoryIsFrozenAtAdmission()
    {
        var key = SessionSliceAcceptanceSupport.Key("history");
        var completion = new RecordingCompletion(
            [SessionSliceAcceptanceSupport.Response("inativa"), SessionSliceAcceptanceSupport.Response("inativa")],
            blockAtCall: 2);
        await using var runtime = Start(new FakeTimeProvider(), completion, root => SessionSliceAcceptanceSupport.Configure(root, null, 2, null));

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-1", SessionEntryKind.Group, "old1");
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "old-2", SessionEntryKind.Group, "old2");
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        var outbound = await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "character", SessionEntryKind.Group, "character", SessionMessageDirection.Outbound);
        Assert.Equal(SessionAdmissionStatus.Accepted, outbound.Status);
        Assert.Single(runtime.ReadOutcomes(key));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "batch-1", SessionEntryKind.Group, "batch1");
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "batch-2", SessionEntryKind.Group, "batch2");
        await completion.BlockStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "new", SessionEntryKind.Group, "newDuringExecution");
        completion.Release();
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var current = runtime.ReadOutcomes(key).Single(outcome => outcome.ProviderMessageIds.SequenceEqual(["batch-1", "batch-2"]));
        Assert.Equal(["old1", "old2"], SessionSliceAcceptanceSupport.Texts(current.Outputs["history"]["messages"]));
        Assert.Equal(["batch1", "batch2"], completion.Requests.ElementAt(1).Inputs.GetProperty("lote").EnumerateArray().Select(message => message.GetProperty("texto").GetString()));
        Assert.DoesNotContain("newDuringExecution", SessionSliceAcceptanceSupport.Texts(current.Outputs["history"]["messages"]));
    }

    [Fact]
    public async Task BlockedSessionDoesNotBlockAnotherAndAdmittedBatchesKeepTheirProjectSnapshot()
    {
        var time = new FakeTimeProvider();
        var drift = SessionSliceAcceptanceSupport.Key("blocked-drift");
        var plex = SessionSliceAcceptanceSupport.Key("moving-plex");
        var driftCompletion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("engajada"), SessionSliceAcceptanceSupport.Response("atenta"), SessionSliceAcceptanceSupport.Response("inativa")], blockAtCall: 1);
        var plexCompletion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa")]);
        await using var runtime = new SessionFlowRuntime(time, key => key == drift ? driftCompletion : plexCompletion);
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("r1", root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null, "r1"), version: 1));

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, drift, "a", SessionEntryKind.Group);
        await driftCompletion.BlockStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, drift, "b", SessionEntryKind.Group);
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("r2", root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null, "r2"), version: 2));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, plex, "plex", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(plex, TestContext.Current.CancellationToken);
        Assert.Single(runtime.ReadOutcomes(plex));
        Assert.Empty(runtime.ReadOutcomes(drift));

        driftCompletion.Release();
        await runtime.WaitForIdleAsync(drift, TestContext.Current.CancellationToken);
        var stale = runtime.ReadOutcomes(drift)[1];
        Assert.Equal(["graph:inativa:r1", "graph:inativa:r1"], driftCompletion.Requests.Take(2).Select(request => request.System));
        Assert.All(runtime.ReadOutcomes(drift), outcome => Assert.Equal("r1", outcome.ProjectRevision));
        Assert.Equal("atenta", stale.TransitionTargetStateId);
        Assert.False(stale.TransitionApplied);
        Assert.Equal("engajada", runtime.ReadSession(drift)!.CurrentStateId);
        Assert.Equal(1, driftCompletion.MaxConcurrent);

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, drift, "c", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(drift, TestContext.Current.CancellationToken);
        Assert.Equal("graph:engajada:r2", driftCompletion.Requests.Last().System);
    }

    [Fact]
    public async Task ActionOutcomesStayLocalUntilResponderReachesTheEnumMappedExit()
    {
        var time = new FakeTimeProvider();
        var quiet = SessionSliceAcceptanceSupport.Key("quiet-action");
        var wait = SessionSliceAcceptanceSupport.Key("wait-action");
        var respond = SessionSliceAcceptanceSupport.Key("respond-action");
        var completions = new Dictionary<SessionKey, RecordingCompletion>
        {
            [quiet] = new([SessionSliceAcceptanceSupport.Response("engajada", "calar")]),
            [wait] = new([SessionSliceAcceptanceSupport.Response("engajada", "esperar")]),
            [respond] = new([SessionSliceAcceptanceSupport.Response("engajada", "responder")]),
        };
        await using var runtime = new SessionFlowRuntime(time, key => completions[key]);
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("r1", root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null)));

        foreach (var (key, entry) in new[] { (quiet, SessionEntryKind.Group), (wait, SessionEntryKind.Group), (respond, SessionEntryKind.Group) })
        {
            await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, key.NativeConversationId, entry);
            await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        }
        Assert.Equal("calar", runtime.ReadOutcomes(quiet).Single().Outputs["ai"]["action"].GetString());
        Assert.Equal("esperar", runtime.ReadOutcomes(wait).Single().Outputs["ai"]["action"].GetString());
        Assert.Equal("responder", runtime.ReadOutcomes(respond).Single().Outputs["ai"]["action"].GetString());
        Assert.Equal("inativa", runtime.ReadSession(quiet)!.CurrentStateId);
        Assert.Equal("inativa", runtime.ReadSession(wait)!.CurrentStateId);
        Assert.Equal("engajada", runtime.ReadSession(respond)!.CurrentStateId);
    }

    [Fact]
    public async Task InvalidTransitionsAndCarryFailClosedAndTraceHasOnlyStructuralData()
    {
        var key = SessionSliceAcceptanceSupport.Key("safe-failure");
        var invalidOutput = "{\"acao\":\"responder\",\"proximo_estado\":\"missing\",\"motivo\":\"invalid\"}";
        await using var runtime = Start(new FakeTimeProvider(), new RecordingCompletion([invalidOutput]), root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null, prompt: "prompt-secret"));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "private-id", SessionEntryKind.Group, "private-text");
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var outcome = Assert.Single(runtime.ReadOutcomes(key));
        Assert.False(outcome.Success);
        Assert.False(outcome.TransitionApplied);
        Assert.Null(outcome.TransitionTargetStateId);
        Assert.Equal("inativa", runtime.ReadSession(key)!.CurrentStateId);
        var trace = JsonSerializer.Serialize(runtime.ReadTrace(key));
        Assert.DoesNotContain("private-text", trace);
        Assert.DoesNotContain("prompt-secret", trace);
        Assert.Contains("ai", trace);

        var exception = Assert.Throws<SessionFlowProjectException>(() => runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("carry", root =>
            SessionSliceAcceptanceSupport.Node(SessionSliceAcceptanceSupport.Graph(SessionSliceAcceptanceSupport.State(root, "inativa")), "exit")["settings"]!["mode"] = "carry")));
        Assert.NotEmpty(exception.Issues);
        Assert.Equal("inativa", runtime.ReadSession(key)!.CurrentStateId);
    }

    [Fact]
    public async Task ReadHistoryPreservesOriginalEnvelopeProvenanceAfterCallerMessageIsDisposed()
    {
        var key = SessionSliceAcceptanceSupport.Key("history-provenance");
        var timestamp = DateTimeOffset.Parse("2026-09-07T12:34:56Z");
        await using var runtime = Start(new FakeTimeProvider(), new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa")]), root => SessionSliceAcceptanceSupport.Configure(root, null, 1, null));
        using (var source = JsonDocument.Parse("""{"texto":"original","remetente":{"nome":"Fixture","stableId":"fixture:sender"},"canal":{"nome":"group","nativeId":"history-provenance"},"reply":null}"""))
        {
            await runtime.ReceiveAsync(key, new SessionMessageEnvelope("provider-1", key.ChannelId, key.AccountId, key.NativeConversationId,
                timestamp, SessionMessageDirection.Inbound, SessionEntryKind.Group, source.RootElement), TestContext.Current.CancellationToken);
        }
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(runtime.ReadHistory(key));
        Assert.Equal(1, snapshot.AdmissionWatermark);
        Assert.Equal("provider-1", snapshot.ProviderMessageId);
        Assert.Equal(timestamp, snapshot.Timestamp);
        Assert.Equal(SessionMessageDirection.Inbound, snapshot.Direction);
        Assert.Equal(SessionEntryKind.Group, snapshot.EntryKind);
        Assert.Equal(key.ChannelId, snapshot.ChannelId);
        Assert.Equal(key.AccountId, snapshot.AccountId);
        Assert.Equal(key.NativeConversationId, snapshot.NativeConversationId);
        Assert.Equal("original", snapshot.Message.GetProperty("texto").GetString());
        Assert.Equal(1, runtime.ReadSession(key)!.HistoryCount);
    }

    [Fact]
    public async Task HistorySelectionUsesNewestMessagesAndCharacterBudget()
    {
        var countKey = SessionSliceAcceptanceSupport.Key("history-count");
        var countCompletion = new RecordingCompletion(Enumerable.Repeat(SessionSliceAcceptanceSupport.Response("inativa"), 4));
        await using (var countRuntime = Start(new FakeTimeProvider(), countCompletion, root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, null, 1, null);
            SessionSliceAcceptanceSupport.ConfigureHistory(root, 2, null, 1200, false);
        }))
        {
            foreach (var id in new[] { "old-1", "old-2", "old-3", "current" })
            {
                await SessionSliceAcceptanceSupport.ReceiveAsync(countRuntime, countKey, id, SessionEntryKind.Group, id);
                await countRuntime.WaitForIdleAsync(countKey, TestContext.Current.CancellationToken);
            }
            Assert.Equal(["old-2", "old-3"], SessionSliceAcceptanceSupport.Texts(countCompletion.Requests.Last().Inputs.GetProperty("historico")));
        }

        var characterKey = SessionSliceAcceptanceSupport.Key("history-characters");
        var characterCompletion = new RecordingCompletion(Enumerable.Repeat(SessionSliceAcceptanceSupport.Response("inativa"), 3));
        await using var characterRuntime = Start(new FakeTimeProvider(), characterCompletion, root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, null, 1, null);
            SessionSliceAcceptanceSupport.ConfigureHistory(root, 8, null, 4, false);
        });
        foreach (var (id, text) in new[] { ("old-a", "aaaa"), ("old-b", "bbbb"), ("current", "current") })
        {
            await SessionSliceAcceptanceSupport.ReceiveAsync(characterRuntime, characterKey, id, SessionEntryKind.Group, text);
            await characterRuntime.WaitForIdleAsync(characterKey, TestContext.Current.CancellationToken);
        }
        Assert.Equal(["bbbb"], SessionSliceAcceptanceSupport.Texts(characterCompletion.Requests.Last().Inputs.GetProperty("historico")));
    }

    [Fact]
    public async Task HistoryAgeAndCharacterDirectionFiltersApplyToFrozenBatchHistory()
    {
        var time = new FakeTimeProvider();
        var key = SessionSliceAcceptanceSupport.Key("history-age-direction");
        var completion = new RecordingCompletion(Enumerable.Repeat(SessionSliceAcceptanceSupport.Response("inativa"), 2));
        await using var runtime = Start(time, completion, root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, null, 1, null);
            SessionSliceAcceptanceSupport.ConfigureHistory(root, 8, 1, 1200, true);
        });

        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "expired", SessionEntryKind.Group, "expired", timestamp: time.GetUtcNow());
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(2));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "character", SessionEntryKind.Group, "character", SessionMessageDirection.Outbound, time.GetUtcNow());
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, key, "current", SessionEntryKind.Group, "current", timestamp: time.GetUtcNow());
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(["character"], SessionSliceAcceptanceSupport.Texts(completion.Requests.Last().Inputs.GetProperty("historico")));
        Assert.Equal(3, runtime.ReadSession(key)!.HistoryCount);
        Assert.Equal(["expired", "character", "current"], SessionSliceAcceptanceSupport.Texts(JsonSerializer.SerializeToElement(runtime.ReadHistory(key).Select(entry => entry.Message))));
    }

    private static SessionFlowRuntime Start(TimeProvider time, IFlowCompletion completion, Action<JsonObject> configure)
    {
        var runtime = new SessionFlowRuntime(time, completion);
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("r1", configure));
        return runtime;
    }

    private static async Task Idle(SessionFlowRuntime runtime, params SessionKey[] keys)
    {
        foreach (var key in keys) await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
    }

    private static string FixturePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "typed-flow-slice2.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Slice 2 fixture was not found from the test output directory.");
    }

    private static JsonElement Node(IEnumerable<JsonElement> nodes, string id) =>
        nodes.Single(node => node.GetProperty("id").GetString() == id);
}
