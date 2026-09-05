using System.Text.Json;
using Filomena.OpenClaw.Bridge;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class OpenClawInboundBridgeReceiverTests
{
    [Fact]
    public async Task AcceptsCanonicalInboundHookEvent()
    {
        var receiver = new OpenClawInboundBridgeReceiver();
        using var payload = JsonDocument.Parse(ValidPayload);

        var result = await receiver.AcceptAsync(payload.RootElement, CancellationToken.None);
        var message = await receiver.Events.ReadAsync(CancellationToken.None);

        Assert.Equal(InboundBridgeAcceptance.Accepted, result);
        Assert.Equal("3EB0C49C242B01294DB555", message.ProviderMessageId);
        Assert.Equal("Oiii flooo", message.Content);
        Assert.Equal("agent:main:whatsapp:default:direct:+5511944558218", message.SessionKey);
        Assert.Equal("whatsapp-dm-5511944558218-s-whatsapp-net", message.Conversation?.Id.Namespace);
        Assert.Equal("whatsapp:5511944558218@s.whatsapp.net", message.Sender?.StableId);
        Assert.Equal("5511944558218:7@c.us", message.Sender?.RawNativeId);
        Assert.Equal("3EB0ANCHOR", message.ReplyTo?.ProviderMessageId);
        Assert.Equal(DateTimeOffset.Parse("2026-09-05T00:17:53Z"), message.Timestamp);
        Assert.Equal("openclaw_hook_bridge", message.StructuralMetadata["source"]);
    }

    [Fact]
    public async Task RedeliveryIsAcknowledgedWithoutEnqueuingTwice()
    {
        var receiver = new OpenClawInboundBridgeReceiver();
        using var payload = JsonDocument.Parse(ValidPayload);

        var first = await receiver.AcceptAsync(payload.RootElement, CancellationToken.None);
        var second = await receiver.AcceptAsync(payload.RootElement, CancellationToken.None);

        Assert.Equal(InboundBridgeAcceptance.Accepted, first);
        Assert.Equal(InboundBridgeAcceptance.Duplicate, second);
        Assert.True(receiver.Events.TryRead(out _));
        Assert.False(receiver.Events.TryRead(out _));
    }

    [Fact]
    public async Task RejectsNamespaceThatDoesNotMatchStructuralWhatsAppConversation()
    {
        using var payload = JsonDocument.Parse(
            ValidPayload.Replace(
                "whatsapp-dm-5511944558218-s-whatsapp-net",
                "whatsapp-dm-someone-else",
                StringComparison.Ordinal));
        var receiver = new OpenClawInboundBridgeReceiver();

        var result = await receiver.AcceptAsync(payload.RootElement, CancellationToken.None);

        Assert.Equal(InboundBridgeAcceptance.Invalid, result);
        Assert.False(receiver.Events.TryRead(out _));
    }

    [Fact]
    public async Task InvalidActorIdentityFailsClosedInsteadOfThrowing()
    {
        using var payload = JsonDocument.Parse(
            ValidPayload.Replace(
                "5511944558218@s.whatsapp.net\",\n    \"stableId\": \"whatsapp:5511944558218@s.whatsapp.net",
                "not-a-jid\",\n    \"stableId\": \"whatsapp:not-a-jid",
                StringComparison.Ordinal));
        var receiver = new OpenClawInboundBridgeReceiver();

        var result = await receiver.AcceptAsync(payload.RootElement, CancellationToken.None);

        Assert.Equal(InboundBridgeAcceptance.Invalid, result);
        Assert.False(receiver.Events.TryRead(out _));
    }

    [Fact]
    public async Task MissingTimestampFailsClosed()
    {
        using var payload = JsonDocument.Parse(
            ValidPayload.Replace(
                "\"timestamp\": \"2026-09-05T00:17:53Z\",",
                string.Empty,
                StringComparison.Ordinal));
        var receiver = new OpenClawInboundBridgeReceiver();

        var result = await receiver.AcceptAsync(payload.RootElement, CancellationToken.None);

        Assert.Equal(InboundBridgeAcceptance.Invalid, result);
        Assert.False(receiver.Events.TryRead(out _));
    }

    private const string ValidPayload = """
        {
          "schemaVersion": 1,
          "sessionKey": "agent:main:whatsapp:default:direct:+5511944558218",
          "providerMessageId": "3EB0C49C242B01294DB555",
          "content": "Oiii flooo",
          "timestamp": "2026-09-05T00:17:53Z",
          "timestampSource": "provider",
          "conversation": {
            "channel": "whatsapp",
            "accountId": "default",
            "nativeId": "5511944558218@s.whatsapp.net",
            "namespace": "whatsapp-dm-5511944558218-s-whatsapp-net",
            "kind": "dm"
          },
          "sender": {
            "channel": "whatsapp",
            "accountId": "default",
            "nativeId": "5511944558218@s.whatsapp.net",
            "stableId": "whatsapp:5511944558218@s.whatsapp.net",
            "displayName": "Helena Assis",
            "rawNativeId": "5511944558218:7@c.us"
          },
          "replyTo": { "providerMessageId": "3EB0ANCHOR" },
          "provenance": { "source": "openclaw_message_received" }
        }
        """;
}
