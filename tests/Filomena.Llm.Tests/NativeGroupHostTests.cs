using Filomena.App;
using Filomena.Core.Conversations;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Core.Flows;
using Filomena.Core.Memory;
using Filomena.Memory;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeGroupHostTests
{
    private const string Group = "120300000001@g.us";
    private const string Alice = "whatsapp:15550000001@s.whatsapp.net";
    private const string Bob = "whatsapp:15550000002@s.whatsapp.net";
    private static ManualShadowConfig Config() => new("project.json", "project", 3, "hash",
        new("runtime", "filomena", "whatsapp", "default", Group),
        FilomenaIdentity.WhatsAppConversationNamespace(Group), ["whatsapp:15550000003@s.whatsapp.net"],
        Alice, "Alice", new(["generate"], ["regenerate"], new Dictionary<string, string>(), ["enforce"], null), "journal", "http://127.0.0.1:8000",
        [Probe(Alice, "private"), Probe(Alice, "plex"), Probe(Bob, "private"), Probe(Bob, "plex")]);
    private static ShadowGraphitiProbeRequest Probe(string actor, string foreign) =>
        new(FilomenaIdentity.WhatsAppConversationNamespace(Group), actor, "GROUP_CANARY", foreign, foreign + "_CANARY");
    private static Dictionary<string, string> Participants() => new()
    {
        ["15550000001@s.whatsapp.net"] = Alice,
        ["90000000001@lid"] = Alice,
        ["15550000002@s.whatsapp.net"] = Bob
    };

    [Fact]
    public void Explicit_group_delivery_authority_binds_only_the_trusted_group()
    {
        var binding = NativeObservationHostBinding.Create(Config(), Group, Participants(), true, true);
        Assert.Equal(SessionEntryKind.Group, binding.Kind);
        Assert.Equal(Group, binding.Session.NativeConversationId);
        Assert.ThrowsAny<ArgumentException>(() => NativeObservationHostBinding.Create(Config(), "120399999999@g.us", Participants(), true, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Group_memory_probe_requires_preflight_only_and_never_starts_runtime(bool preflightOnly)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var calls = 0;
        server.Run(context =>
        {
            Interlocked.Increment(ref calls);
            context.Response.StatusCode = 503;
            return Task.CompletedTask;
        });
        var ct = TestContext.Current.CancellationToken;
        await server.StartAsync(ct);
        var directory = Directory.CreateTempSubdirectory("group-memory-probe-");
        try
        {
            var project = Path.Combine(ParticipationGateV1Tests.Root, "docs/manual-group-shadow-project-v3-gpt6-luna.json");
            var config = Config() with
            {
                ProjectFile = project,
                ExpectedProjectSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(project, ct))),
                JournalDirectory = directory.FullName,
                GraphitiEndpoint = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/mcp",
                OutboundBindings = new(["group-response-boundaries"], ["group-compiled-boundaries"],
                    new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
                    ["group-validator-1", "group-validator-2"], "participation-engaged-v1")
            };
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var file = Path.Combine(directory.FullName, "manual.json");
            var participants = Path.Combine(directory.FullName, "participants.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(config, json), ct);
            await File.WriteAllTextAsync(participants, JsonSerializer.Serialize(Participants(), json), ct);
            using var output = new StringWriter();
            string[] args = ["--manual-shadow", file, "--group-participants", participants,
                .. preflightOnly ? new[] { "--preflight-only" } : Array.Empty<string>()];
            Assert.Equal(2, await ManualShadowCommand.RunAsync(args, new StringReader("/quit"), output, ct));
            if (preflightOnly)
            {
                Assert.True(calls > 0, "Valid group preflight must reach Graphiti read-only client.");
                Assert.Contains("graphiti_preflight_failed", output.ToString());
                Assert.DoesNotContain("invalid_host_configuration", output.ToString());
            }
            else Assert.Equal(0, calls);
            Assert.DoesNotContain("run_started", output.ToString());
            Assert.DoesNotContain("WOULD_SEND", output.ToString());
        }
        finally
        {
            await server.StopAsync(ct);
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Frozen_group_project_runs_two_actors_once_in_shadow_without_speech_state()
    {
        var directory = Directory.CreateTempSubdirectory("native-group-runtime-");
        var projectFile = Path.Combine(ParticipationGateV1Tests.Root, "docs/manual-group-shadow-project-v3-gpt6-luna.json");
        var bytes = await File.ReadAllBytesAsync(projectFile, TestContext.Current.CancellationToken);
        var pin = PinnedShadowProject.FromBytes(bytes, "group-pilot-test", 3,
            Convert.ToHexString(SHA256.HashData(bytes)));
        var config = Config();
        var binding = NativeObservationHostBinding.Create(config, Group, Participants(), live: false);
        var roles = new OutboundBindings(["group-response-boundaries"], ["group-compiled-boundaries"],
            new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
            ["group-validator-1", "group-validator-2"], "participation-engaged-v1");
        var model = new GroupSignals();
        var memory = new GroupMemory();
        await using var run = new ObservedShadowRun(pin, binding, directory.FullName, roles, model, memory, "epoch", 0);
        CanonicalShadowInbound Input(long sequence, string actor, bool self = false) =>
            new("epoch", sequence, config.Session, SessionEntryKind.Group, config.ConversationNamespace, actor,
                new("group-event-" + sequence, "whatsapp", "default", Group, DateTimeOffset.UtcNow,
                    SessionMessageDirection.Inbound, SessionEntryKind.Group,
                    JsonSerializer.SerializeToElement(new { texto = "Filomena, me ajuda com isso?" })), self);
        var first = Input(1, Alice);
        var second = Input(2, Bob);
        Assert.True(Assert.Single((await run.AcceptAsync([first], TestContext.Current.CancellationToken)).Outcomes).Success);
        Assert.True(Assert.Single((await run.AcceptAsync([second], TestContext.Current.CancellationToken)).Outcomes).Success);
        Assert.Empty((await run.AcceptAsync([second], TestContext.Current.CancellationToken)).Outcomes);
        Assert.Empty((await run.AcceptAsync([Input(3, config.SelfActorIds.Single(), true)], TestContext.Current.CancellationToken)).Outcomes);
        Assert.Equal(2, run.Outcomes.Count);
        Assert.All(run.Outcomes, outcome => Assert.False(outcome.TransitionApplied));
        Assert.Equal(2, run.History.Count);
        Assert.All(run.History, item => Assert.Equal(SessionMessageDirection.Inbound, item.Direction));
        Assert.Equal(2, run.Trace.Count(e => e.EventName == "node-outbound" && e.MemoryMetadata!["result"] == "shadowed"));
        Assert.Equal(2, model.Calls.Count(id => id == "group-generation"));
        Assert.DoesNotContain(model.Calls, id => id.EndsWith("-presence", StringComparison.Ordinal));
        Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
        Assert.NotEmpty(memory.Queries);
        Assert.All(memory.Queries, query => Assert.Equal(config.ConversationNamespace, query.ConversationNamespace));
        Assert.Contains(memory.Queries.SelectMany(query => query.Persons), person => person.ActorStableId == Alice);
        Assert.Contains(memory.Queries.SelectMany(query => query.Persons), person => person.ActorStableId == Bob);
    }

    private sealed class GroupMemory : IMemoryReader
    {
        public List<MemoryQuery> Queries { get; } = [];
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<MemoryItem>>([]);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1)]
    public async Task Approved_group_live_batches_two_people_and_records_only_confirmed_speech(int budget)
    {
        var directory = Directory.CreateTempSubdirectory("group-live-runtime-");
        try
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(ParticipationGateV1Tests.Root,
                "docs/manual-group-shadow-project-v3-gpt6-luna.json"), TestContext.Current.CancellationToken);
            var pin = PinnedShadowProject.FromBytes(bytes, "group-live-test", 3, Convert.ToHexString(SHA256.HashData(bytes)));
            var config = Config();
            var binding = NativeObservationHostBinding.Create(config, Group, Participants(), false);
            var roles = new OutboundBindings(["group-response-boundaries"], ["group-compiled-boundaries"],
                new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
                ["group-validator-1", "group-validator-2"], "participation-engaged-v1");
            var sender = new GroupSender();
            var route = LiveRouteBindings.FromShadow(binding, "group-route", Group, "Filomena", 4000)
                with { AllowExactGroupDelivery = true };
            await using var run = new ObservedLiveRun(pin, binding, directory.FullName, roles,
                new GroupSignals(), new GroupMemory(), "epoch", 0, route, sender,
                new(true, true, true, true, true, true, true), _ => Task.CompletedTask, budget);
            CanonicalShadowInbound Input(long n, string actor, bool self = false) => new("epoch", n,
                config.Session, SessionEntryKind.Group, config.ConversationNamespace, actor,
                new("native-group-" + n, "whatsapp", "default", Group, DateTimeOffset.UtcNow,
                    SessionMessageDirection.Inbound, SessionEntryKind.Group,
                    JsonSerializer.SerializeToElement(new { texto = "Filomena, me ajuda?" })), self);
            var inputs = new[] { Input(1, Alice), Input(2, Bob) };
            var result = await run.AcceptAsync(inputs, TestContext.Current.CancellationToken);
            if (budget == 1)
            {
                Assert.All(result.Decisions, d => Assert.Equal("live_interaction_budget_exceeded", d.Diagnostic));
                Assert.Empty(result.Outcomes);
                Assert.Empty(sender.Requests);
                Assert.Empty(run.History);
                return;
            }
            Assert.All(result.Decisions, d => Assert.Equal("admitted", d.Status));
            Assert.NotEmpty(result.Outcomes);
            Assert.All(result.Outcomes, o => Assert.True(o.Success));
            Assert.Single(sender.Requests);
            Assert.Equal(Group, sender.Requests[0].Recipient);
            Assert.Equal(2, run.History.Count(h => h.Direction == SessionMessageDirection.Inbound));
            Assert.Single(run.History, h => h.Direction == SessionMessageDirection.Outbound);
            Assert.Empty((await run.AcceptAsync(inputs, TestContext.Current.CancellationToken)).Outcomes);
            Assert.Empty((await run.AcceptAsync([Input(3, config.SelfActorIds[0], true)], TestContext.Current.CancellationToken)).Outcomes);
            Assert.Single(sender.Requests);
        }
        finally { directory.Delete(true); }
    }

    private sealed class GroupSender : IBoundOutboundTransport
    {
        public List<BoundSendRequest> Requests { get; } = [];
        public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken token) =>
            Task.FromResult(new TransportRouteDescription("whatsapp", "default", Group));
        public Task<SendReceipt> SendTextAsync(string route, string text, CancellationToken token) =>
            throw new InvalidOperationException("Unbound send forbidden");
        public Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult(new SendReceipt("group-provider-id") { Channel = "whatsapp", AccountId = "default", Recipient = Group });
        }
    }

    [Fact]
    public void Group_operational_variant_changes_only_send_wiring_not_brain_or_DM()
    {
        var root = ParticipationGateV1Tests.Root;
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/manual-live-project-v3-gpt6-luna.json")));
        var restored = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/manual-group-shadow-project-v3-gpt6-luna.json")))!;
        foreach (var state in restored["project"]!["states"]!.AsArray())
        {
            var graph = state!["graph"]!;
            foreach (var sink in new[] { "group-transition-responder", "group-accepted-1", "group-accepted-2" })
            {
                var id = sink + "-shadow-send";
                var nodes = graph["nodes"]!.AsArray();
                Assert.Equal("send", nodes.Single(n => n!["id"]!.GetValue<string>() == id)!["kind"]!.GetValue<string>());
                nodes.Remove(nodes.Single(n => n!["id"]!.GetValue<string>() == id));
                var data = graph["edges"]!.AsArray();
                data.Remove(data.Single(e => e!["target"]!.GetValue<string>() == id));
                var control = graph["controlEdges"]!.AsArray();
                var exit = control.Single(e => e!["source"]!.GetValue<string>() == id);
                Assert.Equal("sent", exit!["sourcePort"]!.GetValue<string>());
                control.Remove(exit);
                control.Single(e => e!["target"]!.GetValue<string>() == id)!["target"] = sink;
            }
        }
        Assert.True(JsonNode.DeepEquals(original, restored));
    }

    [Theory]
    [InlineData("dm-roles")]
    [InlineData("bypass")]
    [InlineData("orphan-send")]
    public void Group_send_preflight_rejects_misbound_or_bypassed_roles(string mutation)
    {
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root,
            "docs/manual-group-shadow-project-v3-gpt6-luna.json")))!;
        var graph = project["project"]!["states"]![0]!["graph"]!;
        var control = graph["controlEdges"]!.AsArray();
        var route = control.Single(edge => edge!["target"]!.GetValue<string>() == "group-transition-responder-shadow-send");
        if (mutation == "bypass") route!["target"] = "group-transition-responder";
        if (mutation == "orphan-send")
            foreach (var edge in control.Where(edge => edge!["source"]!.GetValue<string>() == "group-generation").ToArray()) control.Remove(edge);
        var bytes = System.Text.Encoding.UTF8.GetBytes(project.ToJsonString());
        if (mutation != "dm-roles")
        {
            // These mutations break control dominance; the real compiler rejects
            // them even earlier than the operational role check.
            Assert.Throws<SessionFlowProjectException>(() => PinnedShadowProject.FromBytes(bytes,
                "invalid-group", 3, Convert.ToHexString(SHA256.HashData(bytes))));
            return;
        }
        var pin = PinnedShadowProject.FromBytes(bytes, "invalid-group", 3, Convert.ToHexString(SHA256.HashData(bytes)));
        var prefix = mutation == "dm-roles" ? "dm" : "group";
        var roles = new OutboundBindings([prefix + "-response-boundaries"], [prefix + "-compiled-boundaries"],
            new Dictionary<string, string> { [prefix + "-generation"] = "texto", [prefix + "-regeneration"] = "texto" },
            [prefix + "-validator-1", prefix + "-validator-2"], "participation-engaged-v1");
        Assert.Throws<InvalidDataException>(() => NativeObservationHostBinding.ValidateGroupSendWiring(pin, roles));
    }

    private sealed class GroupSignals : IFlowCompletion
    {
        public List<string> Calls { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Calls.Add(request.NodeId);
            if (request.NodeId == "group-situation")
                return new DirectParticipationRuleTests.Signals("direto", "calar").CompleteAsync(request, token);
            if (request.NodeId == "group-generation") return Task.FromResult("Vamos ver isso juntos.");
            throw new InvalidOperationException("Unexpected group model call: " + request.NodeId);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Native_command_requires_group_send_wiring_before_V2_status(bool groupWired, bool live)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var paths = new List<string>();
        server.Run(async context =>
        {
            paths.Add(context.Request.Path);
            await context.Response.WriteAsJsonAsync(new { version = "unsupported" }, TestContext.Current.CancellationToken);
        });
        await server.StartAsync(TestContext.Current.CancellationToken);
        var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var directory = Directory.CreateTempSubdirectory("native-group-command-");
        var credentialName = "FILOMENA_TEST_READER_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(credentialName, "test-only-reader");
        try
        {
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var project = Path.Combine(ParticipationGateV1Tests.Root, groupWired
                ? "docs/manual-group-shadow-project-v3-gpt6-luna.json"
                : "docs/manual-live-project-v3-gpt6-luna.json");
            var config = Config() with
            {
                ProjectFile = project, ProjectId = "filomena-v3-live-sent",
                ExpectedProjectSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken))),
                OutboundBindings = new(["group-response-boundaries"], ["group-compiled-boundaries"],
                    new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
                    ["group-validator-1", "group-validator-2"], "participation-engaged-v1")
            };
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "manual.json"), JsonSerializer.Serialize(config, json), TestContext.Current.CancellationToken);
            var native = new NativeShadowConfig("manual.json", address, credentialName, "source", Group, "R1", directory.FullName,
                GroupParticipants: Participants());
            var file = Path.Combine(directory.FullName, "native.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(native, json), TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            if (live)
            {
                var capabilities = new TransportSafetyCapabilities(true, true, true, true, true, true, true);
                var proof = JsonSerializer.SerializeToUtf8Bytes(new {
                    version = "native-live-capabilities:v1", approvedRevision = "R1", observationSessionRef = "source",
                    routeBindingRef = Filomena.OpenClaw.Observation.NativeObservationReader.RouteBindingRef(config.Session),
                    boundSendContract = "whatsapp-bound-group-text:v1", capabilities, maxInteractions = 5
                }, json);
                await File.WriteAllBytesAsync(Path.Combine(directory.FullName, "proof.json"), proof, TestContext.Current.CancellationToken);
                var liveFile = Path.Combine(directory.FullName, "live.json");
                await File.WriteAllTextAsync(liveFile, JsonSerializer.Serialize(new {
                    nativeConfigFile = file, mcp = new Filomena.OpenClaw.Mcp.OpenClawMcpClientOptions("never-start", []),
                    transportRoute = "group-route", speakerName = "Filomena", maxSinglePartCharacters = 4000,
                    capabilities, capabilityEvidenceFile = "proof.json", capabilityEvidenceSha256 = Convert.ToHexString(SHA256.HashData(proof)),
                    maxInteractions = 5, allowExactGroupDelivery = true
                }, json), TestContext.Current.CancellationToken);
                Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", liveFile, "--authorize-five-interactions"],
                    new StringReader("/quit"), output, TestContext.Current.CancellationToken));
            }
            else Assert.Equal(2, await NativeShadowCommand.RunAsync(["--native-shadow", file], new StringReader("/quit"), output, TestContext.Current.CancellationToken));
            if (groupWired)
            {
                Assert.Equal("/inbound-observation/v2/status", Assert.Single(paths));
                Assert.Contains("ABORT", output.ToString());
                Assert.DoesNotContain("invalid_host_configuration", output.ToString());
                Assert.Contains("\"sendBoundaryAttempts\":0", output.ToString());
            }
            else
            {
                Assert.Empty(paths);
                Assert.Contains("invalid_host_configuration", output.ToString());
            }
            Assert.DoesNotContain("run_started", output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(credentialName, null);
            await server.StopAsync(TestContext.Current.CancellationToken);
            directory.Delete(true);
        }
    }

    [Fact]
    public void Group_host_binds_every_canonical_participant_without_using_display_name()
    {
        var binding = NativeObservationHostBinding.Create(Config(), Group, Participants(), false);
        Assert.Equal(SessionEntryKind.Group, binding.Kind);
        Assert.True(binding.IsPeer(Alice));
        Assert.True(binding.IsPeer(Bob));
        Assert.False(binding.IsPeer("Alice"));
        Assert.False(binding.IsPeer("whatsapp:15550000004@s.whatsapp.net"));
        Assert.Equal(Group, binding.Session.NativeConversationId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown-native")]
    [InlineData("self")]
    [InlineData("foreign-probe")]
    [InlineData("missing-probe")]
    [InlineData("duplicate-probe")]
    [InlineData("wrong-group")]
    [InlineData("enabled")]
    public void Invalid_group_authority_is_rejected_before_any_remote_operation(string mutation)
    {
        var config = Config();
        var participants = Participants();
        var native = Group;
        if (mutation == "missing") participants.Clear();
        if (mutation == "unknown-native") participants["Alice"] = Alice;
        if (mutation == "self") participants["15550000003@s.whatsapp.net"] = config.SelfActorIds[0];
        if (mutation == "foreign-probe") config = config with { GraphitiProbes = [.. config.GraphitiProbes.Select(p => p with { ConversationNamespace = "private-dm" })] };
        if (mutation == "missing-probe") config = config with { GraphitiProbes = config.GraphitiProbes.Take(2).ToArray() };
        if (mutation == "duplicate-probe") config = config with { GraphitiProbes = [config.GraphitiProbes[0], config.GraphitiProbes[0], config.GraphitiProbes[2], config.GraphitiProbes[3]] };
        if (mutation == "wrong-group") native = "120300000002@g.us";
        Assert.ThrowsAny<ArgumentException>(() => NativeObservationHostBinding.Create(config, native, participants, mutation == "enabled"));
    }

    [Fact]
    public void Dm_keeps_PN_memory_binding_and_native_LID_transport_alias()
    {
        var dm = "15550000001@s.whatsapp.net";
        var config = Config() with
        {
            Session = Config().Session with { NativeConversationId = dm },
            ConversationNamespace = FilomenaIdentity.WhatsAppConversationNamespace(dm),
            GraphitiProbes = Config().GraphitiProbes.Take(2).Select(p => p with
                { ConversationNamespace = FilomenaIdentity.WhatsAppConversationNamespace(dm) }).ToArray()
        };
        var binding = NativeObservationHostBinding.Create(config, "90000000001@lid", null, true);
        Assert.Equal(SessionEntryKind.Dm, binding.Kind);
        Assert.True(binding.IsPeer(Alice));
        Assert.False(binding.IsPeer(Bob));
        Assert.ThrowsAny<ArgumentException>(() => NativeObservationHostBinding.Create(config, "90000000001@lid", null, true, true));
        Assert.ThrowsAny<ArgumentException>(() => NativeObservationHostBinding.Create(config, "90000000001@lid", Participants(), false));
    }
}
