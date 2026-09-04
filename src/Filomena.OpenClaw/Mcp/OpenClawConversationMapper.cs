using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.Core.Identity;

namespace Filomena.OpenClaw.Mcp;

public static class OpenClawConversationMapper
{
    public static ConversationInfo? MapConversation(JsonElement value)
    {
        var sessionKey = String(value, "sessionKey");
        var channel = String(value, "channel")?.ToLowerInvariant();
        var nativeId = String(value, "to");
        if (sessionKey is null || channel is null || nativeId is null)
        {
            return null;
        }

        var accountId = String(value, "accountId") ?? "default";
        var kind = ResolveConversationKind(channel, nativeId);
        var conversationNamespace = ResolveNamespace(channel, nativeId, kind);
        if (conversationNamespace is null)
        {
            return null;
        }

        return new ConversationInfo(
            new ConversationId(
                channel,
                accountId,
                nativeId,
                conversationNamespace,
                ScalarString(value, "threadId")),
            sessionKey,
            kind,
            String(value, "displayName"),
            String(value, "label"),
            String(value, "derivedTitle"),
            String(value, "lastMessagePreview"),
            UnixMilliseconds(value, "updatedAt"));
    }

    public static InboundConversationEvent? MapInboundEvent(JsonElement value)
    {
        if (String(value, "type") != "message" || String(value, "role") != "user")
        {
            return null;
        }

        var cursor = Int64(value, "cursor");
        var sessionKey = String(value, "sessionKey");
        var providerMessageId = String(value, "messageId");
        var content = String(value, "text");
        if (cursor is null || sessionKey is null || providerMessageId is null || content is null)
        {
            return null;
        }

        var conversation = Object(value, "conversation") is { } conversationValue
            ? MapConversation(conversationValue)
            : null;
        var raw = Object(value, "raw");
        var rawMessage = raw is { } rawValue ? Object(rawValue, "message") : null;
        var rawSenderId = FirstString(
            raw.HasValue ? String(raw.Value, "senderId") : null,
            rawMessage.HasValue ? String(rawMessage.Value, "senderId") : null);
        var senderName = FirstString(
            raw.HasValue ? String(raw.Value, "senderName") : null,
            rawMessage.HasValue ? String(rawMessage.Value, "senderName") : null);
        var sender = conversation is not null && rawSenderId is not null
            ? MapActor(conversation.Id, rawSenderId, senderName)
            : null;
        var replyToId = FirstString(
            raw.HasValue ? String(raw.Value, "replyToId") : null,
            rawMessage.HasValue ? String(rawMessage.Value, "replyToId") : null,
            rawMessage.HasValue && Object(rawMessage.Value, "replyTo") is { } reply
                ? String(reply, "id")
                : null);
        var timestamp = FirstTimestamp(
            raw.HasValue ? Timestamp(raw.Value, "timestamp") : null,
            rawMessage.HasValue ? Timestamp(rawMessage.Value, "timestamp") : null);

        var structural = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["role"] = "user",
            ["sender_id_available"] = rawSenderId is not null,
            ["reply_to_id_available"] = replyToId is not null,
            ["timestamp_available"] = timestamp is not null,
            ["conversation_route_available"] = conversation is not null,
            ["sanitized_provider_payload"] = raw.HasValue ? SafeJson.Sanitize(raw.Value) : null,
        };

        return new InboundConversationEvent(
            cursor.Value,
            sessionKey,
            conversation,
            providerMessageId,
            content,
            sender,
            timestamp,
            replyToId is null ? null : new ReplyRef(replyToId),
            Int32(value, "messageSeq"),
            structural);
    }

    private static ActorRef? MapActor(
        ConversationId conversation,
        string rawNativeId,
        string? displayName)
    {
        if (conversation.Channel != "whatsapp")
        {
            return new ActorRef(
                conversation.Channel,
                conversation.AccountId,
                rawNativeId,
                $"{conversation.Channel}:{rawNativeId}",
                displayName,
                rawNativeId);
        }

        try
        {
            var nativeId = FilomenaIdentity.NormalizeWhatsAppActorNativeId(rawNativeId);
            return new ActorRef(
                conversation.Channel,
                conversation.AccountId,
                nativeId,
                $"whatsapp:{nativeId}",
                displayName,
                rawNativeId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ConversationKind ResolveConversationKind(string channel, string nativeId) =>
        channel == "whatsapp"
            ? nativeId.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase)
                ? ConversationKind.Group
                : ConversationKind.DirectMessage
            : ConversationKind.Unknown;

    private static string? ResolveNamespace(
        string channel,
        string nativeId,
        ConversationKind kind) =>
        channel == "whatsapp" && kind is not ConversationKind.Unknown
            ? FilomenaIdentity.WhatsAppConversationNamespace(nativeId)
            : null;

    private static JsonElement? Object(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Object
            ? property
            : null;

    private static string? String(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;

    private static string? ScalarString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static long? Int64(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt64(out var result)
            ? result
            : null;

    private static int? Int32(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var result)
            ? result
            : null;

    private static DateTimeOffset? UnixMilliseconds(JsonElement value, string propertyName) =>
        Int64(value, propertyName) is { } milliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    private static DateTimeOffset? Timestamp(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            return number >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(number)
                : DateTimeOffset.FromUnixTimeSeconds(number);
        }

        return property.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(property.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static string? FirstString(params string?[] values) => values.FirstOrDefault(value => value is not null);

    private static DateTimeOffset? FirstTimestamp(params DateTimeOffset?[] values) =>
        values.FirstOrDefault(value => value is not null);
}
