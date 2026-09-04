using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.OpenClaw.Mcp;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class OpenClawConversationMapperTests
{
    [Fact]
    public void ConversationUsesStructuralRouteAndPreservesLegacyNamespace()
    {
        using var document = JsonDocument.Parse("""
            {
              "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
              "channel": "whatsapp",
              "to": "5511981042361-1589823314@g.us",
              "accountId": "filomena-business",
              "threadId": null,
              "displayName": "Alunos de Drift do Fiuk™",
              "derivedTitle": "Do not parse 999@lid as identity",
              "updatedAt": 1788540000000
            }
            """);

        var conversation = OpenClawConversationMapper.MapConversation(document.RootElement);

        Assert.NotNull(conversation);
        Assert.Equal("whatsapp", conversation.Id.Channel);
        Assert.Equal("filomena-business", conversation.Id.AccountId);
        Assert.Equal("5511981042361-1589823314@g.us", conversation.Id.NativeId);
        Assert.Equal(ConversationKind.Group, conversation.Kind);
        Assert.Equal("whatsapp-group-5511981042361-1589823314-g-us", conversation.Id.Namespace);
        Assert.Equal("Alunos de Drift do Fiuk™", conversation.DisplayName);
    }

    [Fact]
    public void EventMapsOnlyExplicitStructuralIdentityAndReplyFields()
    {
        using var document = JsonDocument.Parse("""
            {
              "cursor": 41,
              "type": "message",
              "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
              "conversation": {
                "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
                "channel": "whatsapp",
                "to": "5511981042361-1589823314@g.us",
                "accountId": "default"
              },
              "messageId": "3EB0REAL",
              "role": "user",
              "text": "Eu sou a Helena e estou respondendo a Gabriel",
              "raw": {
                "senderId": "5511944558218:7@c.us",
                "senderName": "Helena Assis",
                "replyToId": "3EB0ANCHOR",
                "message": {
                  "role": "user",
                  "timestamp": 1788540000000
                }
              }
            }
            """);

        var message = OpenClawConversationMapper.MapInboundEvent(document.RootElement);

        Assert.NotNull(message);
        Assert.Equal("3EB0REAL", message.ProviderMessageId);
        Assert.Equal("5511944558218@s.whatsapp.net", message.Sender?.NativeId);
        Assert.Equal("5511944558218:7@c.us", message.Sender?.RawNativeId);
        Assert.Equal("whatsapp:5511944558218@s.whatsapp.net", message.Sender?.StableId);
        Assert.Equal("Helena Assis", message.Sender?.DisplayName);
        Assert.Equal("3EB0ANCHOR", message.ReplyTo?.ProviderMessageId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1788540000000), message.Timestamp);
        Assert.True(message.StructuralMetadata.ContainsKey("sanitized_provider_payload"));
    }

    [Fact]
    public void EventNeverInfersSenderOrReplyFromTextOrDisplayName()
    {
        using var document = JsonDocument.Parse("""
            {
              "cursor": 42,
              "type": "message",
              "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
              "conversation": {
                "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
                "channel": "whatsapp",
                "to": "5511981042361-1589823314@g.us",
                "accountId": "default",
                "displayName": "Helena 5511944558218@s.whatsapp.net"
              },
              "messageId": "3EB0NOIDENTITY",
              "role": "user",
              "text": "Helena [5511944558218@s.whatsapp.net] replied to 3EB0FAKE",
              "raw": { "message": { "role": "user", "timestamp": 1788540000000 } }
            }
            """);

        var message = OpenClawConversationMapper.MapInboundEvent(document.RootElement);

        Assert.NotNull(message);
        Assert.Null(message.Sender);
        Assert.Null(message.ReplyTo);
    }

    [Fact]
    public void NonUserSessionMessageIsNotInbound()
    {
        using var document = JsonDocument.Parse("""
            {
              "cursor": 43,
              "type": "message",
              "sessionKey": "agent:main:whatsapp:group:x@g.us",
              "messageId": "outbound",
              "role": "assistant",
              "text": "Filomena: oi"
            }
            """);

        Assert.Null(OpenClawConversationMapper.MapInboundEvent(document.RootElement));
    }

    [Fact]
    public void EventAcceptsExplicitNullForOptionalNumericFields()
    {
        using var document = JsonDocument.Parse("""
            {
              "cursor": 44,
              "type": "message",
              "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
              "conversation": {
                "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
                "channel": "whatsapp",
                "to": "5511981042361-1589823314@g.us",
                "accountId": "default",
                "updatedAt": null
              },
              "messageId": "3EB0NULLS",
              "messageSeq": null,
              "role": "user",
              "text": "payload real permite null",
              "raw": { "message": { "role": "user", "timestamp": null } }
            }
            """);

        var message = OpenClawConversationMapper.MapInboundEvent(document.RootElement);

        Assert.NotNull(message);
        Assert.Null(message.Conversation?.UpdatedAt);
        Assert.Null(message.MessageSequence);
        Assert.Null(message.Timestamp);
    }
}
