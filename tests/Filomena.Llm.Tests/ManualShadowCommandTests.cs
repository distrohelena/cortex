using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ManualShadowCommandTests
{
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
