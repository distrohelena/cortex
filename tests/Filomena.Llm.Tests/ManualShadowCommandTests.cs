using Filomena.App;
using Cortex.Core.Flows;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using System.Reflection;
using System.Security.Cryptography;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ManualShadowCommandTests
{
    [Fact]
    public void Live_role_gate_accepts_exact_GPT6_Luna_medium_and_rejects_other_remote_models()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var bytes = File.ReadAllBytes(Path.Combine(root.FullName, "docs/manual-live-project-v3-gpt6-luna.json"));
        var roles = new OutboundBindings(["dm-response-boundaries"], ["dm-compiled-boundaries"],
            new Dictionary<string, string> { ["dm-generation"] = "texto", ["dm-regeneration"] = "texto" },
            ["dm-validator-1", "dm-validator-2"], "participation-engaged-v1");
        var method = typeof(ManualShadowCommand).GetMethod("ValidateRoles", BindingFlags.NonPublic | BindingFlags.Static)!;
        PinnedShadowProject Pin(byte[] content) => PinnedShadowProject.FromBytes(content, "filomena-v3-live-sent", 3,
            Convert.ToHexString(SHA256.HashData(content)));
        Assert.Null(method.Invoke(null, [Pin(bytes), roles]));
        var other = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("gpt-6-luna", "gpt-6-astra"));
        var rejected = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [Pin(other), roles]));
        Assert.IsType<InvalidDataException>(rejected.InnerException);
    }
    [Fact]
    public async Task Missing_host_configuration_fails_without_legacy_or_live_startup()
    {
        using var output = new StringWriter();
        Assert.Equal(2, await ManualShadowCommand.RunAsync(["--manual-shadow"], new StringReader("hello"), output, TestContext.Current.CancellationToken));
        Assert.Contains("invalid_host_configuration", output.ToString());
        Assert.DoesNotContain("WOULD_SEND", output.ToString());
    }

    [Fact]
    public async Task Project_shaped_authority_is_rejected_before_remote_access()
    {
        var directory = Directory.CreateTempSubdirectory("manual-command-");
        try
        {
            var path = Path.Combine(directory.FullName, "host.json");
            await File.WriteAllTextAsync(path, """{"project":{},"outboundMode":"enabled"}""", TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await ManualShadowCommand.RunAsync(["--manual-shadow", path], new StringReader("hello"), output, TestContext.Current.CancellationToken));
            Assert.Contains("invalid_host_configuration", output.ToString());
        }
        finally { directory.Delete(true); }
    }
}
