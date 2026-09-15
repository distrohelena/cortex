using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Outbound;
using Xunit;

namespace Filomena.Core.Tests.Outbound;

// Local guarantees only. The transport is always a fake; nothing here proves live Gateway behavior.
public sealed class LiveOutboundHostTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "live-outbound-" + Guid.NewGuid().ToString("N"));
    private static readonly SessionKey Session = new("runtime", "filomena", "whatsapp", "account", "dm-exact");
    private static readonly SessionKey Other = Session with { NativeConversationId = "dm-other" };
    private static readonly JsonElement Project = JsonSerializer.SerializeToElement(new { version = 1 });
    private static readonly OutboundBindings Bindings = new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged");
    private static readonly TransportSafetyCapabilities Complete = new(true, true, true, true, true, true, true);
    private static readonly OutboundSpeaker Speaker = new("Filomena", "character:filomena");
    private static readonly LiveRouteBinding Binding = new(Session, "opaque-route", "whatsapp", "account", "dm-exact", Speaker, 1000);
    public LiveOutboundHostTests() => Directory.CreateDirectory(directory);
    private string JournalPath => Path.Combine(directory, "journal.jsonl");
    private ShadowOutboundHost Host(TransportSafetyCapabilities? capabilities = null, params SessionKey[] sessions) =>
        new(JournalPath, Project, Bindings, sessions.Length == 0 ? [Session] : sessions, capabilities ?? Complete);
    private static OutboundExecution Begin(ShadowOutboundHost host, string execution = "execution", string input = "input", SessionKey? session = null) =>
        host.Begin(Project, execution, session ?? Session, SessionEntryKind.Dm, [input]);
    private static TypedValue Approve(OutboundExecution execution, string text = "candidate")
    {
        var recall = new TypedValue(new ArrayType(FlowDomain.Memory), JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var boundaries = new TypedValue(new ArrayType(FlowDomain.Boundary), JsonSerializer.SerializeToElement(new object[] { new { trusted = true } }));
        execution.Recall("recall", recall);
        execution.Compiled("compile", recall, boundaries);
        var candidate = new TypedValue(new StringType(), JsonSerializer.SerializeToElement(text));
        execution.Generated("generate", new Dictionary<string, TypedValue> { ["text"] = candidate });
        execution.Enforced("enforce", candidate, boundaries, true, "revision");
        return candidate;
    }
    private static ShadowOutboundHost Armed(ShadowOutboundHost host, FakeTransport transport, LiveRouteBinding? binding = null)
    {
        host.BindLiveRoute(binding ?? Binding, transport);
        host.Rearm(OutboundMode.Enabled);
        return host;
    }
    private string[] JournalLines()
    {
        if (!File.Exists(JournalPath)) return [];
        using var stream = new FileStream(JournalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
    private string[] JournalStatuses() => JournalLines().Select(line => JsonDocument.Parse(line).RootElement.GetProperty("Status").GetString()!).ToArray();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConfirmedReceiptIsSentOnceAndStaysUnresolvedUntilLocalCommit()
    {
        var transport = new FakeTransport();
        using var host = Armed(Host(), transport);
        Assert.Equal(OutboundMode.Enabled, host.EffectiveMode);
        var execution = Begin(host);
        var candidate = Approve(execution);
        var result = await execution.SendAsync(candidate, Token);
        Assert.Equal("sent", result.Status);
        Assert.Equal("provider_accepted", result.DiagnosticCode);
        Assert.Equal("provider-1", result.ProviderMessageId);
        Assert.Equal(Speaker, result.Speaker);
        Assert.Equal("engaged", result.WouldTransitionTo);
        Assert.Equal(("opaque-route", "candidate"), Assert.Single(transport.Sends));
        Assert.Equal("duplicate_attempt", (await execution.SendAsync(candidate, Token)).DiagnosticCode);
        Assert.Single(transport.Sends);
        Assert.Equal(new[] { "pending", "sent" }, JournalStatuses());
        // Receipt persisted but speech not yet in local history: the session must not speak again.
        Assert.True(host.RequiresReconciliation(Session));
        execution.HistoryCommitted(transitionApplied: true);
        Assert.False(host.RequiresReconciliation(Session));
        Assert.Equal(new[] { "pending", "sent", "history_committed" }, JournalStatuses());
        // The real attempt keeps the authorized text and provider ID for idempotent recovery (ACL-protected journal);
        // it still never records the route.
        var text = string.Join("\n", JournalLines());
        Assert.DoesNotContain("dm-exact", text);
        Assert.DoesNotContain("opaque-route", text);
        Assert.Empty(host.PendingSpeechCommits(Session));
    }

    [Fact]
    public async Task SentWithoutLocalCommitIsRecoveredIdempotentlyWithoutResend()
    {
        var transport = new FakeTransport();
        using (var host = Armed(Host(), transport))
        {
            var execution = Begin(host);
            Assert.Equal("sent", (await execution.SendAsync(Approve(execution), Token)).Status);
        }
        using var restarted = Armed(Host(), transport);
        Assert.True(restarted.RequiresReconciliation(Session));
        var commit = Assert.Single(restarted.PendingSpeechCommits(Session));
        Assert.Equal(("provider-1", "candidate", Speaker), (commit.ProviderMessageId, commit.Text, commit.Speaker));
        Assert.Empty(restarted.PendingSpeechCommits(Other));
        restarted.CompleteSpeechCommit(Session, commit.AttemptRef, transitionApplied: false);
        Assert.False(restarted.RequiresReconciliation(Session));
        Assert.Empty(restarted.PendingSpeechCommits(Session));
        Assert.Equal(new[] { "pending", "sent", "history_committed" }, JournalStatuses());
        Assert.Throws<InvalidOperationException>(() => restarted.CompleteSpeechCommit(Session, commit.AttemptRef, false));
        var next = Begin(restarted, "next", "next-input");
        Assert.Equal("sent", (await next.SendAsync(Approve(next), Token)).Status);
        Assert.Equal(2, transport.Sends.Count);
    }

    [Theory]
    [InlineData("not_sent")]
    [InlineData("sent")]
    [InlineData("abandoned")]
    public async Task IndeterminateIsNeverRedispatchedAndOnlyExplicitReconciliationUnblocksTheSession(string verdict)
    {
        var transport = new FakeTransport { SendError = new IOException("dropped") };
        using (var host = Armed(Host(), transport))
        {
            var execution = Begin(host);
            Assert.Equal("indeterminate", (await execution.SendAsync(Approve(execution), Token)).Status);
        }
        var recovered = new FakeTransport();
        using var restarted = Armed(Host(), recovered);
        Assert.True(restarted.RequiresReconciliation(Session));
        Assert.Empty(restarted.PendingSpeechCommits(Session));
        var blocked = Begin(restarted, "blocked", "blocked-input");
        Assert.Equal("reconciliation_required", (await blocked.SendAsync(Approve(blocked), Token)).DiagnosticCode);
        Assert.Empty(recovered.Sends);

        var attempt = JsonDocument.Parse(JournalLines()[0]).RootElement.GetProperty("AttemptRef").GetString()!;
        var parsed = Enum.Parse<ReconciliationVerdict>(verdict.Replace("_", ""), ignoreCase: true);
        if (verdict == "sent")
        {
            Assert.Throws<ArgumentException>(() => restarted.Reconcile(Session, attempt, parsed, "evidence:gateway-log-42"));
            restarted.Reconcile(Session, attempt, parsed, "evidence:gateway-log-42", providerMessageId: "late-provider-7");
            var commit = Assert.Single(restarted.PendingSpeechCommits(Session));
            Assert.Equal(("late-provider-7", "candidate", Speaker), (commit.ProviderMessageId, commit.Text, commit.Speaker));
            Assert.True(restarted.RequiresReconciliation(Session));
            restarted.CompleteSpeechCommit(Session, attempt, transitionApplied: false);
        }
        else restarted.Reconcile(Session, attempt, parsed, "evidence:gateway-log-42");
        Assert.False(restarted.RequiresReconciliation(Session));
        Assert.Throws<InvalidOperationException>(() => restarted.Reconcile(Session, attempt, ReconciliationVerdict.NotSent, "evidence:again"));
        Assert.Contains(JournalLines(), line => line.Contains("reconciled:evidence:gateway-log-42", StringComparison.Ordinal));
        var next = Begin(restarted, "next", "next-input");
        Assert.Equal("sent", (await next.SendAsync(Approve(next), Token)).Status);
        Assert.Single(recovered.Sends);
    }

    [Fact]
    public void ReconciliationNeedsAnIndeterminateAttemptAndEvidence()
    {
        using var host = Host();
        Assert.Throws<InvalidOperationException>(() => host.Reconcile(Session, "unknown-attempt", ReconciliationVerdict.NotSent, "evidence:x"));
        Assert.Throws<ArgumentException>(() => host.Reconcile(Session, "unknown-attempt", ReconciliationVerdict.NotSent, " "));
        Assert.Throws<InvalidOperationException>(() => host.CompleteSpeechCommit(Session, "unknown-attempt", false));
    }

    [Fact]
    public async Task MissingRouteBindingBlocksBeforeAnyTransportOrJournalEffect()
    {
        using var host = Host();
        host.Rearm(OutboundMode.Enabled);
        Assert.Equal(OutboundMode.Disabled, host.EffectiveMode);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), Token);
        Assert.Equal(("blocked", "route_not_bound"), (result.Status, result.DiagnosticCode));
        Assert.Empty(JournalLines());
    }

    [Fact]
    public async Task WrongRouteResolutionFailsClosedBeforeDispatch()
    {
        var transport = new FakeTransport { Description = new("whatsapp", "account", "somebody-else") };
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), Token);
        Assert.Equal(("failed", "route_mismatch"), (result.Status, result.DiagnosticCode));
        Assert.Empty(transport.Sends);
        Assert.Equal(1, transport.Describes);
        Assert.Equal(new[] { "pending", "not_sent" }, JournalStatuses());
        Assert.False(host.RequiresReconciliation(Session));
    }

    [Fact]
    public async Task RouteDescriptionOutageFailsClosedBeforeDispatch()
    {
        var transport = new FakeTransport { DescribeError = new IOException("gateway offline") };
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), Token);
        Assert.Equal(("failed", "route_unresolved"), (result.Status, result.DiagnosticCode));
        Assert.Empty(transport.Sends);
        Assert.Equal(new[] { "pending", "not_sent" }, JournalStatuses());
    }

    [Fact]
    public async Task BindingCoversExactlyOneSessionEvenWhenOthersAreAllowlisted()
    {
        var transport = new FakeTransport();
        using var host = Armed(Host(null, Session, Other), transport);
        var other = Begin(host, "other-execution", "other-input", Other);
        var result = await other.SendAsync(Approve(other), Token);
        Assert.Equal(("blocked", "route_not_bound"), (result.Status, result.DiagnosticCode));
        Assert.Empty(transport.Sends);
        Assert.Throws<InvalidOperationException>(() => host.BindLiveRoute(Binding with { Session = Session with { NativeConversationId = "not-allowlisted" } }, transport));
    }

    [Fact]
    public async Task AmbiguousTransportFailureIsIndeterminateNeverRetriedAndLatchesHost()
    {
        var transport = new FakeTransport { SendError = new IOException("connection dropped after dispatch") };
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), Token);
        Assert.Equal(("indeterminate", "transport_failure"), (result.Status, result.DiagnosticCode));
        Assert.Single(transport.Sends);
        Assert.Equal(new[] { "pending", "indeterminate" }, JournalStatuses());
        Assert.Equal(OutboundMode.Disabled, host.Mode);
        Assert.True(host.RequiresReconciliation(Session));
        host.Rearm(OutboundMode.Enabled);
        var next = Begin(host, "next", "next-input");
        Assert.Equal("reconciliation_required", (await next.SendAsync(Approve(next), Token)).DiagnosticCode);
        Assert.Single(transport.Sends);
    }

    [Fact]
    public async Task CancellationDuringDispatchIsIndeterminate()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new FakeTransport { SendBehavior = async token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return new("never"); } };
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), cancellation.Token);
        Assert.Equal(("indeterminate", "cancelled_after_dispatch"), (result.Status, result.DiagnosticCode));
        Assert.Equal(new[] { "pending", "indeterminate" }, JournalStatuses());
        Assert.True(host.RequiresReconciliation(Session));
    }

    [Fact]
    public async Task CancellationBeforeDispatchIsProvablyNotSent()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new FakeTransport { DescribeBehavior = _ => { cancellation.Cancel(); return Task.FromResult(new TransportRouteDescription("whatsapp", "account", "dm-exact")); } };
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), cancellation.Token);
        Assert.Equal(("failed", "cancelled_before_dispatch"), (result.Status, result.DiagnosticCode));
        Assert.Empty(transport.Sends);
        Assert.Equal(new[] { "pending", "not_sent" }, JournalStatuses());
        Assert.False(host.RequiresReconciliation(Session));
    }

    [Fact]
    public async Task KillSwitchAfterReservationResolvesNotSentWithoutDispatch()
    {
        ShadowOutboundHost? host = null;
        var transport = new FakeTransport { DescribeBehavior = _ => { host!.Rearm(OutboundMode.Disabled); return Task.FromResult(new TransportRouteDescription("whatsapp", "account", "dm-exact")); } };
        host = Armed(Host(), transport);
        using (host)
        {
            var execution = Begin(host);
            var result = await execution.SendAsync(Approve(execution), Token);
            Assert.Equal(("disabled", "disarmed_before_dispatch"), (result.Status, result.DiagnosticCode));
            Assert.Empty(transport.Sends);
            Assert.Equal(new[] { "pending", "not_sent" }, JournalStatuses());
        }
    }

    [Fact]
    public async Task CrashAfterReservationBecomesIndeterminateOnRestartWithoutRetry()
    {
        var hang = new TaskCompletionSource<SendReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeTransport { SendBehavior = _ => hang.Task };
        var crashed = Armed(Host(), transport);
        var execution = Begin(crashed);
        var inflight = execution.SendAsync(Approve(execution), Token);
        await transport.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.Equal(new[] { "pending" }, JournalStatuses());
        crashed.Dispose(); // process death while the provider call is outstanding

        using var restarted = Armed(Host(), transport);
        Assert.Equal(new[] { "pending", "indeterminate" }, JournalStatuses());
        Assert.True(restarted.RequiresReconciliation(Session));
        var replay = Begin(restarted, "replay", "input");
        Assert.Equal("reconciliation_required", (await replay.SendAsync(Approve(replay), Token)).DiagnosticCode);
        var fresh = Begin(restarted, "fresh", "fresh-input");
        Assert.Equal("reconciliation_required", (await fresh.SendAsync(Approve(fresh), Token)).DiagnosticCode);
        Assert.Single(transport.Sends);

        hang.SetResult(new SendReceipt("late-provider-id") { Channel = "whatsapp", Recipient = "dm-exact" });
        var late = await inflight;
        Assert.Equal("indeterminate", late.Status);
        Assert.Null(late.ProviderMessageId);
    }

    [Fact]
    public async Task ReceiptPersistedBeforeLocalCommitCrashRequiresReconciliationAfterRestart()
    {
        var transport = new FakeTransport();
        using (var host = Armed(Host(), transport))
        {
            var execution = Begin(host);
            Assert.Equal("sent", (await execution.SendAsync(Approve(execution), Token)).Status);
        }
        using var restarted = Armed(Host(), transport);
        Assert.True(restarted.RequiresReconciliation(Session));
        var next = Begin(restarted, "next", "next-input");
        Assert.Equal("reconciliation_required", (await next.SendAsync(Approve(next), Token)).DiagnosticCode);
        Assert.Single(transport.Sends);
        Assert.Equal(new[] { "pending", "sent" }, JournalStatuses());
    }

    [Fact]
    public async Task HistoricalShadowRecordsAreNeverPromotedToRealSends()
    {
        var transport = new FakeTransport();
        using var host = Host();
        host.Rearm(OutboundMode.Shadow);
        var shadow = Begin(host, "shadow-execution", "input");
        Assert.Equal("shadowed", (await shadow.SendAsync(Approve(shadow), Token)).Status);
        Armed(host, transport);
        var replay = Begin(host, "real-execution-same-input", "input");
        Assert.Equal("replayed_input", (await replay.SendAsync(Approve(replay), Token)).DiagnosticCode);
        var sameExecution = host.Begin(Project, "shadow-execution", Session, SessionEntryKind.Dm, ["different-input"]);
        Assert.Equal("execution_replay", (await sameExecution.SendAsync(Approve(sameExecution), Token)).DiagnosticCode);
        Assert.Empty(transport.Sends);
        Assert.Equal(new[] { "shadowed" }, JournalStatuses());
    }

    [Fact]
    public async Task ShadowModeWithBoundTransportNeverDispatches()
    {
        var transport = new FakeTransport();
        using var host = Host();
        host.BindLiveRoute(Binding, transport);
        host.Rearm(OutboundMode.Shadow);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), Token);
        Assert.Equal(("shadowed", "would_send"), (result.Status, result.DiagnosticCode));
        Assert.Null(result.ProviderMessageId);
        Assert.Empty(transport.Sends);
        Assert.Equal(0, transport.Describes);
        Assert.Equal(new[] { "shadowed" }, JournalStatuses());
    }

    [Theory]
    [InlineData("recipient")]
    [InlineData("channel")]
    [InlineData("account")]
    public async Task ReceiptRouteMismatchIsIndeterminateAndLatches(string field)
    {
        var transport = new FakeTransport
        {
            Receipt = new SendReceipt("provider-1")
            {
                Channel = field == "channel" ? "telegram" : "whatsapp",
                AccountId = field == "account" ? "other-account" : "account",
                Recipient = field == "recipient" ? "somebody-else" : "dm-exact",
            }
        };
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), Token);
        Assert.Equal(("indeterminate", "receipt_route_mismatch"), (result.Status, result.DiagnosticCode));
        Assert.Null(result.ProviderMessageId);
        Assert.Equal(OutboundMode.Disabled, host.Mode);
        Assert.True(host.RequiresReconciliation(Session));
    }

    [Fact]
    public async Task ReceiptWithoutRouteEvidenceIsIndeterminate()
    {
        var transport = new FakeTransport { Receipt = new SendReceipt("provider-1") };
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), Token);
        Assert.Equal(("indeterminate", "receipt_route_unverified"), (result.Status, result.DiagnosticCode));
        Assert.True(host.RequiresReconciliation(Session));
    }

    [Fact]
    public async Task ReceiptWithoutAccountIsAcceptedOnlyWhenChannelAndRecipientMatch()
    {
        // The native gateway payload carries no accountId; the account guarantee is the pre-send binding.
        var transport = new FakeTransport { Receipt = new SendReceipt("provider-1") { Channel = "whatsapp", Recipient = "dm-exact" } };
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution), Token);
        Assert.Equal("sent", result.Status);
        Assert.Equal("unattested", execution.Trace(result)["receiptAccount"]);
    }

    [Fact]
    public async Task PayloadOverSinglePartLimitIsBlockedBeforeReservation()
    {
        var transport = new FakeTransport();
        using var host = Armed(Host(), transport, Binding with { MaxSinglePartCharacters = 10 });
        var execution = Begin(host);
        var result = await execution.SendAsync(Approve(execution, new string('x', 11)), Token);
        Assert.Equal(("blocked", "payload_exceeds_single_part"), (result.Status, result.DiagnosticCode));
        Assert.Empty(transport.Sends);
        Assert.Empty(JournalLines());
        Assert.Throws<ArgumentOutOfRangeException>(() => host.BindLiveRoute(Binding with { MaxSinglePartCharacters = 4001 }, transport));
    }

    [Fact]
    public async Task IncompleteCapabilitiesBlockEnabledEvenWhenBound()
    {
        var transport = new FakeTransport();
        using var host = Armed(Host(TransportSafetyCapabilities.Unproven), transport);
        Assert.Equal(OutboundMode.Disabled, host.EffectiveMode);
        var execution = Begin(host);
        Assert.Equal("transport_capabilities_incomplete", (await execution.SendAsync(Approve(execution), Token)).DiagnosticCode);
        Assert.Empty(transport.Sends);
        Assert.Empty(JournalLines());
    }

    [Fact]
    public async Task ForgedProvenanceIsBlockedBeforeReservationInEnabledMode()
    {
        var transport = new FakeTransport();
        using var host = Armed(Host(), transport);
        var execution = Begin(host);
        Approve(execution);
        var forged = new TypedValue(new StringType(), JsonSerializer.SerializeToElement("candidate"));
        Assert.Equal("invalid_provenance", (await execution.SendAsync(forged, Token)).DiagnosticCode);
        Assert.Empty(transport.Sends);
        Assert.Empty(JournalLines());
    }

    public void Dispose() => Directory.Delete(directory, true);

    internal sealed class FakeTransport : IOutboundTransport
    {
        public TransportRouteDescription Description { get; init; } = new("whatsapp", "account", "dm-exact");
        public Exception? DescribeError { get; init; }
        public Func<CancellationToken, Task<TransportRouteDescription>>? DescribeBehavior { get; init; }
        public SendReceipt Receipt { get; init; } = new("provider-1") { Channel = "whatsapp", AccountId = "account", Recipient = "dm-exact", GatewayRunId = "run-1" };
        public Exception? SendError { get; init; }
        public Func<CancellationToken, Task<SendReceipt>>? SendBehavior { get; init; }
        public List<(string Route, string Text)> Sends { get; } = [];
        public int Describes { get; private set; }
        public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken)
        {
            Describes++;
            if (DescribeError is not null) throw DescribeError;
            return DescribeBehavior is not null ? DescribeBehavior(cancellationToken) : Task.FromResult(Description);
        }

        public Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken)
        {
            Sends.Add((sessionKey, text));
            SendStarted.TrySetResult();
            if (SendError is not null) throw SendError;
            return SendBehavior is not null ? SendBehavior(cancellationToken) : Task.FromResult(Receipt);
        }
    }
}
