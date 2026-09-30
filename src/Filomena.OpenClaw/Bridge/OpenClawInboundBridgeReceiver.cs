using System.Text.Json;
using System.Threading.Channels;
using Filomena.Core.Conversations;
using Filomena.Core.Identity;
using Filomena.OpenClaw.Mcp;

namespace Filomena.OpenClaw.Bridge;

public enum InboundBridgeAcceptance
{
    Accepted,
    Duplicate,
    Invalid,
}

public sealed class OpenClawInboundBridgeReceiver
{
    private readonly Func<InboundConversationEvent, InboundConversationEvent>? prepareForQueue;

    public OpenClawInboundBridgeReceiver(Func<InboundConversationEvent, InboundConversationEvent>? prepareForQueue = null)
        => this.prepareForQueue = prepareForQueue;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ObservedMessageRegistry observedMessages = new();
    private readonly Channel<InboundConversationEvent> events = Channel.CreateUnbounded<InboundConversationEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    public ChannelReader<InboundConversationEvent> Events => events.Reader;

    public async ValueTask<InboundBridgeAcceptance> AcceptAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InboundConversationEvent? message;
        try
        {
            var envelope = payload.Deserialize<InboundBridgeEnvelope>(JsonOptions);
            message = envelope is null ? null : Map(envelope);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException)
        {
            return InboundBridgeAcceptance.Invalid;
        }

        if (message?.Conversation is not { } conversation)
        {
            return InboundBridgeAcceptance.Invalid;
        }

        var dedupScope = JsonSerializer.Serialize(new[] { conversation.Id.Channel, conversation.Id.AccountId, conversation.Id.NativeId });
        if (!observedMessages.TryRemember(dedupScope, message.ProviderMessageId))
        {
            return InboundBridgeAcceptance.Duplicate;
        }

