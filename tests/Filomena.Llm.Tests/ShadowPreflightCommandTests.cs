using System.Text.Json;
using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ShadowPreflightCommandTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shadow-preflight-" + Guid.NewGuid());
    public ShadowPreflightCommandTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Missing_config_returns_machine_readable_failure_without_starting_host()
    {
        using var output = new StringWriter();
        var code = await ShadowPreflightCommand.RunAsync(["--shadow-preflight", "--json"], output, TestContext.Current.CancellationToken);
        Assert.NotEqual(0, code);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("ready").GetBoolean());
        Assert.Contains("configuration", json.RootElement.GetProperty("criticalFailures").EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [InlineData("shadow")]
    [InlineData("enabled")]
    [InlineData("disabled")]
    public async Task Current_source_is_not_ready_and_static_never_probes_graphiti(string mode)
    {
        var path = WriteConfig(mode);
        using var output = new StringWriter();
        var code = await ShadowPreflightCommand.RunAsync(["--shadow-preflight", path, "--json"], output, TestContext.Current.CancellationToken);
        Assert.Equal(2, code);
        using var json = JsonDocument.Parse(output.ToString());
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("id").GetString()!);
        Assert.True(checks["project_pinned"].GetProperty("passed").GetBoolean());
        Assert.True(checks["enabled_impossible"].GetProperty("passed").GetBoolean());
        Assert.Equal(mode == "shadow", checks["outbound_shadow"].GetProperty("passed").GetBoolean());
        Assert.Equal("not_run", checks["graphiti_reachable"].GetProperty("phase").GetString());
        Assert.False(checks["legacy_lifecycle_isolated"].GetProperty("passed").GetBoolean());
        Assert.DoesNotContain("5511000000000", output.ToString());
        Assert.DoesNotContain("127.0.0.1", output.ToString());
    }

    [Fact]
    public async Task Config_cannot_claim_source_capabilities_or_legacy_isolation()
    {
        var path = WriteConfig("shadow");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        node["legacyInactive"] = true;
        File.WriteAllText(path, node.ToJsonString());
        using var output = new StringWriter();
        Assert.Equal(2, await ShadowPreflightCommand.RunAsync(["--shadow-preflight", path, "--json"], output, TestContext.Current.CancellationToken));
        Assert.Contains("invalid_host_configuration", output.ToString());
    }

    private string WriteConfig(string mode)
    {
        var project = Path.Combine(AppContext.BaseDirectory, "typed-flow-slice42.json");
        var config = new
        {
            projectFile = project,
            projectId = "shadow-preflight-test",
            projectVersion = 1,
            expectedProjectSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(project))),
            source = "openclaw-mcp",
            outboundMode = mode,
            journalDirectory = directory,
            session = new { runtimeId = "test", characterId = "character", channelId = "whatsapp", accountId = "default", nativeConversationId = "5511000000000@s.whatsapp.net" },
            conversationNamespace = "whatsapp-dm-5511000000000-s-whatsapp-net",
            selfActorIds = new[] { "whatsapp:5511999999999@s.whatsapp.net" },
            peerActorIds = new[] { "whatsapp:5511000000000@s.whatsapp.net" },
            outboundBindings = new { recallNodes = new[] { "recall" }, compilerNodes = new[] { "compile" }, candidatePorts = new Dictionary<string, string> { ["generate"] = "text" }, enforcementNodes = new[] { "enforce" }, wouldTransitionTo = "engaged" },
            graphitiEndpoint = "http://127.0.0.1:1/mcp",
            graphitiProbe = new { conversationNamespace = "whatsapp-dm-5511000000000-s-whatsapp-net", participantStableId = "whatsapp:5511000000000@s.whatsapp.net", localCanary = "local", foreignNamespace = "foreign", foreignCanary = "foreign-marker" }
        };
        var path = Path.Combine(directory, "host.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        return path;
    }

    [Fact]
    public async Task Transport_phase_never_runs_graphiti_or_uses_unbound_source()
    {
        using var output = new StringWriter();
        Assert.Equal(2, await ShadowPreflightCommand.RunAsync(["--shadow-preflight", WriteConfig("shadow"), "--transport-read-only", "--json"], output, TestContext.Current.CancellationToken));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("transportReady").GetBoolean());
        Assert.Null(json.RootElement.GetProperty("effectiveConfigRevision").GetString());
        var graphiti = json.RootElement.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "graphiti_reachable");
        Assert.Equal("not_run", graphiti.GetProperty("phase").GetString());
    }
    public void Dispose() => Directory.Delete(directory, true);
}
