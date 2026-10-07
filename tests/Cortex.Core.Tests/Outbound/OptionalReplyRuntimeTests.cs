using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Conversations;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Outbound;
using Xunit;

namespace Cortex.Core.Tests.Outbound;

public sealed class OptionalReplyRuntimeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "optional-reply-" + Guid.NewGuid().ToString("N"));
    private static readonly SessionKey Session = new("test", "filomena", "whatsapp", "account", "120300000001@g.us");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public OptionalReplyRuntimeTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Regeneration_replaces_target_and_receipt_recovery_never_resends(bool clearTarget)
    {
        var project = Project();
        var roles = new OutboundBindings(["recall"], ["compile"], new Dictionary<string, string>
            { ["generate"] = "text", ["regenerate"] = "text" }, ["enforce"], "engaged")
            { ReplyTargetPorts = new Dictionary<string, string> { ["generate"] = "replyTo", ["regenerate"] = "replyTo" } };
        var transport = new Transport();
        ShadowOutboundHost CreateHost() => new(Path.Combine(directory, "recovery"), project.Document,
            roles, [Session], new(true, true, true, true, true, true, true));
        using (var host = CreateHost())
        {
            host.BindLiveRoute(new(Session, "route", "whatsapp", "account", Session.NativeConversationId,
                new("Filomena", "self"), 1000) { AllowExactGroupDelivery = true }, transport);
            host.Rearm(OutboundMode.Enabled);
            var execution = host.Begin(project.Document, "execution", Session, SessionEntryKind.Group,
                ["latest"], ["earlier", "latest"]);
            var memory = new TypedValue(new ArrayType(FlowDomain.Memory), JsonSerializer.SerializeToElement(Array.Empty<object>()));
            execution.Recall("recall", memory);
            execution.Compiled("compile", memory, new(new ArrayType(FlowDomain.Boundary), memory.Value));
            var refs = new Dictionary<string, string> { ["message:earlier"] = "ref1", ["message:latest"] = "ref2" };
            TypedValue Generate(string node, string? target)
            {
                var text = new TypedValue(new StringType(), JsonSerializer.SerializeToElement(node));
                execution.Generated(node, new Dictionary<string, TypedValue> { ["text"] = text,
                    ["replyTo"] = new(new NullableType(new StringType()), JsonSerializer.SerializeToElement(target)) }, refs);
                return text;
            }
            Generate("generate", "ref1");
            var candidate = Generate("regenerate", clearTarget ? null : "ref2");
            Assert.Equal("sent", (await execution.SendAsync(candidate, Token)).Status);
            Assert.Equal(clearTarget ? null : "latest", Assert.Single(transport.Requests).ReplyToMessageId);
        }
        using var restarted = CreateHost();
        var commit = Assert.Single(restarted.PendingSpeechCommits(Session));
        var persisted = JsonSerializer.SerializeToElement(commit);
        Assert.Equal(clearTarget ? null : "latest", persisted.GetProperty("ReplyToMessageId").GetString());
        Assert.Equal("regenerate", commit.Text);
        restarted.CompleteSpeechCommit(Session, commit.AttemptRef, false);
        Assert.Empty(restarted.PendingSpeechCommits(Session));
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData("earlier", false)]
    [InlineData("earlier", true)]
    [InlineData("plain", false)]
    [InlineData("fabricated", false)]
    [InlineData("person", false)]
    [InlineData("quote-only", false)]
    public async Task Runtime_binds_only_exposed_real_message_refs_to_the_exact_candidate(string selection, bool shadow)
    {
        var project = Project();
        var roles = JsonSerializer.Deserialize<OutboundBindings>("""
            {"RecallNodes":["recall"],"CompilerNodes":["compile"],"CandidatePorts":{"generate":"text"},
             "EnforcementNodes":["enforce"],"WouldTransitionTo":"engaged","ReplyTargetPorts":{"generate":"replyTo"}}
            """)!;
        using var host = new ShadowOutboundHost(Path.Combine(directory, "journal"), project.Document,
            roles, [Session], new(true, true, true, true, true, true, true));
        var transport = new Transport();
        host.BindLiveRoute(new(Session, "group-route", "whatsapp", "account", Session.NativeConversationId,
            new("Filomena", "self"), 1000) { AllowExactGroupDelivery = true }, transport);
        host.Rearm(shadow ? OutboundMode.Shadow : OutboundMode.Enabled);
        var completion = new Completion(selection);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion,
            new ShadowRuntimeTests.Reader("empty"), outboundHost: host);
        try { runtime.UpdateProject(project); }
        catch (SessionFlowProjectException error) { Assert.Fail(JsonSerializer.Serialize(error.Issues)); }
        // Historical speech is a real native message, not a generated quote snippet.
        await runtime.ReceiveAsync(Session, Envelope("earlier-provider-id", "Earlier question", SessionMessageDirection.Outbound), Token);
        var rejected = await runtime.ReceiveAsync(Session, Envelope("foreign", "Wrong group", SessionMessageDirection.Inbound)
            with { NativeConversationId = "120399999999@g.us" }, Token);
        Assert.Equal(SessionAdmissionStatus.Rejected, rejected.Status);
        await runtime.ReceiveAsync(Session, Envelope("latest-provider-id", "Latest question", SessionMessageDirection.Inbound), Token);
        await runtime.WaitForIdleAsync(Session, Token);
        var outcome = Assert.Single(runtime.ReadOutcomes(Session));
        Assert.True(outcome.Success, outcome.Error);
        Assert.NotNull(completion.Input);
        Assert.DoesNotContain("provider-id", completion.Input);
        Assert.DoesNotContain("120300000001", completion.Input);
        var status = Assert.Single(runtime.ReadTrace(Session), t => t.EventName == "node-outbound").MemoryMetadata!["result"];
        var valid = selection is "plain" or "earlier";
        Assert.Equal(valid ? shadow ? "shadowed" : "sent" : "blocked", status);
        if (valid && !shadow)
        {
            var request = Assert.Single(transport.Requests);
            Assert.Equal(selection == "earlier" ? "earlier-provider-id" : null, request.ReplyToMessageId);
            var speech = runtime.ReadHistory(Session).Last().Message;
            if (selection == "earlier") Assert.Equal("earlier-provider-id", speech.GetProperty("reply").GetProperty("id").GetString());
            else Assert.Equal(JsonValueKind.Null, speech.GetProperty("reply").ValueKind);
        }
        else Assert.Empty(transport.Requests);
    }

    private static SessionMessageEnvelope Envelope(string id, string text, SessionMessageDirection direction) =>
        new(id, "whatsapp", "account", Session.NativeConversationId, DateTimeOffset.UtcNow, direction, SessionEntryKind.Group,
            JsonSerializer.SerializeToElement(new {
                texto = text, remetente = new { nome = "Helena", stableId = "whatsapp:5511944558218@s.whatsapp.net" },
                canal = new { nome = "Group", nativeId = Session.NativeConversationId },
                reply = new { id = "unobserved-quoted-id", texto = "Quoted only" },
                id = "forged-payload-id"
            }));

    private static SessionFlowProject Project()
    {
        var baseline = ShadowRuntimeTests.Project(false, history: true, sendExit: "sent");
        var document = JsonNode.Parse(baseline.Document.GetRawText())!;
        foreach (var state in document["states"]!.AsArray())
        {
            var graph = state!["graph"]!;
            var nodes = graph["nodes"]!.AsArray();
            nodes.Single(n => n!["id"]!.GetValue<string>() == "dm")!["kind"] = "group";
            var generator = nodes.Single(n => n!["id"]!.GetValue<string>() == "generate")!;
            generator.AsObject().Remove("outputMode");
            generator["responseFields"]!.AsArray().Add(JsonNode.Parse("""{"id":"replyTo","name":"replyTo","type":"text?"}"""));
            generator["inputs"] = JsonNode.Parse("""[{"id":"context","name":"context","type":"message[]","required":true},{"id":"batch","name":"batch","type":"message[]","required":true}]""");
            graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"history","sourcePort":"messages","target":"generate","targetPort":"context"}"""));
            graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"batch","sourcePort":"messages","target":"generate","targetPort":"batch"}"""));
        }
        return baseline with { Document = JsonSerializer.SerializeToElement(document) };
    }

    private sealed class Completion(string selection) : IFlowCompletion
    {
        public string? Input { get; private set; }
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (request.NodeId != "generate")
                return Task.FromResult(JsonSerializer.Serialize(request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao")));
            Input = request.Inputs.GetRawText();
            var message = request.Inputs.GetProperty("context")[0];
            Assert.NotNull(request.HostReferences);
            Assert.Equal(message.GetProperty("ref").GetString(), request.HostReferences["message:earlier-provider-id"]);
            Assert.DoesNotContain("earlier-provider-id", JsonSerializer.Serialize(request));
            var target = selection switch
            {
                "plain" => null,
                "earlier" => message.TryGetProperty("ref", out var reference) ? reference.GetString() : "missing-message-ref",
                "person" => message.GetProperty("remetente").GetProperty("ref").GetString(),
                "quote-only" => message.GetProperty("reply").GetProperty("ref").GetString(),
                _ => "ref9999",
            };
            return Task.FromResult(JsonSerializer.Serialize(new { text = "Reply candidate", replyTo = target }));
        }
    }

    private sealed class Transport : IBoundOutboundTransport
    {
        public List<BoundSendRequest> Requests { get; } = [];
        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken token) =>
            Task.FromResult(new TransportRouteDescription("whatsapp", "account", Session.NativeConversationId));
        public Task<SendReceipt> SendTextAsync(string route, string text, CancellationToken token) => throw new InvalidOperationException("Legacy send");
        public Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken token)
        {
            Requests.Add(request);
            var receipt = JsonSerializer.SerializeToNode(new SendReceipt("sent-provider-id") {
                Channel = "whatsapp", AccountId = "account", Recipient = Session.NativeConversationId })!;
            receipt["ReplyToMessageId"] = request.ReplyToMessageId;
            return Task.FromResult(receipt.Deserialize<SendReceipt>()!);
        }
    }
    public void Dispose() => Directory.Delete(directory, true);
}
