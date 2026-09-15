using System.Net;
using System.Text.Json;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Flows;
using Filomena.Core.Shadow;
using Filomena.OpenClaw.Observation;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class NativeObservationReaderTests
{
    [Fact]
    public async Task Trusted_native_lid_binding_keeps_logical_pn_memory_identity()
    {
        var transport = Session with { NativeConversationId = "12345@lid" };
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["routeBindingRef"] = NativeObservationReader.RouteBindingRef(transport);
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? status : Batch(false, [Event()])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer, transport);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var batch = await reader.PollAsync(approval, new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken);
        var inbound = reader.ToInbound(Assert.Single(batch.Records));
        Assert.Equal("100@s.whatsapp.net", inbound.Session.NativeConversationId);
        Assert.Equal("whatsapp-test", inbound.ConversationNamespace);
        Assert.Equal(Peer, inbound.ActorId);
        Assert.Equal("N1", inbound.Message.ProviderMessageId);
    }

    [Fact]
    public void Transport_alias_cannot_change_account()
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        Assert.Throws<ArgumentException>(() => new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding,
            "source", Self, Peer, Session with { AccountId = "foreign" }));
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData("armed", false)]
    public async Task Provisioning_checks_reader_without_requiring_capture(string captureState, bool beforeArm)
    {
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["bootstrap"] = JsonSerializer.SerializeToNode(new { subsystemOperational = true, readerAvailable = true,
            journalHealth = "healthy", captureState, activeEpoch = captureState == "armed" ? "epoch" : null,
            controlAvailable = true, configSnapshotAvailable = true, dispatchInspectorAvailable = true,
            effectiveConfigRevision = "R1" });
        using var handler = new Handler(_ => status);
        using var client = new HttpClient(handler);
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var checks = await reader.ProvisioningChecksAsync(beforeArm, TestContext.Current.CancellationToken);
        Assert.All(checks, check => Assert.True(check.Passed, check.Id));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task Provisioning_does_not_infer_bootstrap_from_reader_capabilities()
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        await Assert.ThrowsAsync<NativeObservationException>(() => reader.ProvisioningChecksAsync(true, TestContext.Current.CancellationToken));
    }
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "test", "100@s.whatsapp.net");
    private const string Self = "whatsapp:200@s.whatsapp.net", Peer = "whatsapp:100@s.whatsapp.net";
    private static readonly ShadowSessionBinding Binding = new(Session, "whatsapp-test", [Self], [Peer]);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static object Cursor(long sequence) => new { version = "inbound-observation:v1", epoch = "epoch", sessionRef = "source", sequence };
    private static object Status(string health = "healthy") => new
    {
        version = "inbound-observation:v1",
        health,
        sessionRef = "source",
        channel = "whatsapp",
        kind = "dm",
        routeBindingRef = NativeObservationReader.RouteBindingRef(Session),
        journalEpoch = "epoch",
        selfIdentityRef = "self-ref",
        peerActorRef = "peer-ref",
        capabilities = NativeObservationPreflight.RequiredCapabilities.ToDictionary(k => k, _ => true),
        dispatch = new { version = "dispatch-inspection:v1", status = "inactive", effectiveConfigRevision = "R1", capturedAt = 1000, diagnostic = "excluded", paths = Array.Empty<object>() }
    };
    private static object Event(string session = "source", bool self = false) => new
    {
        sequence = 1,
        nativeMessageId = "N1",
        sessionRef = session,
        actorRef = self ? "self-ref" : "peer-ref",
        fromMe = self,
        selfIdentityRef = "self-ref",
        occurredAt = 1000,
        journalEpoch = "epoch",
        payload = new { receivedAt = 1100, message = new { message = new { conversation = "hello" } } }
    };
    private static object Batch(bool open, object[] events) => open
        ? new { version = "inbound-observation:v1", sessionRef = "source", effectiveConfigRevision = "R1", status = "healthy", epochClosed = false, snapshot = events, cursor = Cursor(events.Length) }
        : new { version = "inbound-observation:v1", sessionRef = "source", effectiveConfigRevision = "R1", status = "healthy", epochClosed = false, events, nextCursor = Cursor(events.Length) };

    [Fact]
    public async Task Snapshot_is_context_poll_preserves_metadata_and_registry_deduplicates_after_restart()
    {
        using var handler = new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : r.RequestUri.AbsolutePath.EndsWith("/open") ? Batch(true, []) : Batch(false, [Event()]));
        using var client = new HttpClient(handler);
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new NeverComplete());
        runtime.UpdateProject(new("test", 1, "project", JsonSerializer.SerializeToElement(new
        {
            version = 1,
            stateScope = "session",
            initialStateId = "initial",
            states = new[]{new {id="initial",name="Initial",graph=new {
                version=3,nodes=new object[]{new {id="dm",kind="dm"},new{id="exit",kind="exit",settings=new{mode="switch",targetStateId="initial"}}},
                edges=Array.Empty<object>(),controlEdges=new[]{new{source="dm",sourcePort="then",target="exit",targetPort="exec"}}
            }}}
        })));
        var opened = await reader.OpenAsync(approval, TestContext.Current.CancellationToken);
        Assert.Empty(opened.Records);
        Assert.Empty(runtime.ReadOutcomes(Session));
        var poll = await reader.PollAsync(approval, opened.Cursor, TestContext.Current.CancellationToken);
        var record = Assert.Single(poll.Records);
        Assert.Equal("N1", record.NativeMessageId); Assert.Equal(1, record.Sequence);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1000), record.OccurredAt);
        var inbound = reader.ToInbound(record);
        var restartedReader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var reopenedApproval = await restartedReader.PreflightAsync(TestContext.Current.CancellationToken);
        var replay = Assert.Single((await restartedReader.PollAsync(reopenedApproval, opened.Cursor, TestContext.Current.CancellationToken)).Records);
        Assert.Equal(record.NativeMessageId, replay.NativeMessageId);
        Assert.Equal(record.Sequence, replay.Sequence);
        var path = Path.Combine(Path.GetTempPath(), "reader-inbox-" + Guid.NewGuid() + ".jsonl");
        try
        {
            using (var inbox = new ShadowInboundJournal(path, Binding, "project", "epoch", 0))
            {
                Assert.Equal("observed", inbox.Observe(inbound).Status);
                var admission = await runtime.ReceiveAsync(Session, inbound.Message, TestContext.Current.CancellationToken);
                Assert.True(admission.Status == SessionAdmissionStatus.Accepted, admission.Reason);
                inbox.Admitted("N1", admission.AdmissionWatermark!.Value);
                await runtime.WaitForIdleAsync(Session, TestContext.Current.CancellationToken);
                var outcome = Assert.Single(runtime.ReadOutcomes(Session));
                inbox.Completed("N1", outcome.BatchId, outcome.ExecutionId, outcome.Success);
                Assert.Equal("duplicate", inbox.Observe(inbound).Status);
            }
            using var restarted = ShadowInboundJournal.StartNonResumableRun(path, Binding, "project", "epoch", 1);
            Assert.Equal("backlog", restarted.Observe(inbound).Status);
        }
        finally { File.Delete(path); }
        Assert.All(handler.Methods, m => Assert.Equal(HttpMethod.Get, m));
        Assert.Single(runtime.ReadOutcomes(Session));
    }

    [Theory]
    [InlineData("degraded")]
    [InlineData("gap_detected")]
    [InlineData("cursor_expired")]
    [InlineData("unavailable")]
    public async Task Unhealthy_is_not_valid_empty(string health)
    {
        using var client = new HttpClient(new Handler(_ => Status(health)));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var error = await Assert.ThrowsAsync<NativeObservationException>(() => reader.PreflightAsync(TestContext.Current.CancellationToken));
        Assert.Equal(health, error.Code);
    }

    [Fact]
    public async Task Wrong_session_is_integrity_failure_and_self_does_not_open_execution()
    {
        var wrong = false;
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : Batch(false, [Event(wrong ? "foreign" : "source", true)])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var cursor = new NativeObservationCursor("inbound-observation:v1", "epoch", "source", 0);
        var own = reader.ToInbound(Assert.Single((await reader.PollAsync(approval, cursor, TestContext.Current.CancellationToken)).Records));
        Assert.True(own.FromMe); Assert.True(Binding.IsSelf(own.ActorId));
        var path = Path.Combine(Path.GetTempPath(), "reader-self-" + Guid.NewGuid() + ".jsonl");
        try
        {
            using var inbox = new ShadowInboundJournal(path, Binding, "project", "epoch", 0);
            Assert.NotEqual("observed", inbox.Observe(own).Status);
        }
        finally { File.Delete(path); }
        wrong = true;
        Assert.Equal("integrity_violation", (await Assert.ThrowsAsync<NativeObservationException>(() => reader.PollAsync(approval, cursor, TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    public async Task Approval_is_reader_local_and_version_or_revisions_are_not_coerced()
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var a = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var b = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await a.PreflightAsync(TestContext.Current.CancellationToken);
        Assert.Equal("invalid_preflight_provenance", (await Assert.ThrowsAsync<NativeObservationException>(() => b.OpenAsync(approval, TestContext.Current.CancellationToken))).Code);
        Assert.Equal("version_mismatch", (await Assert.ThrowsAsync<NativeObservationException>(() => a.PollAsync(approval, new("future", "epoch", "source", 0), TestContext.Current.CancellationToken))).Code);
        using var wrongClient = new HttpClient(new Handler(_ => new { version = "future" }));
        var wrong = new NativeObservationReader(wrongClient, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        Assert.Equal("version_mismatch", (await Assert.ThrowsAsync<NativeObservationException>(() => wrong.PreflightAsync(TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    public async Task Http_conflict_is_stale_not_empty()
    {
        var stale = false;
        using var client = new HttpClient(new FaultHandler(() => stale));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        stale = true;
        Assert.Equal("preflight_stale_or_dispatch_changed", (await Assert.ThrowsAsync<NativeObservationException>(() => reader.OpenAsync(approval, TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    public async Task Foreign_route_cannot_be_bound_to_the_host_session()
    {
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["routeBindingRef"] = "foreign";
        using var client = new HttpClient(new Handler(_ => status));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        Assert.Equal("integrity_violation", (await Assert.ThrowsAsync<NativeObservationException>(() => reader.PreflightAsync(TestContext.Current.CancellationToken))).Code);
    }
    [Theory]
    [InlineData("sequence", "gap_detected")]
    [InlineData("nativeMessageId", "invalid_output")]
    [InlineData("actorRef", "integrity_violation")]
    public async Task Corrupt_records_fail_closed_without_partial_batch(string field, string code)
    {
        var row = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        row[field] = field == "sequence" ? System.Text.Json.Nodes.JsonValue.Create(2) : System.Text.Json.Nodes.JsonValue.Create(field == "nativeMessageId" ? "" : "foreign");
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : Batch(false, [row])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        Assert.Equal(code, (await Assert.ThrowsAsync<NativeObservationException>(() => reader.PollAsync(approval, new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken))).Code);
    }

    private sealed class FaultHandler(Func<bool> stale) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(stale() ? new HttpResponseMessage(HttpStatusCode.Conflict) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Status(), Json)) });
    }

    private sealed class Handler(Func<HttpRequestMessage, object> response) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response(request), Json)) });
        }
    }
    private sealed class NeverComplete : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => throw new InvalidOperationException("No model call allowed.");
    }
}
