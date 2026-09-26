using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Outbound;
using Xunit;

namespace Filomena.Core.Tests.Outbound;

// Confirmed-send integration through the real session runtime with fake completion and fake transport.
// These are local guarantees; nothing here touches a Gateway, a model or Graphiti.
public sealed class LiveRuntimeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "live-runtime-" + Guid.NewGuid().ToString("N"));
    private static readonly SessionKey Session = new("runtime", "filomena", "whatsapp", "default", "5511944558218@s.whatsapp.net");
    private static readonly OutboundSpeaker Speaker = new("Filomena", "character:filomena");
    private static readonly LiveRouteBinding Binding = new(Session, "opaque-route", "whatsapp", "default", Session.NativeConversationId, Speaker, 1000);
    private static readonly OutboundBindings Roles = new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged");
    private static readonly TransportSafetyCapabilities Complete = new(true, true, true, true, true, true, true);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public LiveRuntimeTests() => Directory.CreateDirectory(directory);

    private ShadowOutboundHost Host(SessionFlowProject project, SequencedTransport transport, OutboundMode mode = OutboundMode.Enabled)
    {
        var host = new ShadowOutboundHost(Path.Combine(directory, "journal.jsonl"), project.Document, Roles, [Session], Complete);
        host.BindLiveRoute(Binding, transport);
        host.Rearm(mode);
        return host;
    }

    private static SessionFlowRuntime Runtime(SessionFlowProject project, IFlowCompletion completion, ShadowOutboundHost host)
    {
        var runtime = new SessionFlowRuntime(TimeProvider.System, completion, new ShadowRuntimeTests.Reader("empty"), outboundHost: host);
        try { runtime.UpdateProject(project); }
        catch (SessionFlowProjectException error) { Assert.Fail(JsonSerializer.Serialize(error.Issues)); }
        return runtime;
    }

    private static ValueTask<SessionAdmission> Inbound(SessionFlowRuntime runtime, string id, string text) =>
        runtime.ReceiveAsync(Session, new(id, Session.ChannelId, Session.AccountId, Session.NativeConversationId,
            DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
            JsonSerializer.SerializeToElement(new { texto = text, remetente = new { nome = "Helena", stableId = "whatsapp:5511944558218@s.whatsapp.net" }, canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null })), Token);

    private static string[] Texts(IEnumerable<SessionHistoryEntry> history) => history.Select(h => h.Message.GetProperty("texto").GetString()!).ToArray();

    [Fact]
    public async Task ConfirmedSendEntersHistoryOnceAndAppliesTransitionOnce()
    {
        var project = ShadowRuntimeTests.Project(false, sendExit: "sent");
        var transport = new SequencedTransport();
        using var host = Host(project, transport);
        await using var runtime = Runtime(project, new GatedCompletion(), host);
        await Inbound(runtime, "in-1", "Oi.");
        await runtime.WaitForIdleAsync(Session, Token);

        var outcome = Assert.Single(runtime.ReadOutcomes(Session));
        Assert.True(outcome.Success, outcome.Error);
        Assert.True(outcome.TransitionApplied);
        Assert.Equal("engaged", runtime.ReadSession(Session)!.CurrentStateId);
        Assert.Equal(("opaque-route", "Vamos por partes."), Assert.Single(transport.Sends));

        var history = runtime.ReadHistory(Session);
        Assert.Equal(2, history.Count);
        var speech = history[1];
        Assert.Equal(SessionMessageDirection.Outbound, speech.Direction);
        Assert.Equal("provider-1", speech.ProviderMessageId);
        Assert.Equal("Vamos por partes.", speech.Message.GetProperty("texto").GetString());
        Assert.Equal("Filomena", speech.Message.GetProperty("remetente").GetProperty("nome").GetString());
        Assert.Equal("character:filomena", speech.Message.GetProperty("remetente").GetProperty("stableId").GetString());
        Assert.Equal(Session.NativeConversationId, speech.Message.GetProperty("canal").GetProperty("nativeId").GetString());

        var trace = runtime.ReadTrace(Session);
        var send = Assert.Single(trace, e => e.EventName == "node-outbound");
        Assert.Equal("sent", send.MemoryMetadata!["result"]);
        Assert.DoesNotContain("provider-1", send.MemoryMetadata.Values);
        Assert.Single(trace, e => e.EventName == "speech-committed");
        Assert.Single(trace, e => e.EventName == "transition-applied");
        Assert.DoesNotContain(trace, e => e.EventName == "node-hypothetical-transition");
        Assert.False(host.RequiresReconciliation(Session));

        // The provider echo of our own speech is a duplicate, never a new human turn.
        var echo = await runtime.ReceiveAsync(Session, new("provider-1", Session.ChannelId, Session.AccountId, Session.NativeConversationId,
            DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm, history[1].Message), Token);
        Assert.Equal(SessionAdmissionStatus.Duplicate, echo.Status);
        await runtime.WaitForIdleAsync(Session, Token);
        Assert.Single(runtime.ReadOutcomes(Session));
        Assert.Single(transport.Sends);
    }

    [Theory]
    [InlineData(OutboundMode.Enabled, false)]
    [InlineData(OutboundMode.Shadow, true)]
    public async Task Pending_review_uses_confirmed_speech_not_execution_success(OutboundMode mode, bool expected)
    {
        var project = ShadowRuntimeTests.Project(false, sendExit: mode == OutboundMode.Enabled ? "sent" : "shadowed");
        var transport = new SequencedTransport();
        using var host = Host(project, transport, mode);
        await using var runtime = Runtime(project, new GatedCompletion(), host);
        await Inbound(runtime, "in-1", "Oi.");
        await runtime.WaitForIdleAsync(Session, Token);
        Assert.True(Assert.Single(runtime.ReadOutcomes(Session)).Success);
        var pending = new PendingParticipationReviews(runtime, Session,
            new(project.ProjectRevision, [], []), 150, 2, 10);
        Assert.Equal(expected, pending.Track("in-1"));
        if (expected) Assert.Empty(transport.Sends);
        else Assert.Single(transport.Sends); // Local fake transport, never a real send.
    }

    [Fact]
    public async Task Uncertain_send_is_not_a_new_response_opportunity()
    {
        var project = ShadowRuntimeTests.Project(false, sendExit: "indeterminate");
        var transport = new SequencedTransport { SendError = new IOException("Synthetic receipt lost") };
        using var host = Host(project, transport);
        await using var runtime = Runtime(project, new GatedCompletion(), host);
        await Inbound(runtime, "in-1", "Oi.");
        await runtime.WaitForIdleAsync(Session, Token);
        Assert.True(host.RequiresReconciliation(Session));
        Assert.DoesNotContain(runtime.ReadTrace(Session), t => t.EventName == "speech-committed");
        var pending = new PendingParticipationReviews(runtime, Session,
            new(project.ProjectRevision, [], []), 150, 2, 10);
        Assert.False(pending.Track("in-1"));
    }

    [Fact]
    public async Task Pending_original_closes_when_its_existing_execution_commits_speech()
    {
        var project = ShadowRuntimeTests.Project(false, sendExit: "sent");
        var transport = new SequencedTransport();
        var completion = new GatedCompletion { Gated = true };
        using var host = Host(project, transport);
        await using var runtime = Runtime(project, completion, host);
        await Inbound(runtime, "in-1", "Oi.");
        await completion.Started(1).WaitAsync(Token);
        var pending = new PendingParticipationReviews(runtime, Session,
            new(project.ProjectRevision, [], []), 150, 2, 10);
        Assert.True(pending.Track("in-1"));
        completion.Release(1);
        await runtime.WaitForIdleAsync(Session, Token);
        var result = Assert.Single(await pending.ReviewPendingAsync((_, _) => throw new Exception("Already answered"), Token));
        Assert.Equal("already_answered", result.Status);
        Assert.Null(result.Review);
        Assert.Single(transport.Sends);
    }

    [Theory]
    [InlineData("indeterminate")]
    [InlineData("failed")]
    public async Task UnconfirmedOutcomeCommitsNeitherSpeechNorTransitionEvenWhenWiredToExit(string scenario)
    {
        // Miswired graph: the unconfirmed exit leads to the transition node anyway. The runtime must not honor it.
        var project = ShadowRuntimeTests.Project(false, sendExit: scenario);
        var transport = new SequencedTransport
        {
            SendError = scenario == "indeterminate" ? new IOException("dropped after dispatch") : null,
            Description = scenario == "failed" ? new("whatsapp", "default", "someone-else") : null,
        };
        using var host = Host(project, transport);
        await using var runtime = Runtime(project, new GatedCompletion(), host);
        await Inbound(runtime, "in-1", "Oi.");
        await runtime.WaitForIdleAsync(Session, Token);

        var outcome = Assert.Single(runtime.ReadOutcomes(Session));
        Assert.True(outcome.Success, outcome.Error);
        Assert.False(outcome.TransitionApplied);
        Assert.Equal("initial", runtime.ReadSession(Session)!.CurrentStateId);
        Assert.Single(runtime.ReadHistory(Session));
        var trace = runtime.ReadTrace(Session);
        Assert.Equal(scenario, Assert.Single(trace, e => e.EventName == "node-outbound").MemoryMetadata!["result"]);
        Assert.Contains(trace, e => e.EventName == "node-hypothetical-transition" && e.MemoryMetadata!["applied"] == "false");
        Assert.DoesNotContain(trace, e => e.EventName is "speech-committed" or "transition-applied");
        Assert.Equal(scenario == "indeterminate", host.RequiresReconciliation(Session));
        Assert.Equal(scenario == "indeterminate" ? 1 : 0, transport.Sends.Count);
    }

    [Fact]
    public async Task ShadowWithBoundTransportStaysHypotheticalInRuntime()
    {
        var project = ShadowRuntimeTests.Project(false);
        var transport = new SequencedTransport();
        using var host = Host(project, transport, OutboundMode.Shadow);
        await using var runtime = Runtime(project, new GatedCompletion(), host);
        await Inbound(runtime, "in-1", "Oi.");
        await runtime.WaitForIdleAsync(Session, Token);
        var outcome = Assert.Single(runtime.ReadOutcomes(Session));
        Assert.True(outcome.Success, outcome.Error);
        Assert.False(outcome.TransitionApplied);
        Assert.Empty(transport.Sends);
        Assert.Equal(0, transport.Describes);
        Assert.Single(runtime.ReadHistory(Session));
        Assert.Equal("initial", runtime.ReadSession(Session)!.CurrentStateId);
        Assert.Contains(runtime.ReadTrace(Session), e => e.EventName == "node-hypothetical-transition");
    }

    [Fact]
    public async Task QueuedSuccessorSeesConfirmedPredecessorSpeechWithoutFutureInboundOrRepin()
    {
        var project = ShadowRuntimeTests.Project(false, sendExit: "sent", history: true);
        var transport = new SequencedTransport();
        var completion = new GatedCompletion { Gated = true };
        using var host = Host(project, transport);
        await using var runtime = Runtime(project, completion, host);

        await Inbound(runtime, "in-1", "m1");
        await completion.Started(1).WaitAsync(TimeSpan.FromSeconds(30), Token);
        // M2 closes its own batch while E1 is still generating; its history snapshot cannot contain E1's speech yet.
        await Inbound(runtime, "in-2", "m2");
        runtime.UpdateProject(ShadowRuntimeTests.Project(false, sendExit: "sent", history: true, version: 2));
        completion.Release(1);

        await completion.Started(2).WaitAsync(TimeSpan.FromSeconds(30), Token);
        // M3 arrives after E2 started: it must not leak into E2's context.
        await Inbound(runtime, "in-3", "m3");
        completion.Release(2);
        await completion.Started(3).WaitAsync(TimeSpan.FromSeconds(30), Token);
        completion.Release(3);
        await runtime.WaitForIdleAsync(Session, Token).WaitAsync(TimeSpan.FromSeconds(30), Token);

        var outcomes = runtime.ReadOutcomes(Session);
        Assert.Equal(3, outcomes.Count);
        Assert.Equal(new[] { "in-1", "in-2", "in-3" }, outcomes.Select(o => Assert.Single(o.ProviderMessageIds)));
        Assert.Equal(3, transport.Sends.Count);
        // Local admission order: M2 was admitted while E1 was still generating, so E1's speech lands after it.
        Assert.Equal(new[] { "m1", "m2", "Vamos por partes.", "m3", "Vamos por partes.", "Vamos por partes." }, Texts(runtime.ReadHistory(Session)));

        string[] Context(SessionFlowOutcome outcome) => outcome.Outputs["history"]["messages"].EnumerateArray().Select(m => m.GetProperty("texto").GetString()!).ToArray();
        Assert.Empty(Context(outcomes[0]));
        Assert.Equal(new[] { "m1", "Vamos por partes." }, Context(outcomes[1]));
        Assert.Equal(new[] { "m1", "m2", "Vamos por partes.", "Vamos por partes." }, Context(outcomes[2]));
        // B2 keeps its pinned project revision, but its state pin went stale when E1 transitioned:
        // it is reprocessed against the confirmed state before any external effect, never sent under the stale decision.
        Assert.Equal(1, outcomes[1].ProjectVersion);
        Assert.Equal(("engaged", 1L), (outcomes[1].StateId, outcomes[1].StateVersion));
        Assert.Equal(2, outcomes[2].ProjectVersion);
        Assert.All(outcomes, outcome => Assert.True(outcome.Success, outcome.Error));
        Assert.All(outcomes, outcome => Assert.True(outcome.TransitionApplied));
        var trace = runtime.ReadTrace(Session);
        // B2 closed under initial/0 and B3 under engaged/1; each predecessor transition made the next pin stale.
        var repinned = trace.Where(e => e.EventName == "batch-repinned").ToArray();
        Assert.Equal(new[] { outcomes[1].ExecutionId, outcomes[2].ExecutionId }, repinned.Select(e => e.ExecutionId));
        Assert.Equal(("initial", "0"), (repinned[0].MemoryMetadata!["fromStateId"], repinned[0].MemoryMetadata!["fromStateVersion"]));
        Assert.Equal(("engaged", "1"), (repinned[1].MemoryMetadata!["fromStateId"], repinned[1].MemoryMetadata!["fromStateVersion"]));
        Assert.All(repinned, e => Assert.Equal("engaged", e.TransitionTargetStateId));
        Assert.Equal(("engaged", 2L), (outcomes[2].StateId, outcomes[2].StateVersion));
        Assert.DoesNotContain(trace, e => e.EventName == "transition-skipped-stale");
        Assert.Equal(3, trace.Count(e => e.EventName == "speech-committed"));
        Assert.Equal(2, trace.Count(e => e.EventName == "history-frontier"));
        Assert.False(host.RequiresReconciliation(Session));
    }

    [Fact]
    public async Task StaleBatchWhoseConfirmedStateCannotTakeTheEntryIsBlockedWithDiagnosticAndPreserved()
    {
        var project = ShadowRuntimeTests.Project(false, sendExit: "sent", engagedWithoutDm: true);
        var transport = new SequencedTransport();
        var completion = new GatedCompletion { Gated = true };
        using var host = Host(project, transport);
        await using var runtime = Runtime(project, completion, host);

        await Inbound(runtime, "in-1", "m1");
        await completion.Started(1).WaitAsync(TimeSpan.FromSeconds(30), Token);
        await Inbound(runtime, "in-2", "m2");
        completion.Release(1);
        // A bounded wait: if the stale batch were executed, it would block on a second generation that is never released.
        await runtime.WaitForIdleAsync(Session, Token).WaitAsync(TimeSpan.FromSeconds(30), Token);

        var outcomes = runtime.ReadOutcomes(Session);
        Assert.Equal(2, outcomes.Count);
        Assert.True(outcomes[0].TransitionApplied);
        Assert.Equal("engaged", runtime.ReadSession(Session)!.CurrentStateId);
        var blocked = outcomes[1];
        Assert.False(blocked.Success);
        Assert.Equal("stale-state-entry-unavailable", blocked.Error);
        Assert.Equal("in-2", Assert.Single(blocked.ProviderMessageIds));
        Assert.Single(transport.Sends);
        // Nothing was generated or dispatched for the blocked batch; the inbound stays in history for reconciliation.
        Assert.Equal(new[] { "m1", "m2", "Vamos por partes." }, Texts(runtime.ReadHistory(Session)));
        var trace = runtime.ReadTrace(Session);
        Assert.Single(trace, e => e.EventName == "batch-blocked-stale" && e.ExecutionId == blocked.ExecutionId);
        Assert.DoesNotContain(trace, e => e.ExecutionId == blocked.ExecutionId && e.EventName.StartsWith("node-", StringComparison.Ordinal));
        Assert.Equal(0, runtime.ReadSession(Session)!.PendingExecutionCount);
    }

    public void Dispose() => Directory.Delete(directory, true);

    internal sealed class SequencedTransport : IBoundOutboundTransport
    {
        private int sequence;
        public TransportRouteDescription? Description { get; init; }
        public Exception? SendError { get; init; }
        public string ProviderPrefix { get; init; } = "provider-";
        public List<(string Route, string Text)> Sends { get; } = [];
        public List<BoundSendRequest> Requests { get; } = [];
        public int Describes { get; private set; }
        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken)
        {
            Describes++;
            return Task.FromResult(Description ?? new("whatsapp", "default", Session.NativeConversationId));
        }
        public Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Legacy send must not be reached by the live runtime.");

        public Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken cancellationToken)
        {
            lock (Sends) { Sends.Add((request.TransportRoute, request.Text)); Requests.Add(request); }
            if (SendError is not null) throw SendError;
            var id = ProviderPrefix + Interlocked.Increment(ref sequence);
            return Task.FromResult(new SendReceipt(id) { Channel = "whatsapp", AccountId = "default", Recipient = Session.NativeConversationId, GatewayRunId = "run-" + id });
        }
    }

    internal sealed class GatedCompletion : IFlowCompletion
    {
        private readonly object gate = new();
        private readonly Dictionary<int, TaskCompletionSource> started = [];
        private readonly Dictionary<int, TaskCompletionSource> released = [];
        private int generations;
        public bool Gated { get; init; }
        public Task Started(int generation) => Slot(started, generation).Task;
        public void Release(int generation) => Slot(released, generation).TrySetResult();
        private TaskCompletionSource Slot(Dictionary<int, TaskCompletionSource> slots, int generation)
        {
            lock (gate)
            {
                if (!slots.TryGetValue(generation, out var slot)) slots[generation] = slot = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return slot;
            }
        }
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (request.NodeId == "generate")
            {
                if (Gated)
                {
                    var generation = Interlocked.Increment(ref generations);
                    Slot(started, generation).TrySetResult();
                    await Slot(released, generation).Task.WaitAsync(token);
                }
                return "Vamos por partes.";
            }
            return JsonSerializer.Serialize(request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao"));
        }
    }
}
