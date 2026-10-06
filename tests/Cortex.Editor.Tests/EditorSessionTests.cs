using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cortex.Editor.Tests;

public sealed class EditorSessionTests
{
    [Fact]
    public void ConfiguredHttpsOriginSupportsTunnelWithoutTrustingForwardedHeaders()
    {
        var session = new EditorSession("https://slogan.ngrok-free.dev");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new("slogan.ngrok-free.dev");
        context.Request.Method = "POST";
        context.Request.Headers.Origin = "https://slogan.ngrok-free.dev";
        context.Request.Headers["X-Cortex-Session"] = session.Token;
        Assert.True(session.Authorized(context));
        context.Request.Headers.Origin = "https://foreign.invalid";
        Assert.False(session.Authorized(context));
        context.Request.Method = "GET";
        context.Request.Host = new("foreign.invalid");
        context.Request.Headers["X-Forwarded-Host"] = "slogan.ngrok-free.dev";
        Assert.False(session.Authorized(context));
        context.Request.Host = new("slogan.ngrok-free.dev");
        context.Request.Headers.Remove("X-Cortex-Session");
        Assert.False(session.Authorized(context));
    }

    [Theory]
    [InlineData("http://slogan.ngrok-free.dev")]
    [InlineData("https://user:password@slogan.ngrok-free.dev")]
    [InlineData("https://slogan.ngrok-free.dev/path")]
    [InlineData("https://slogan.ngrok-free.dev?query=x")]
    [InlineData("https://slogan.ngrok-free.dev/#fragment")]
    public void PublicOriginMustBeAnExactHttpsOrigin(string origin) =>
        Assert.Throws<ArgumentException>(() => new EditorSession(origin));
}
