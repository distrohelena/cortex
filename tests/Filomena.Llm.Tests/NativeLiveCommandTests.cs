using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeLiveCommandTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    public void Reply_wiring_requires_explicit_live_capability(bool authorized, bool wired, bool valid)
    {
        var config = new NativeLiveConfig("native.json", new("never-start", []), "route", "Agent", 1000,
            new(true, true, true, true, true, true, true), "proof.json", "hash",
            AllowExactGroupDelivery: true, AllowNativeGroupReplies: authorized);
        var roles = new Filomena.Core.Outbound.OutboundBindings(
            [], [], new Dictionary<string, string> { ["generate"] = "texto" }, [], null)
        {
            ReplyTargetPorts = wired ? new Dictionary<string, string> { ["generate"] = "replyTo" }
                : new Dictionary<string, string>()
        };
        if (valid) config.ValidateReplyWiring(roles);
        else Assert.Throws<InvalidDataException>(() => config.ValidateReplyWiring(roles));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    public async Task Native_group_members_require_matching_host_and_hashed_group_permission(
        bool host, bool evidence, bool group, bool valid)
    {
        var dir = Directory.CreateTempSubdirectory("group-members-auth-");
        try
        {
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var capabilities = new Filomena.Core.Outbound.TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                version = "native-live-capabilities:v1", approvedRevision = "R1", observationSessionRef = "session",
                routeBindingRef = "route", boundSendContract = group ? "whatsapp-bound-group-text:v1" : "whatsapp-bound-text:v1",
                capabilities, allowNativeGroupParticipants = evidence
            }, json);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "proof.json"), bytes, TestContext.Current.CancellationToken);
            var config = new NativeLiveConfig("missing-native.json", new("never-start", []), "route", "Agent", 1000,
                capabilities, "proof.json", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                AllowExactGroupDelivery: group, AllowNativeGroupParticipants: host);
            var file = Path.Combine(dir.FullName, "live.json");
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(config, json), TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", file, "--authorize-one-response"],
                new StringReader("/quit"), output, TestContext.Current.CancellationToken));
            Assert.Contains(valid ? "invalid_host_configuration" : "invalid_live_authorization", output.ToString());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(true, true, true, "--authorize-continuous-group", true)]
    [InlineData(true, false, true, "--authorize-continuous-group", false)]
    [InlineData(false, true, true, "--authorize-continuous-group", false)]
    [InlineData(true, true, false, "--authorize-continuous-group", false)]
    [InlineData(true, true, true, "--authorize-one-response", false)]
    public async Task Continuous_group_requires_explicit_CLI_host_and_hashed_authority(
        bool host, bool evidence, bool group, string flag, bool valid)
    {
        var dir = Directory.CreateTempSubdirectory("continuous-group-auth-");
        try
        {
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var capabilities = new Filomena.Core.Outbound.TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                version = "native-live-capabilities:v1", approvedRevision = "R1", observationSessionRef = "session",
                routeBindingRef = "route", boundSendContract = group ? "whatsapp-bound-group-text:v1" : "whatsapp-bound-text:v1",
                capabilities, continuousGroup = evidence
            }, json);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "proof.json"), bytes, TestContext.Current.CancellationToken);
            var config = new NativeLiveConfig("missing-native.json", new("never-start", []), "route", "Agent", 1000,
                capabilities, "proof.json", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                AllowExactGroupDelivery: group);
            var document = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(config, json))!;
            document["continuousGroup"] = host;
            var file = Path.Combine(dir.FullName, "live.json");
            await File.WriteAllTextAsync(file, document.ToJsonString(), TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", file, flag], new StringReader("/quit"), output,
                TestContext.Current.CancellationToken));
            Assert.Contains(valid ? "invalid_host_configuration" : "invalid_live_authorization", output.ToString());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(true, true, true, false, false, false, "--authorize-continuous-dm", true)]
    [InlineData(true, false, true, false, false, false, "--authorize-continuous-dm", false)]
    [InlineData(false, true, true, false, false, false, "--authorize-continuous-dm", false)]
    [InlineData(true, true, false, false, false, false, "--authorize-continuous-dm", false)]
    [InlineData(true, true, true, true, false, false, "--authorize-continuous-dm", false)]
    [InlineData(true, true, true, false, true, false, "--authorize-continuous-dm", false)]
    [InlineData(true, true, true, false, false, true, "--authorize-continuous-dm", false)]
    [InlineData(true, true, true, false, false, false, "--authorize-continuous-group", false)]
    public async Task Continuous_DM_requires_matching_cli_host_and_hashed_authority(
        bool host, bool evidence, bool dmContract, bool group, bool groupParticipants, bool groupReplies,
        string flag, bool valid)
    {
        var dir = Directory.CreateTempSubdirectory("continuous-dm-auth-");
        try
        {
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var capabilities = new Filomena.Core.Outbound.TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                version = "native-live-capabilities:v1", approvedRevision = "R1", observationSessionRef = "session",
                routeBindingRef = "route", boundSendContract = groupReplies ? "whatsapp-bound-group-reply:v1"
                    : dmContract ? "whatsapp-bound-text:v1" : "whatsapp-bound-group-text:v1",
                capabilities, continuousDm = evidence, continuousGroup = group,
                allowNativeGroupParticipants = groupParticipants
            }, json);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "proof.json"), bytes, TestContext.Current.CancellationToken);
            var config = new NativeLiveConfig("missing-native.json", new("never-start", []), "route", "Agent", 1000,
                capabilities, "proof.json", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                AllowExactGroupDelivery: group, ContinuousDm: host, ContinuousGroup: group,
                AllowNativeGroupParticipants: groupParticipants, AllowNativeGroupReplies: groupReplies);
            var file = Path.Combine(dir.FullName, "live.json");
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(config, json), TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", file, flag], new StringReader("/quit"), output,
                TestContext.Current.CancellationToken));
            Assert.Contains(valid ? "invalid_host_configuration" : "invalid_live_authorization", output.ToString());
            Assert.DoesNotContain("run_started", output.ToString());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(true, true, "whatsapp-bound-text:v1", null, null, false)]
    [InlineData(true, true, "whatsapp-bound-member-dm-text:v1", "15550001@s.whatsapp.net", null, false)]
    [InlineData(true, true, "whatsapp-bound-member-dm-text:v1", "15550001@s.whatsapp.net", "lease-7", true)]
    [InlineData(false, false, "whatsapp-bound-text:v1", null, null, true)]
    [InlineData(false, true, "whatsapp-bound-member-dm-text:v1", "15550001@s.whatsapp.net", "lease-7", false)]
    public async Task Managed_member_DM_cannot_downgrade_or_lose_its_membership_lease(
        bool managedHost, bool managedEvidence, string contract, string? nativeConversationId,
        string? membershipLeaseRef, bool valid)
    {
        var dir = Directory.CreateTempSubdirectory("managed-member-dm-auth-");
        try
        {
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var capabilities = new Filomena.Core.Outbound.TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                version = "native-live-capabilities:v1", approvedRevision = "native-dispatch-R7", observationSessionRef = "dm-scope",
                routeBindingRef = "dm-route", boundSendContract = contract, capabilities, continuousDm = true,
                managedMemberDm = managedEvidence, nativeConversationId, membershipLeaseRef
            }, json);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "proof.json"), bytes, TestContext.Current.CancellationToken);
            var config = new NativeLiveConfig("missing-native.json", new("never-start", []), "route", "Agent", 1000,
                capabilities, "proof.json", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                ContinuousDm: true, ManagedMemberDm: managedHost);
            var file = Path.Combine(dir.FullName, "live.json");
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(config, json), TestContext.Current.CancellationToken);
            using var output = new StringWriter();

            Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", file, "--authorize-continuous-dm"],
                new StringReader("/quit"), output, TestContext.Current.CancellationToken));

            Assert.Contains(valid ? "invalid_host_configuration" : "invalid_live_authorization", output.ToString());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(true, "whatsapp-bound-text:v1", false, false)]
    [InlineData(false, "whatsapp-bound-group-text:v1", false, false)]
    [InlineData(true, "whatsapp-bound-group-text:v1", false, true)]
    [InlineData(true, "whatsapp-bound-group-reply:v1", true, true)]
    [InlineData(true, "whatsapp-bound-group-text:v1", true, false)]
    [InlineData(true, "whatsapp-bound-group-reply:v1", false, false)]
    [InlineData(false, "whatsapp-bound-group-reply:v1", true, false)]
    public async Task Group_authority_requires_matching_hashed_native_contract(bool allowGroup, string contract, bool allowReply, bool valid)
    {
        var dir = Directory.CreateTempSubdirectory("group-contract-auth-");
        try
        {
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var capabilities = new Filomena.Core.Outbound.TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                version = "native-live-capabilities:v1", approvedRevision = "R1", observationSessionRef = "session",
                routeBindingRef = "route", boundSendContract = contract, capabilities, maxInteractions = 5
            }, json);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "proof.json"), bytes, TestContext.Current.CancellationToken);
            var config = new NativeLiveConfig("missing-native.json", new("never-start", []), "route", "Agent", 1000,
                capabilities, "proof.json", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                5, AllowExactGroupDelivery: allowGroup);
            var file = Path.Combine(dir.FullName, "live.json");
            var document = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(config, json))!;
            document["allowNativeGroupReplies"] = allowReply;
            await File.WriteAllTextAsync(file, document.ToJsonString(), TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", file, "--authorize-five-interactions"],
                new StringReader("/quit"), output, TestContext.Current.CancellationToken));
            Assert.Contains(valid ? "invalid_host_configuration" : "invalid_live_authorization", output.ToString());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Receiver_revalidation_requires_matching_host_and_hashed_evidence(bool host, bool evidence)
    {
        var dir = Directory.CreateTempSubdirectory("receiver-auth-");
        try
        {
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var capabilities = new Filomena.Core.Outbound.TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                version = "native-live-capabilities:v1", approvedRevision = "R1", observationSessionRef = "session",
                routeBindingRef = "route", boundSendContract = "whatsapp-bound-text:v1", capabilities,
                maxInteractions = 5, revalidateReceiverWithinWindow = evidence
            }, json);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "evidence.json"), bytes, TestContext.Current.CancellationToken);
            var config = new NativeLiveConfig("missing-native.json", new("never-start", []), "route", "Agent", 1000,
                capabilities, "evidence.json", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), 5, host);
            var file = Path.Combine(dir.FullName, "live.json");
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(config, json), TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", file, "--authorize-five-interactions"],
                new StringReader("/poll"), output, TestContext.Current.CancellationToken));
            Assert.Contains("invalid_live_authorization", output.ToString());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(5, 5, "--authorize-one-response", false)]
    [InlineData(1, 1, "--authorize-five-interactions", false)]
    [InlineData(5, 1, "--authorize-five-interactions", false)]
    [InlineData(6, 6, "--authorize-five-interactions", false)]
    [InlineData(4, 4, "--authorize-interactions=4", true)]
    [InlineData(1, 1, "--authorize-interactions=1", true)]
    [InlineData(4, 3, "--authorize-interactions=4", false)]
    [InlineData(5, 5, "--authorize-interactions=4", false)]
    [InlineData(6, 6, "--authorize-interactions=6", false)]
    [InlineData(0, 0, "--authorize-interactions=0", false)]
    [InlineData(4, 4, "--authorize-interactions=04", false)]
    public async Task Window_budget_requires_matching_explicit_authorization_and_evidence(int budget, int proofBudget, string flag, bool valid)
    {
        var dir = Directory.CreateTempSubdirectory("bounded-live-auth-");
        try
        {
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var capabilities = new Filomena.Core.Outbound.TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                version = "native-live-capabilities:v1", approvedRevision = "R1", observationSessionRef = "session",
                routeBindingRef = "route", boundSendContract = "whatsapp-bound-text:v1", capabilities, maxInteractions = proofBudget
            }, json);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "evidence.json"), bytes, TestContext.Current.CancellationToken);
            var config = new NativeLiveConfig("missing-native.json", new("never-start", []), "route", "Agent", 1000,
                capabilities, "evidence.json", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), budget);
            var file = Path.Combine(dir.FullName, "live.json");
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(config, json), TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", file, flag], new StringReader("/poll"), output,
                TestContext.Current.CancellationToken));
            Assert.Contains(valid ? "invalid_host_configuration" : "invalid_live_authorization", output.ToString());
            Assert.DoesNotContain("run_started", output.ToString());
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Live_route_uses_trusted_logical_PN_not_the_observation_LID()
    {
        var session = new SessionKey("runtime", "character", "whatsapp", "account", "15550001@s.whatsapp.net");
        var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId),
            ["self"], ["whatsapp:15550001@s.whatsapp.net"]);
        var config = new NativeLiveConfig("native-with-lid.json", new("never-start", []), "opaque-route", "Agent", 1000,
            new(true, true, true, true, true, true, true), "evidence.json", "hash");
        var route = config.BindRoute(binding);
        Assert.Equal("15550001@s.whatsapp.net", route.Recipient);
        Assert.Equal(session, route.Session);
        Assert.Equal("account", route.AccountId);
        Assert.Equal("opaque-route", route.TransportRoute);
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{\"project\":{},\"outboundMode\":\"enabled\"}")]
    [InlineData(true, "{\"nativeConfigFile\":\"missing.json\"}")]
    public async Task Invalid_or_unauthorized_document_cannot_launch_a_sender(bool authorize, string json)
    {
        var dir = Directory.CreateTempSubdirectory("native-live-command-");
        try
        {
            var path = Path.Combine(dir.FullName, "live.json");
            await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            string[] args = authorize ? ["--native-live", path, "--authorize-one-response"] : ["--native-live", path];
            Assert.Equal(2, await NativeLiveCommand.RunAsync(args, new StringReader("/poll"), output, TestContext.Current.CancellationToken));
            Assert.Contains("invalid_live_authorization", output.ToString());
            Assert.DoesNotContain("run_started", output.ToString());
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Mixed_modes_fail_before_reading_any_document()
    {
        using var output = new StringWriter();
        Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", "missing", "--native-shadow", "missing", "--authorize-one-response"],
            new StringReader("/poll"), output, TestContext.Current.CancellationToken));
        Assert.Contains("invalid_live_authorization", output.ToString());
    }

    [Theory]
    [InlineData("not evidence")]
    [InlineData("{\"version\":\"native-live-capabilities:v2\"}")]
    public async Task Matching_file_hash_is_not_a_capability_proof(string evidence)
    {
        var dir = Directory.CreateTempSubdirectory("native-live-evidence-");
        try
        {
            var file = Path.Combine(dir.FullName, "live.json");
            var bytes = System.Text.Encoding.UTF8.GetBytes(evidence);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "evidence.json"), bytes, TestContext.Current.CancellationToken);
            var config = new NativeLiveConfig("missing-native.json", new("never-start", []), "route", "Agent", 1000,
                new(true, true, true, true, true, true, true), "evidence.json",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", file, "--authorize-one-response"],
                new StringReader("/poll"), output, TestContext.Current.CancellationToken));
            Assert.Contains("invalid_live_authorization", output.ToString());
        }
        finally { dir.Delete(true); }
    }
}
