using System.Text.Json;
using Cortex.Core.Conversations;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class OutboundDriverRecoveryTests
{
    private static readonly SessionKey Session = new("runtime", "agent", "whatsapp", "account", "dm-exact");
    private static readonly ArtifactScope Scope = new("installation", Session, "execution", "revision");
    private static readonly OutboundBindings Roles = new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, [], null);
    private static SessionFlowProject Project() => DurableDriverTests.Project(ExecutionSnapshotCodecTests.Json("""
        {"version":4,"nodes":[{"id":"in","kind":"dm"},
        {"id":"generate","kind":"prompt","prompt":"Write","outputMode":"text","inputs":[],"responseFields":[{"id":"text","name":"text","type":"text"}]},
        {"id":"send","kind":"send"}],"edges":[{"source":"generate","sourcePort":"text","target":"send","targetPort":"message"}],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"generate","targetPort":"exec"},{"source":"generate","sourcePort":"then","target":"send","targetPort":"exec"}]}
        """));
    private static ShadowOutboundHost Host(string path, SessionFlowProject project) => new(path, project.Document, Roles, [Session], new(true, true, true, true, true, true, true));
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RecoverReadsOriginalJournalWithoutCompletionOrRedispatch(bool unknown)
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = Project(); var journal = Path.Combine(directory.Path, "outbound.jsonl"); var transport = new Transport(unknown);
        var projectRef = await store.PutOnceAsync(Scope, "project", JsonSerializer.SerializeToElement(project), TestContext.Current.CancellationToken);
        var message = JsonSerializer.SerializeToElement(new { texto = "input", remetente = (object?)null, canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null });
        var batchRef = await store.PutOnceAsync(Scope, "batch", JsonSerializer.SerializeToElement(new ExecutionBatchV1("in", message, new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(), ["input"], [], false)), TestContext.Current.CancellationToken);
        var start = new ExecutionStart(Scope, "batch", projectRef, batchRef, "initial", 0, SessionEntryKind.Dm);
        StepCommand command;
        using (var host = Host(journal, project))
        {
            host.BindLiveRoute(new(Session, "route", "whatsapp", "account", "dm-exact", new("Filomena", "self"), 4000), transport); host.Rearm(OutboundMode.Enabled);
            var outbound = host.Begin(project.Document, Scope.ExecutionId, Session, SessionEntryKind.Dm, ["input"]);
            var recall = new TypedValue(new ArrayType(FlowDomain.Memory), JsonSerializer.SerializeToElement(Array.Empty<object>()));
            var compiled = new TypedValue(new ArrayType(FlowDomain.Boundary), JsonSerializer.SerializeToElement(Array.Empty<object>()));
            var candidate = new TypedValue(new StringType(), JsonSerializer.SerializeToElement("candidate"));
            outbound.Recall("recall", recall); outbound.Compiled("compile", recall, compiled); outbound.Generated("generate", new Dictionary<string, TypedValue> { ["text"] = candidate });
            var graph = project.Document.GetProperty("states")[0].GetProperty("graph");
            var snapshot = FlowExecutionKernel.Start(TypedFlowCompiler.Compile(graph, "in"), message, start, new Dictionary<string, IReadOnlyDictionary<string, TypedValue>>()).Snapshot;
            snapshot.Cursor = "send"; snapshot.Ordinal = 4;
            snapshot.Values[new("recall", "memories")] = recall; snapshot.Values[new("compile", "boundaries")] = compiled; snapshot.Values[new("generate", "text")] = candidate;
            snapshot.Outputs["generate"] = new() { ["text"] = candidate };
            snapshot.TrustedEvidence = JsonSerializer.SerializeToElement(new { Boundaries = (object?)null, Outbound = outbound.CaptureEvidence(snapshot.Values) });
            var snapshotRef = await store.PutOnceAsync(Scope, "before-send", ExecutionSnapshotCodec.Encode(snapshot), TestContext.Current.CancellationToken);
            command = new(new(Scope, "send", 4), snapshotRef);
            await store.PutOnceAsync(Scope, "intent-" + DurableIdentity.OperationId(command.Key), JsonSerializer.SerializeToElement(command), TestContext.Current.CancellationToken);
            Assert.Equal(unknown ? "indeterminate" : "sent", (await outbound.SendAsync(candidate, TestContext.Current.CancellationToken)).Status);
        }
        using var restarted = Host(journal, project);
        var driver = new FlowExecutionDriver(store, new Bindings(project, restarted));
        var recovered = await driver.RecoverAsync(command.Key, TestContext.Current.CancellationToken);
        Assert.NotNull(recovered);
        Assert.Equal(unknown ? ExecutionStatus.ReconciliationRequired : ExecutionStatus.Completed, recovered.Status);
        var checkpoint = ExecutionSnapshotCodec.Decode(await store.ReadAsync(recovered.SnapshotRef, TestContext.Current.CancellationToken), Scope);
        Assert.Equal(unknown ? null : "provider-1", checkpoint.ConfirmedSend?.ProviderMessageId);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(recovered, await driver.AdvanceAsync(command, TestContext.Current.CancellationToken));
        if (!unknown)
        {
            var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), new Bindings(project, restarted));
            var publication = await artifacts.ReadPublicationAsync(projectRef, TestContext.Current.CancellationToken);
            var now = DateTimeOffset.UtcNow;
            await store.PutOnceAsync(Scope, "prepared", JsonSerializer.SerializeToElement(new PreparedSessionExecution(start, now, null)), TestContext.Current.CancellationToken);
            var batch = new DurableSessionBatch("batch", Scope.ExecutionId, publication, "initial", 0, publication.Entries[0].Entry, [], 0, 0, now, SessionBatchCloseReason.Immediate);
            var commit = new OutcomeCommitCommand(batch, recovered, now, now, "initial", 0, 0);
            var receipt = await artifacts.CommitOutcomeAsync(commit, TestContext.Current.CancellationToken);
            Assert.Equal(receipt, await artifacts.CommitOutcomeAsync(commit, TestContext.Current.CancellationToken));
            Assert.Single((await artifacts.ReadHistoryAsync(Scope.InstallationId, Session, new(), TestContext.Current.CancellationToken)).Items);
            Assert.Empty(restarted.PendingSpeechCommits(Session));
        }
    }
    private sealed class Bindings(SessionFlowProject project, ShadowOutboundHost host) : IExecutionHostBindings, ISessionFlowCompletionFactory
    {
        public ValueTask<ExecutionHostBinding> ResolveAsync(ArtifactScope scope, CancellationToken token) => ValueTask.FromResult(new ExecutionHostBinding(scope, project.Document, this, new UnavailableMemoryReader(), host, TimeProvider.System));
        public IFlowCompletion Create(SessionCompletionContext context) => throw new InvalidOperationException("Recovery must not bind an inference call.");
    }
    private sealed class Transport(bool unknown) : IBoundOutboundTransport
    {
        internal int Calls { get; private set; }
        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken) => Task.FromResult(new TransportRouteDescription("whatsapp", "account", "dm-exact"));
        public Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken cancellationToken)
        { Calls++; if (unknown) throw new IOException("unknown"); return Task.FromResult(new SendReceipt("provider-1") { Channel = "whatsapp", AccountId = "account", Recipient = "dm-exact" }); }
        public Task<SendReceipt> SendTextAsync(string conversationId, string text, CancellationToken cancellationToken) => throw new InvalidOperationException("Only bound dispatch.");
    }
}