        // Once remembered, ownership is transferred to the process inbox even
        // if the HTTP caller disconnects before receiving its acknowledgement.
        await events.Writer.WriteAsync(prepareForQueue?.Invoke(message) ?? message, CancellationToken.None);
        return InboundBridgeAcceptance.Accepted;
    }

    private static InboundConversationEvent? Map(InboundBridgeEnvelope envelope)
    {
        if (envelope.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(envelope.SessionKey) ||
            string.IsNullOrWhiteSpace(envelope.ProviderMessageId) ||
            string.IsNullOrWhiteSpace(envelope.Content) ||
            envelope.Timestamp is null ||
            envelope.Conversation is null)
        {
            return null;
        }

        var conversation = MapConversation(envelope.Conversation, envelope.SessionKey);
        if (conversation is null)
        {
            return null;
        }

        var sender = envelope.Sender is null
            ? null
            : MapActor(envelope.Sender, conversation.Id);
        if (envelope.Sender is not null && sender is null)
        {
            return null;
        }

        var structural = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source"] = "openclaw_hook_bridge",
            ["sender_id_available"] = sender is not null,
            ["reply_to_id_available"] = !string.IsNullOrWhiteSpace(envelope.ReplyTo?.ProviderMessageId),
            ["timestamp_available"] = true,
            ["conversation_route_available"] = true,
            ["reply_to_full_id"] = envelope.ReplyTo?.ProviderMessageIdFull,
            ["reply_to_sender_native_id"] = envelope.ReplyTo?.SenderNativeId,
            ["reply_to_content"] = envelope.ReplyTo?.Content,
            ["reply_to_is_quote"] = envelope.ReplyTo?.IsQuote,
        };

        return new InboundConversationEvent(
            0,
            envelope.SessionKey,
            conversation,
            envelope.ProviderMessageId,
            envelope.Content,
            sender,
            envelope.Timestamp.Value,
            string.IsNullOrWhiteSpace(envelope.ReplyTo?.ProviderMessageId)
                ? null
                : new ReplyRef(envelope.ReplyTo.ProviderMessageId),
            null,
            structural);
    }

    private static ConversationInfo? MapConversation(
        InboundBridgeConversation value,
        string sessionKey)
    {
        if (string.IsNullOrWhiteSpace(value.Channel) ||
            string.IsNullOrWhiteSpace(value.AccountId) ||
            string.IsNullOrWhiteSpace(value.NativeId) ||
            string.IsNullOrWhiteSpace(value.Namespace))
        {
            return null;
        }

        var channel = value.Channel.Trim().ToLowerInvariant();
        var kind = value.Kind switch
        {
            "dm" => ConversationKind.DirectMessage,
            "group" => ConversationKind.Group,
            _ => ConversationKind.Unknown,
        };
        if (kind == ConversationKind.Unknown || !HasCanonicalConversationIdentity(channel, value, kind))
        {
            return null;
        }

        return new ConversationInfo(
            new ConversationId(
                channel,
                value.AccountId,
                value.NativeId,
                value.Namespace,
                value.ThreadId),
            sessionKey,
            kind,
            null,
            null,
            null,
            null,
            null);
    }

    private static bool HasCanonicalConversationIdentity(
        string channel,
        InboundBridgeConversation value,
        ConversationKind kind)
    {
        if (channel == "whatsapp")
        {
            var expectedKind = value.NativeId.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase)
                ? ConversationKind.Group
                : ConversationKind.DirectMessage;
            return kind == expectedKind &&
                string.Equals(
                    FilomenaIdentity.WhatsAppConversationNamespace(value.NativeId),
                    value.Namespace,
                    StringComparison.Ordinal);
        }

        if (channel == "telegram")
        {
            var negative = value.NativeId.StartsWith("-", StringComparison.Ordinal);
            var digits = negative ? value.NativeId[1..] : value.NativeId;
            if (digits.Length == 0 || digits.Any(character => !char.IsAsciiDigit(character)))
            {
                return false;
            }

            var expectedKind = negative ? ConversationKind.Group : ConversationKind.DirectMessage;
            var sign = negative ? $"neg-{digits}" : $"pos-{digits}";
            return kind == expectedKind &&
                string.Equals($"telegram-{value.Kind}-{sign}", value.Namespace, StringComparison.Ordinal);
        }

        return false;
    }

    private static ActorRef? MapActor(InboundBridgeActor value, ConversationId conversation)
    {
        if (!string.Equals(value.Channel, conversation.Channel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(value.AccountId, conversation.AccountId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(value.NativeId) ||
            string.IsNullOrWhiteSpace(value.StableId))
        {
            return null;
        }

        if (conversation.Channel == "whatsapp")
        {
            var nativeId = FilomenaIdentity.NormalizeWhatsAppActorNativeId(value.NativeId);
            var stableId = FilomenaIdentity.WhatsAppActorStableId(nativeId);
            if (!string.Equals(nativeId, value.NativeId, StringComparison.Ordinal) ||
                !string.Equals(stableId, value.StableId, StringComparison.Ordinal))
            {
                return null;
            }
        }
        else if (conversation.Channel == "telegram")
        {
            if (value.NativeId.Length == 0 ||
                value.NativeId.Any(character => !char.IsAsciiDigit(character)) ||
                !string.Equals($"telegram:user:{value.NativeId}", value.StableId, StringComparison.Ordinal))
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        return new ActorRef(
            conversation.Channel,
            conversation.AccountId,
            value.NativeId,
            value.StableId,
            value.DisplayName,
            value.RawNativeId);
    }

    private sealed record InboundBridgeEnvelope(
        int SchemaVersion,
        string SessionKey,
        string ProviderMessageId,
        string Content,
        DateTimeOffset? Timestamp,
        InboundBridgeConversation? Conversation,
        InboundBridgeActor? Sender,
        InboundBridgeReply? ReplyTo);

    private sealed record InboundBridgeConversation(
        string Channel,
        string AccountId,
        string NativeId,
        string Namespace,
        string Kind,
        string? ThreadId);

    private sealed record InboundBridgeActor(
        string Channel,
        string AccountId,
        string NativeId,
        string StableId,
        string? DisplayName,
        string? RawNativeId);

    private sealed record InboundBridgeReply(
        string ProviderMessageId,
        string? ProviderMessageIdFull,
        string? SenderNativeId,
        string? Content,
        bool? IsQuote);
}
