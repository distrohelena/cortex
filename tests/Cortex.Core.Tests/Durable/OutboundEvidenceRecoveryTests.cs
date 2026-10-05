using System.Text.Json;
using Cortex.Core.Conversations;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class OutboundEvidenceRecoveryTests
{
    private static readonly SessionKey Session = new("runtime", "agent", "whatsapp", "account", "dm-exact");
    private static readonly JsonElement Project = JsonSerializer.SerializeToElement(new { version = 1 });
    private static readonly OutboundBindings Roles = new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, [], null);
    private static readonly ArtifactScope Scope = new("installation", Session, "execution", "revision");
    private static ShadowOutboundHost Host(string path) => new(path, Project, Roles, [Session], new(true, true, true, true, true, true, true));
    private static ExecutionHostBinding Binding(ShadowOutboundHost host) => new(Scope, Project, new NoCompletion(), new UnavailableMemoryReader(), host, TimeProvider.System);
    private static Dictionary<PortRef, TypedValue> Approve(OutboundExecution execution)
    {
        var recall = new TypedValue(new ArrayType(FlowDomain.Memory), JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var compiled = new TypedValue(new ArrayType(FlowDomain.Boundary), JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var text = new TypedValue(new StringType(), JsonSerializer.SerializeToElement("candidate"));
        execution.Recall("recall", recall); execution.Compiled("compile", recall, compiled);
        execution.Generated("generate", new Dictionary<string, TypedValue> { ["text"] = text });
        return new() { [new("recall", "memories")] = recall, [new("compile", "boundaries")] = compiled, [new("generate", "text")] = text };
    }
    private static Dictionary<PortRef, TypedValue> CopyValues(Dictionary<PortRef, TypedValue> original) => original.ToDictionary(x => x.Key, x => x.Value with { Value = x.Value.Value.Clone() });

    [Fact]
    public async Task RevokedHostDoesNotRegainSendingAuthority()
    {
        using var directory = new ArtifactDirectory();
        Directory.CreateDirectory(directory.Path);
        var path = Path.Combine(directory.Path, "outbound.jsonl");
        OutboundExecutionEvidence evidence;
        Dictionary<PortRef, TypedValue> values;
        using (var host = Host(path))
        {
            var execution = host.Begin(Project, "execution", Session, SessionEntryKind.Dm, ["input"]);
            values = Approve(execution); evidence = execution.CaptureEvidence(values);
        }
        using var restarted = Host(path);
        var restoredValues = CopyValues(values);
        var restored = restarted.RestoreExecution(Binding(restarted), evidence, restoredValues);
        Assert.Equal("disabled", (await restored.SendAsync(restoredValues[new("generate", "text")], TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task RestoredProvenanceCanShadowSendWithCurrentAuthority()
    {
        using var directory = new ArtifactDirectory(); Directory.CreateDirectory(directory.Path);
        var path = Path.Combine(directory.Path, "outbound.jsonl");
        OutboundExecutionEvidence evidence; Dictionary<PortRef, TypedValue> values;
        using (var host = Host(path))
        {
            var execution = host.Begin(Project, "execution", Session, SessionEntryKind.Dm, ["input"]);
            values = Approve(execution); evidence = execution.CaptureEvidence(values);
        }
        using var restarted = Host(path); restarted.Rearm(OutboundMode.Shadow);
        var copied = CopyValues(values);
        var restored = restarted.RestoreExecution(Binding(restarted), evidence, copied);
        Assert.Equal("shadowed", (await restored.SendAsync(copied[new("generate", "text")], TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task ReceiptWithoutNodeCheckpointRecoversSameProviderId()
    {
        using var directory = new ArtifactDirectory(); Directory.CreateDirectory(directory.Path);
        var path = Path.Combine(directory.Path, "outbound.jsonl");
        var transport = new Transport();
        OutboundExecutionEvidence evidence;
        Dictionary<PortRef, TypedValue> values;
        using (var host = Host(path))
        {
            host.BindLiveRoute(new(Session, "route", "whatsapp", "account", "dm-exact", new("Filomena", "self"), 4000), transport);
            host.Rearm(OutboundMode.Enabled);
            var execution = host.Begin(Project, "execution", Session, SessionEntryKind.Dm, ["input"]);
            values = Approve(execution); evidence = execution.CaptureEvidence(values); // pre-dispatch checkpoint
            Assert.Equal("sent", (await execution.SendAsync(values[new("generate", "text")], TestContext.Current.CancellationToken)).Status);
        }
        using var restarted = Host(path);
        var restored = restarted.RestoreExecution(Binding(restarted), evidence, CopyValues(values));
        var recovered = restarted.RecoverExecutionSend(restored);
        Assert.Equal("provider-1", recovered?.ProviderMessageId);
        Assert.Equal("provider-1", Assert.Single(restarted.PendingSpeechCommits(Session)).ProviderMessageId);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task IndeterminateAttemptNeverCallsTransportAgain()
    {
        using var directory = new ArtifactDirectory(); Directory.CreateDirectory(directory.Path);
        var path = Path.Combine(directory.Path, "outbound.jsonl");
        var transport = new Transport { FailAfterDispatch = true };
        OutboundExecutionEvidence evidence; Dictionary<PortRef, TypedValue> values;
        using (var host = Host(path))
        {
            host.BindLiveRoute(new(Session, "route", "whatsapp", "account", "dm-exact", new("Filomena", "self"), 4000), transport);
            host.Rearm(OutboundMode.Enabled);
            var execution = host.Begin(Project, "execution", Session, SessionEntryKind.Dm, ["input"]);
            values = Approve(execution); evidence = execution.CaptureEvidence(values);
            Assert.Equal("indeterminate", (await execution.SendAsync(values[new("generate", "text")], TestContext.Current.CancellationToken)).Status);
        }
        using var restarted = Host(path);
        restarted.BindLiveRoute(new(Session, "route", "whatsapp", "account", "dm-exact", new("Filomena", "self"), 4000), transport);
        restarted.Rearm(OutboundMode.Enabled);
        var copied = CopyValues(values);
        var restored = restarted.RestoreExecution(Binding(restarted), evidence, copied);
        Assert.Equal("indeterminate", restarted.RecoverExecutionSend(restored)?.Status);
        Assert.NotEqual("sent", (await restored.SendAsync(copied[new("generate", "text")], TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, transport.Calls);
    }
    private sealed class NoCompletion : ISessionFlowCompletionFactory
    { public IFlowCompletion Create(SessionCompletionContext context) => throw new InvalidOperationException("No inference in evidence tests."); }
    private sealed class Transport : IBoundOutboundTransport
    {
        internal int Calls { get; private set; }
        internal bool FailAfterDispatch { get; init; }
        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken) => Task.FromResult(new TransportRouteDescription("whatsapp", "account", "dm-exact"));
        public Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken cancellationToken)
        { Calls++; if (FailAfterDispatch) throw new IOException("unknown outcome"); return Task.FromResult(new SendReceipt("provider-1") { Channel = "whatsapp", AccountId = "account", Recipient = "dm-exact" }); }
        public Task<SendReceipt> SendTextAsync(string conversationId, string text, CancellationToken cancellationToken) => throw new InvalidOperationException("Only bound dispatch allowed.");
    }
}
