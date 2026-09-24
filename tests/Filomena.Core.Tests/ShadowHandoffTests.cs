using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Core.Tests.Outbound;
using Xunit;

namespace Filomena.Core.Tests;

public sealed class ShadowHandoffTests : IDisposable
{
    [Fact]
    public void Quoted_context_survives_canonicalization_and_model_projection_without_native_ids()
    {
        var input = Input(1, "incoming");
        var payload = JsonNode.Parse(input.Message.Message.GetRawText())!;
        payload["reply"] = JsonSerializer.SerializeToNode(new { id = "private-quoted-id", texto = "A pergunta citada",
            origem = "citacao_do_remetente" });
        input = input with { Message = input.Message with { Message = JsonSerializer.SerializeToElement(payload) } };
        var normalized = ShadowInboundCanonicalizer.Normalize(input);
        var model = FlowDomain.ForModel(normalized.Message, FlowDomain.Message, new Dictionary<string, string>());
        Assert.Equal("A pergunta citada", model.GetProperty("reply").GetProperty("texto").GetString());
        Assert.DoesNotContain("private-quoted-id", model.GetRawText());
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), "shadow-handoff-" + Guid.NewGuid());
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "default", "5511944558218@s.whatsapp.net");
    public ShadowHandoffTests() => Directory.CreateDirectory(directory);
    private static CanonicalShadowInbound Input(long sequence, string id) => new("epoch", sequence, Session, SessionEntryKind.Dm,
        FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId), "whatsapp:5511944558218@s.whatsapp.net",
        new(id, Session.ChannelId, Session.AccountId, Session.NativeConversationId, DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
            JsonSerializer.SerializeToElement(new { texto = "O trabalho demorou.", remetente = new { nome = "Helena", stableId = "whatsapp:5511944558218@s.whatsapp.net" }, canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null })));

    [Fact]
    public void Host_bound_group_accepts_only_its_exact_group_and_trusted_participants()
    {
        var group = Session with { NativeConversationId = "123456@g.us" };
        var ns = FilomenaIdentity.WhatsAppConversationNamespace(group.NativeConversationId);
        var actor = "whatsapp:5511944558218@s.whatsapp.net";
        var binding = new ShadowSessionBinding(group, ns, ["whatsapp:5511999999999@s.whatsapp.net"], [actor], SessionEntryKind.Group);
        var input = new CanonicalShadowInbound("epoch", 1, group, SessionEntryKind.Group, ns, actor,
            new("native-group-id", group.ChannelId, group.AccountId, group.NativeConversationId, DateTimeOffset.UtcNow,
                SessionMessageDirection.Inbound, SessionEntryKind.Group, JsonSerializer.SerializeToElement(new { texto = "oi" })));
        Assert.True(binding.Accepts(input));
        var normalized = ShadowInboundCanonicalizer.Normalize(input);
        Assert.Equal("Grupo", normalized.Message.GetProperty("canal").GetProperty("nome").GetString());
        Assert.Equal(actor, normalized.Message.GetProperty("remetente").GetProperty("stableId").GetString());
        Assert.False(binding.Accepts(input with { ActorId = "whatsapp:other@s.whatsapp.net" }));
        Assert.False(binding.Accepts(input with { Kind = SessionEntryKind.Dm }));
        Assert.False(binding.Accepts(input with { Session = group with { NativeConversationId = "other@g.us" } }));
    }

    [Fact]
    public async Task Group_inbound_reaches_existing_group_flow_and_only_would_send()
    {
        var group = Session with { NativeConversationId = "123456@g.us" };
        var ns = FilomenaIdentity.WhatsAppConversationNamespace(group.NativeConversationId);
        var actor = "whatsapp:5511944558218@s.whatsapp.net";
        var source = ShadowRuntimeTests.Project(false);
        var document = JsonNode.Parse(source.Document.GetRawText())!.AsObject();
        foreach (var state in document["states"]!.AsArray())
        {
            var graph = state!["graph"]!;
            foreach (var node in graph["nodes"]!.AsArray().Where(node => node!["id"]!.GetValue<string>() == "dm"))
            {
                node!["id"] = "group";
                node["kind"] = "group";
            }
            foreach (var edge in graph["edges"]!.AsArray().Where(edge => edge!["source"]!.GetValue<string>() == "dm")) edge!["source"] = "group";
            foreach (var edge in graph["controlEdges"]!.AsArray().Where(edge => edge!["source"]!.GetValue<string>() == "dm")) edge!["source"] = "group";
        }
        var pin = new PinnedShadowProject(new(source.ProjectId, source.ProjectVersion, source.ProjectRevision,
            JsonSerializer.SerializeToElement(document)));
        using var host = new ShadowOutboundHost(Path.Combine(directory, "group-out.jsonl"), pin.Project.Document,
            new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged"),
            [group], TransportSafetyCapabilities.Unproven);
        host.Rearm(OutboundMode.Shadow);
        var binding = new ShadowSessionBinding(group, ns, ["whatsapp:5511999999999@s.whatsapp.net"], [actor], SessionEntryKind.Group);
        using var inbox = new ShadowInboundJournal(Path.Combine(directory, "group-in.jsonl"), binding, pin.Project.ProjectRevision, "epoch", 10);
        await using var handoff = new ShadowRuntimeHandoff(pin, binding, inbox, host,
            new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"));
        var inbound = new CanonicalShadowInbound("epoch", 11, group, SessionEntryKind.Group, ns, actor,
            new("group-native-1", group.ChannelId, group.AccountId, group.NativeConversationId, DateTimeOffset.UtcNow,
                SessionMessageDirection.Inbound, SessionEntryKind.Group, JsonSerializer.SerializeToElement(new { texto = "Filomena, oi" })));
        Assert.Equal("admitted", (await handoff.AcceptAsync(inbound, TestContext.Current.CancellationToken)).Status);
        await handoff.DrainAsync(TestContext.Current.CancellationToken);
        Assert.Single(handoff.Outcomes);
        Assert.Contains(handoff.Trace, x => x.EventName == "node-outbound" && x.MemoryMetadata!["result"] == "shadowed");
    }

    [Theory]
    [InlineData("boundary")]
    [InlineData("empty")]
    [InlineData("outage")]
    public async Task Normal_runtime_with_pinned_project_durable_ingress_and_no_send(string scenario)
    {
        var project = ShadowRuntimeTests.Project(false);
        var pin = new PinnedShadowProject(project);
        using var host = new ShadowOutboundHost(Path.Combine(directory, "outbound.jsonl"), pin.Project.Document,
            new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged"), [Session], TransportSafetyCapabilities.Unproven);
        host.Rearm(OutboundMode.Shadow);
        var binding = new ShadowSessionBinding(Session, Input(1, "i").ConversationNamespace, ["whatsapp:5511999999999@s.whatsapp.net"], ["whatsapp:5511944558218@s.whatsapp.net"]);
        using var inbox = new ShadowInboundJournal(Path.Combine(directory, "inbox.jsonl"), binding, pin.Project.ProjectRevision, "epoch", 10);
        await using var handoff = new ShadowRuntimeHandoff(pin, binding, inbox, host, new ShadowRuntimeTests.Completion(scenario), new ShadowRuntimeTests.Reader(scenario));
        Assert.Equal("admitted", (await handoff.AcceptAsync(Input(11, "input-1"), TestContext.Current.CancellationToken)).Status);
        Assert.Equal("duplicate", (await handoff.AcceptAsync(Input(11, "input-1"), TestContext.Current.CancellationToken)).Status);
        await handoff.DrainAsync(TestContext.Current.CancellationToken);
        var outcome = Assert.Single(handoff.Outcomes);
        Assert.Single(handoff.History);
        Assert.Equal(SessionMessageDirection.Inbound, handoff.History[0].Direction);
        Assert.False(outcome.TransitionApplied);
        Assert.Contains(handoff.Audit, a => a.ExecutionId == outcome.ExecutionId && a.BatchId == outcome.BatchId);
        Assert.DoesNotContain("5511944558218", JsonSerializer.Serialize(handoff.Audit));
        if (scenario == "outage") { Assert.False(outcome.Success); Assert.DoesNotContain(handoff.Trace, t => t.EventName == "node-outbound"); }
        else Assert.Contains(handoff.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "shadowed" && t.MemoryMetadata["wouldTransitionTo"] == "engaged");
    }

    [Fact]
    public async Task Inbounds_during_generation_are_preserved_and_enter_existing_serial_queue()
    {
        var pin = new PinnedShadowProject(ShadowRuntimeTests.Project(false));
        using var host = new ShadowOutboundHost(Path.Combine(directory, "out.jsonl"), pin.Project.Document,
            new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], null), [Session], TransportSafetyCapabilities.Unproven);
        host.Rearm(OutboundMode.Shadow);
        var binding = new ShadowSessionBinding(Session, Input(1, "i").ConversationNamespace, ["whatsapp:5511999999999@s.whatsapp.net"], ["whatsapp:5511944558218@s.whatsapp.net"]);
        using var inbox = new ShadowInboundJournal(Path.Combine(directory, "in.jsonl"), binding, pin.Project.ProjectRevision, "epoch", 10);
        var completion = new PausedCompletion();
        await using var handoff = new ShadowRuntimeHandoff(pin, binding, inbox, host, completion, new ShadowRuntimeTests.Reader("empty"));
        await handoff.AcceptAsync(Input(11, "one"), TestContext.Current.CancellationToken);
        await completion.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("admitted", (await handoff.AcceptAsync(Input(12, "two"), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, handoff.History.Count);
        completion.Release.SetResult();
        await handoff.DrainAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, handoff.Outcomes.Count);
        Assert.Equal(["one", "two"], handoff.Outcomes.SelectMany(o => o.ProviderMessageIds));
    }

    [Fact]
    public async Task Disarm_preserves_input_and_blocks_subsequent_execution_until_reconciled()
    {
        var pin = new PinnedShadowProject(ShadowRuntimeTests.Project(false));
        using var host = new ShadowOutboundHost(Path.Combine(directory, "out.jsonl"), pin.Project.Document,
            new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], null), [Session], TransportSafetyCapabilities.Unproven);
        host.Rearm(OutboundMode.Shadow);
        var binding = new ShadowSessionBinding(Session, Input(1, "i").ConversationNamespace, ["whatsapp:5511999999999@s.whatsapp.net"], ["whatsapp:5511944558218@s.whatsapp.net"]);
        using var inbox = new ShadowInboundJournal(Path.Combine(directory, "in.jsonl"), binding, pin.Project.ProjectRevision, "epoch", 10);
        await using var handoff = new ShadowRuntimeHandoff(pin, binding, inbox, host, new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"));
        host.Rearm(OutboundMode.Disabled);
        Assert.Equal("preserved", (await handoff.AcceptAsync(Input(11, "one"), TestContext.Current.CancellationToken)).Status);
        host.Rearm(OutboundMode.Shadow);
        Assert.Equal("reconciliation_required", (await handoff.AcceptAsync(Input(12, "two"), TestContext.Current.CancellationToken)).Status);
        Assert.Empty(handoff.History);
        Assert.Empty(handoff.Outcomes);
    }

    [Fact]
    public void Payload_cannot_override_bound_person_or_channel()
    {
        var input = Input(11, "one");
        var forged = input with
        {
            Message = input.Message with
            {
                Message = JsonSerializer.SerializeToElement(new
                { texto = "hello", remetente = new { nome = "Peer", stableId = "forged", participantUuid = "forged" }, canal = new { nativeId = "foreign" } })
            }
        };
        var normalized = ShadowInboundCanonicalizer.Normalize(forged);
        Assert.Equal(input.ActorId, normalized.Message.GetProperty("remetente").GetProperty("stableId").GetString());
        Assert.Equal(Session.NativeConversationId, normalized.Message.GetProperty("canal").GetProperty("nativeId").GetString());
        Assert.False(normalized.Message.GetProperty("remetente").TryGetProperty("participantUuid", out _));
    }

    [Theory]
    [InlineData("{}", "")]
    [InlineData("{\"nome\":null}", "")]
    [InlineData("{\"nome\":\"\"}", "")]
    [InlineData("{\"nome\":\"Helena\"}", "Helena")]
    public void Canonicalization_preserves_absent_or_known_names_without_inventing_labels(string sender, string expected)
    {
        var input = Input(11, "one");
        input = input with { Message = input.Message with { Message = JsonSerializer.SerializeToElement(new
        { texto = "hello", remetente = JsonSerializer.Deserialize<JsonElement>(sender) }) } };
        var normalized = ShadowInboundCanonicalizer.Normalize(input);
        Assert.Equal(input.ActorId, normalized.Message.GetProperty("remetente").GetProperty("stableId").GetString());
        var model = FlowDomain.ForModel(normalized.Message, FlowDomain.Message, new Dictionary<string, string>());
        Assert.Equal(expected, model.GetProperty("remetente").GetProperty("nome").GetString());
        Assert.DoesNotContain(input.ActorId, model.GetRawText());
    }

    [Fact]
    public void Pin_does_not_follow_mutated_json_or_accept_another_digest()
    {
        var original = ShadowRuntimeTests.Project(false);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original.Document);
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var pin = PinnedShadowProject.FromBytes(bytes, original.ProjectId, 1, digest);
        Array.Fill(bytes, (byte)0);
        Assert.Equal(original.Document.GetRawText(), pin.Project.Document.GetRawText());
        Assert.Throws<InvalidDataException>(() => PinnedShadowProject.FromBytes(bytes, original.ProjectId, 1, digest));
    }

    private sealed class PausedCompletion : IFlowCompletion
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(token); return "Vamos por partes.";
        }
    }
    public void Dispose() => Directory.Delete(directory, true);
}
