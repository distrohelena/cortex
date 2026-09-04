using Filomena.OpenClaw.Mcp;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class OpenClawMcpClientOptionsTests
{
    [Fact]
    public void DefaultStartsOneObservationOnlyMcpStdioProcess()
    {
        var options = OpenClawMcpClientOptions.Default;

        Assert.Equal("openclaw.cmd", options.Command);
        Assert.Equal(["mcp", "serve", "--claude-channel-mode", "off"], options.Arguments);
        Assert.DoesNotContain(options.Arguments, argument => argument.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(options.Arguments, argument => argument.Contains("password", StringComparison.OrdinalIgnoreCase));
    }
}
