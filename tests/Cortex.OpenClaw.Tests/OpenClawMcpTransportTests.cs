using System.Runtime.CompilerServices;
using System.Text.Json;
using Cortex.OpenClaw.Mcp;
using Xunit;

namespace Cortex.OpenClaw.Tests;

public sealed class OpenClawMcpTransportTests
{
    [Fact]
    public async Task PreservesObservedReceiptMetadataWithoutInferringMissingRouteFields()
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("messages_send", """
            {"result":{"messageId":"provider-1","channel":"whatsapp","accountId":"account-a",
              "toJid":"12345@lid","runId":"gateway-run-1"}}
            """);
        var receipt = await new OpenClawMcpTransport(client).SendTextAsync(
            "opaque-route", "exact candidate", TestContext.Current.CancellationToken);
        var value = JsonSerializer.SerializeToElement(receipt);
        Assert.True(value.TryGetProperty("Channel", out var channel));
        Assert.Equal("whatsapp", channel.GetString());
        Assert.Equal("account-a", value.GetProperty("AccountId").GetString());
        Assert.Equal("12345@lid", value.GetProperty("Recipient").GetString());
        Assert.Equal("gateway-run-1", value.GetProperty("GatewayRunId").GetString());
        Assert.Equal("provider-1", receipt.ProviderMessageId);
        Assert.Single(client.Calls);
    }

    [Theory]
    [InlineData("\"channel\":42")]
    [InlineData("\"accountId\":{}")]
    [InlineData("\"toJid\":[]")]
    [InlineData("\"runId\":false")]
    [InlineData("\"channel\":\" \"")]
    public async Task MalformedReceiptMetadataIsUncertainAndNeverRetried(string field)
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("messages_send", "{\"result\":{\"messageId\":\"provider-1\"," + field + "}}");
        await Assert.ThrowsAsync<InvalidDataException>(() => new OpenClawMcpTransport(client)
            .SendTextAsync("opaque-route", "exact candidate", TestContext.Current.CancellationToken));
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task IdOnlyReceiptDoesNotInventAccountOrRecipientFromOpaqueRoute()
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("messages_send", """{"result":{"messageId":"provider-1"}}""");
        var receipt = await new OpenClawMcpTransport(client).SendTextAsync(
            "account-and-peer-looking-route", "exact candidate", TestContext.Current.CancellationToken);
        var value = JsonSerializer.SerializeToElement(receipt);
        Assert.True(value.TryGetProperty("AccountId", out var account));
        Assert.Equal(JsonValueKind.Null, account.ValueKind);
        Assert.Equal(JsonValueKind.Null, value.GetProperty("Recipient").ValueKind);
        Assert.Equal(JsonValueKind.Null, value.GetProperty("Channel").ValueKind);
        Assert.Equal(JsonValueKind.Null, value.GetProperty("GatewayRunId").ValueKind);
    }

    [Fact]
    public async Task SendsStandaloneThroughExactSessionRouteAndReturnsProviderReceipt()
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("messages_send", """{"result":{"messageId":"sent-1","channel":"whatsapp","runId":"gateway-id"}}""");
        var transport = new OpenClawMcpTransport(client);
        var receipt = await transport.SendTextAsync("opaque-route", "Filomena: oi", TestContext.Current.CancellationToken);
        Assert.Equal("sent-1", receipt.ProviderMessageId);
        var call = Assert.Single(client.Calls);
        Assert.Equal("messages_send", call.Name);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal("opaque-route", call.Arguments["session_key"]);
        Assert.Equal("Filomena: oi", call.Arguments["text"]);
    }

    [Theory]
    [InlineData("{\"result\":null}")]
    [InlineData("{\"result\":{}}")]
    [InlineData("{\"result\":{\"messageId\":\"\"}}")]
    public async Task DoesNotClaimSuccessOrRetryWithoutProviderReceipt(string response)
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("messages_send", response);
        var transport = new OpenClawMcpTransport(client);
        await Assert.ThrowsAsync<InvalidDataException>(() => transport.SendTextAsync(
            "route", "oi", TestContext.Current.CancellationToken));
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task DescribesRouteFromConversationGetWithoutInferringMissingFields()
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("conversation_get", """
            {"conversation":{"sessionKey":"opaque-route","channel":"whatsapp","to":"5511944558218@s.whatsapp.net","accountId":"default","displayName":"Helena"}}
            """);
        client.Enqueue("conversation_get", """
            {"conversation":{"sessionKey":"opaque-route","channel":"whatsapp","to":"5511944558218@s.whatsapp.net"}}
            """);
        Cortex.Core.Outbound.IOutboundTransport transport = new OpenClawMcpTransport(client);

        var described = await transport.DescribeRouteAsync("opaque-route", TestContext.Current.CancellationToken);
        Assert.Equal(("whatsapp", "default", "5511944558218@s.whatsapp.net"), (described.Channel, described.AccountId, described.Recipient));
        var call = Assert.Single(client.Calls);
        Assert.Equal("conversation_get", call.Name);
        Assert.Equal("opaque-route", Assert.Single(call.Arguments).Value);

        var partial = await transport.DescribeRouteAsync("opaque-route", TestContext.Current.CancellationToken);
        Assert.Null(partial.AccountId);
        Assert.Equal("5511944558218@s.whatsapp.net", partial.Recipient);
    }

    [Theory]
    [InlineData("""{"conversation":null}""")]
    [InlineData("""{}""")]
    [InlineData("""{"conversation":{"sessionKey":"opaque-route","channel":42,"to":"x"}}""")]
    public async Task UnresolvedOrMalformedRouteDescriptionFailsClosed(string response)
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("conversation_get", response);
        var transport = new OpenClawMcpTransport(client);
        await Assert.ThrowsAsync<InvalidDataException>(() => transport.DescribeRouteAsync("opaque-route", TestContext.Current.CancellationToken));
        Assert.Equal("conversation_get", Assert.Single(client.Calls).Name);
    }

    [Fact]
    public async Task ListsAndMapsChannelBackedConversations()
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("conversations_list", """
            { "conversations": [
              {
                "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
                "channel": "whatsapp",
                "to": "5511981042361-1589823314@g.us",
                "accountId": "default",
                "displayName": "Alunos de Drift do Fiuk™"
              },
              {
                "sessionKey": "agent:main:whatsapp:direct:5511944558218@s.whatsapp.net",
                "channel": "whatsapp",
                "to": "5511944558218@s.whatsapp.net",
                "accountId": "default",
                "displayName": "Helena"
              }
            ] }
            """);
        var transport = new OpenClawMcpTransport(client);

        var conversations = await transport.ListConversationsAsync("whatsapp", CancellationToken.None);

        Assert.Equal(2, conversations.Count);
        Assert.Equal("whatsapp-group-5511981042361-1589823314-g-us", conversations[0].Id.Namespace);
        Assert.Equal("whatsapp-dm-5511944558218-s-whatsapp-net", conversations[1].Id.Namespace);
        Assert.Equal("whatsapp", client.Calls.Single().Arguments["channel"]);
    }

    [Fact]
    public async Task ListenerAdvancesCursorAndSuppressesRedeliveryOfOneProviderMessage()
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("events_wait", WaitResult(Event(1, "message-a", "first")));
        client.Enqueue("events_wait", WaitResult(Event(2, "message-a", "first redelivery")));
        client.Enqueue("events_wait", WaitResult(Event(3, "message-b", "second")));
        var transport = new OpenClawMcpTransport(client);
        var received = new List<string>();

        await foreach (var message in transport.ListenAsync(TimeSpan.FromSeconds(30), CancellationToken.None))
        {
            received.Add(message.ProviderMessageId);
            if (received.Count == 2)
            {
                break;
            }
        }

        Assert.Equal(["message-a", "message-b"], received);
        var cursors = client.Calls
            .Where(call => call.Name == "events_wait")
            .Select(call => Convert.ToInt64(call.Arguments["after_cursor"]))
            .ToArray();
        Assert.Equal([0L, 1L, 2L], cursors);
    }

    [Fact]
    public async Task GapTriggersReadOnlyConversationRecoveryBeforeListeningContinues()
    {
        await using var client = new FakeMcpToolClient();
        client.Enqueue("events_wait", """
            {
              "event": null,
              "gap": { "requested_after_cursor": 0, "oldest_available_cursor": 5 }
            }
            """);
        client.Enqueue("conversations_list", """
            { "conversations": [{
              "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
              "channel": "whatsapp",
              "to": "5511981042361-1589823314@g.us",
              "accountId": "default"
            }] }
            """);
        client.Enqueue("messages_read", """{ "messages": [{ "role": "user", "content": "recovery snapshot" }] }""");
        client.Enqueue("events_wait", WaitResult(Event(5, "message-after-gap", "after gap")));
        var transport = new OpenClawMcpTransport(client);

        await foreach (var message in transport.ListenAsync(TimeSpan.FromSeconds(30), CancellationToken.None))
        {
            Assert.Equal("message-after-gap", message.ProviderMessageId);
            break;
        }

        Assert.Contains(client.Calls, call => call.Name == "messages_read");
        var waits = client.Calls.Where(call => call.Name == "events_wait").ToArray();
        Assert.Equal(0L, Convert.ToInt64(waits[0].Arguments["after_cursor"]));
        Assert.Equal(4L, Convert.ToInt64(waits[1].Arguments["after_cursor"]));
    }

    [Fact]
    public async Task ListenerHonorsCancellationDuringLongPoll()
    {
        await using var client = new FakeMcpToolClient { BlockWhenEmpty = true };
        var transport = new OpenClawMcpTransport(client);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = transport
            .ListenAsync(TimeSpan.FromMinutes(5), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task SharedRegistrySuppressesRedeliveryAcrossTransportReconnect()
    {
        var registry = new ObservedMessageRegistry();
        await using var firstClient = new FakeMcpToolClient();
        firstClient.Enqueue("events_wait", WaitResult(Event(1, "message-a", "first connection")));
        var firstTransport = new OpenClawMcpTransport(firstClient, registry);
        await foreach (var message in firstTransport.ListenAsync(TimeSpan.FromSeconds(30), CancellationToken.None))
        {
            Assert.Equal("message-a", message.ProviderMessageId);
            break;
        }

        await using var secondClient = new FakeMcpToolClient();
        secondClient.Enqueue("events_wait", WaitResult(Event(1, "message-a", "redelivered after reconnect")));
        secondClient.Enqueue("events_wait", WaitResult(Event(2, "message-b", "new after reconnect")));
        var secondTransport = new OpenClawMcpTransport(secondClient, registry);

        await foreach (var message in secondTransport.ListenAsync(TimeSpan.FromSeconds(30), CancellationToken.None))
        {
            Assert.Equal("message-b", message.ProviderMessageId);
            break;
        }
    }

    private static string WaitResult(string eventJson) => $$"""
        { "event": {{eventJson}} }
        """;

    private static string Event(long cursor, string messageId, string text) => $$"""
        {
          "cursor": {{cursor}},
          "type": "message",
          "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
          "conversation": {
            "sessionKey": "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
            "channel": "whatsapp",
            "to": "5511981042361-1589823314@g.us",
            "accountId": "default"
          },
          "messageId": "{{messageId}}",
          "role": "user",
          "text": "{{text}}",
          "raw": { "message": { "role": "user", "timestamp": 1788540000000 } }
        }
        """;

    private sealed class FakeMcpToolClient : IMcpToolClient
    {
        private readonly Dictionary<string, Queue<JsonElement>> responses = new(StringComparer.Ordinal);

        public List<McpToolCall> Calls { get; } = [];

        public bool BlockWhenEmpty { get; init; }

        public void Enqueue(string toolName, string json)
        {
            if (!responses.TryGetValue(toolName, out var queue))
            {
                queue = new Queue<JsonElement>();
                responses.Add(toolName, queue);
            }

            using var document = JsonDocument.Parse(json);
            queue.Enqueue(document.RootElement.Clone());
        }

        public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDescriptor>>([]);

        public async Task<JsonElement> CallToolAsync(
            string name,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add(new McpToolCall(name, arguments));
            if (responses.TryGetValue(name, out var queue) && queue.TryDequeue(out var result))
            {
                return result;
            }

            if (BlockWhenEmpty)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            throw new InvalidOperationException($"No fake response for {name}.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
