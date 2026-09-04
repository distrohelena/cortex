using System.Runtime.CompilerServices;
using System.Text.Json;
using Filomena.Core.Conversations;

namespace Filomena.OpenClaw.Mcp;

public sealed class OpenClawMcpTransport
{
    private const int RecoveryMessageLimit = 50;
    private readonly IMcpToolClient client;
    private readonly ObservedMessageRegistry observedMessages;

    public OpenClawMcpTransport(IMcpToolClient client)
        : this(client, new ObservedMessageRegistry())
    {
    }

    public OpenClawMcpTransport(
        IMcpToolClient client,
        ObservedMessageRegistry observedMessages)
    {
        this.client = client;
        this.observedMessages = observedMessages;
    }

    public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken) =>
        client.ListToolsAsync(cancellationToken);

    public async Task<IReadOnlyList<ConversationInfo>> ListConversationsAsync(
        string? channel,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["limit"] = 500,
            ["includeDerivedTitles"] = true,
            ["includeLastMessage"] = false,
        };
        if (!string.IsNullOrWhiteSpace(channel))
        {
            arguments["channel"] = channel;
        }

        var result = await client.CallToolAsync("conversations_list", arguments, cancellationToken);
        if (!TryArray(result, "conversations", out var conversations))
        {
            return [];
        }

        return conversations
            .EnumerateArray()
            .Select(OpenClawConversationMapper.MapConversation)
            .OfType<ConversationInfo>()
            .ToArray();
    }

    public async Task<JsonElement> ReadRecentRawAsync(
        string sessionKey,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        return await client.CallToolAsync(
            "messages_read",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["session_key"] = sessionKey,
                ["limit"] = Math.Clamp(limit, 1, 200),
            },
            cancellationToken);
    }

    public async IAsyncEnumerable<InboundConversationEvent> ListenAsync(
        TimeSpan waitTimeout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = 0L;
        var timeoutMilliseconds = checked((int)Math.Clamp(
            waitTimeout.TotalMilliseconds,
            1,
            300_000));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await client.CallToolAsync(
                "events_wait",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["after_cursor"] = cursor,
                    ["timeout_ms"] = timeoutMilliseconds,
                },
                cancellationToken);

            if (TryObject(result, "gap", out var gap) &&
                Int64(gap, "oldest_available_cursor") is { } oldestAvailableCursor)
            {
                await RecoverConversationSnapshotsAsync(cancellationToken);
                cursor = Math.Max(cursor, oldestAvailableCursor - 1);
            }

            if (!TryObject(result, "event", out var eventValue))
            {
                continue;
            }

            if (Int64(eventValue, "cursor") is { } eventCursor)
            {
                cursor = Math.Max(cursor, eventCursor);
            }

            var message = OpenClawConversationMapper.MapInboundEvent(eventValue);
            if (message is null ||
                !observedMessages.TryRemember(message.SessionKey, message.ProviderMessageId))
            {
                continue;
            }

            yield return message;
        }
    }

    private async Task RecoverConversationSnapshotsAsync(CancellationToken cancellationToken)
    {
        var conversations = await ListConversationsAsync(channel: null, cancellationToken);
        foreach (var conversation in conversations)
        {
            _ = await ReadRecentRawAsync(
                conversation.SessionKey,
                RecoveryMessageLimit,
                cancellationToken);
        }
    }

    private static bool TryArray(JsonElement value, string name, out JsonElement result)
    {
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(name, out result) &&
            result.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryObject(JsonElement value, string name, out JsonElement result)
    {
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(name, out result) &&
            result.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        result = default;
        return false;
    }

    private static long? Int64(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var property) &&
        property.TryGetInt64(out var result)
            ? result
            : null;
}
