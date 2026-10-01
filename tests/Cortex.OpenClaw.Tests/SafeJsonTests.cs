using System.Text.Json;
using Cortex.OpenClaw.Mcp;
using Xunit;

namespace Cortex.OpenClaw.Tests;

public sealed class SafeJsonTests
{
    [Fact]
    public void RedactsCredentialShapedPropertiesAtAnyDepth()
    {
        using var document = JsonDocument.Parse("""
            {
              "messageId": "safe-id",
              "token": "top-secret",
              "nested": {
                "Authorization": "Bearer secret",
                "password": "secret",
                "senderId": "5511944558218@s.whatsapp.net"
              }
            }
            """);

        var safe = SafeJson.Sanitize(document.RootElement);
        var json = JsonSerializer.Serialize(safe);

        Assert.Contains("safe-id", json, StringComparison.Ordinal);
        Assert.Contains("5511944558218@s.whatsapp.net", json, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"secret\"", json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundsLongStringsAndCollections()
    {
        var payload = new
        {
            content = new string('x', 1_000),
            items = Enumerable.Range(0, 24).ToArray(),
        };
        var element = JsonSerializer.SerializeToElement(payload);

        var safe = SafeJson.Sanitize(element, maxStringLength: 64, maxArrayItems: 4);
        var json = JsonSerializer.Serialize(safe);

        Assert.Contains("[truncated]", json, StringComparison.Ordinal);
        Assert.Contains("[20 more items]", json, StringComparison.Ordinal);
        Assert.True(json.Length < 300);
    }
}
