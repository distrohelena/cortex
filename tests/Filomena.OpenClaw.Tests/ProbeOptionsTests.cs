using Filomena.App;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class ProbeOptionsTests
{
    [Fact]
    public void DefaultsAreObservationOnlyAndTargetTheKnownConversations()
    {
        var options = new ProbeOptions();

        Assert.Equal("milestone-a", options.Mode);
        Assert.Equal(300, options.WaitTimeoutSeconds);
        Assert.Equal(2, options.ReconnectDelaySeconds);
        Assert.Equal(
            [
                "5511981042361-1589823314@g.us",
                "120363318973251043@g.us",
                "5511944558218@s.whatsapp.net",
            ],
            options.TargetConversationIds);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("list")]
    [InlineData("wait")]
    [InlineData("milestone-a")]
    [InlineData("phase-b")]
    public void AcceptsKnownRuntimeModes(string mode)
    {
        Assert.True(ProbeOptions.IsSupportedMode(mode));
    }

    [Fact]
    public void RejectsUnknownRuntimeMode()
    {
        Assert.False(ProbeOptions.IsSupportedMode("send-everything"));
    }
}
