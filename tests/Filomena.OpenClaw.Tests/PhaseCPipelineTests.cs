using System.Net;
using System.Text;
using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.Llm.Agent;
using Filomena.Llm.Arbitration;
using Filomena.Llm.LmStudio;
using Filomena.OpenClaw.Bridge;
using Filomena.OpenClaw.Mcp;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class PhaseCPipelineTests
{
    [Theory]
    [InlineData(true, 2, 1)]
    [InlineData(false, 1, 0)]
    public async Task BridgeThroughArbiterAndGenerationToMcpUsesOnlyOneSend(
        bool respond, int expectedModelCalls, int expectedSends)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        var receiver = new OpenClawInboundBridgeReceiver();
        using var payload = JsonDocument.Parse("""
            {
              "schemaVersion":1,
              "sessionKey":"opaque-session-route",
              "providerMessageId":"inbound-1",
              "timestamp":"2026-09-04T18:00:00Z",
              "content":"Filomena como vai?",
              "conversation":{
                "channel":"whatsapp","accountId":"default",
                "nativeId":"5511981042361-1589823314@g.us","kind":"group",
                "namespace":"whatsapp-group-5511981042361-1589823314-g-us"
              },
              "sender":{
                "channel":"whatsapp","accountId":"default",
                "nativeId":"5511944558218@s.whatsapp.net",
                "stableId":"whatsapp:5511944558218@s.whatsapp.net","displayName":"Helena"
              }
            }
            """);
        Assert.Equal(InboundBridgeAcceptance.Accepted, await receiver.AcceptAsync(payload.RootElement, cancellation.Token));
        Assert.Equal(InboundBridgeAcceptance.Duplicate, await receiver.AcceptAsync(payload.RootElement, cancellation.Token));
        var inbound = await receiver.Events.ReadAsync(cancellation.Token);
        Assert.False(receiver.Events.TryRead(out _));
        var httpHandler = new ModelHandler(respond);
        using var http = new HttpClient(httpHandler);
        var model = new LmStudioClient(http, new LmStudioOptions());
        await using var mcp = new ToolClient();
        var agent = new FilomenaAgent(model, new OpenClawMcpTransport(mcp), new FilomenaAgentOptions
        {
            AllowedConversationNamespaces = [inbound.Conversation!.Id.Namespace],
        });
        await using var manager = new ConversationTurnManager(new GemmaTurnArbiter(model),
            new ConversationTurnOptions(), new ImmediateClock(), agent);
        await manager.EnqueueAsync(inbound, cancellation.Token);
        var result = await manager.Results.ReadAsync(cancellation.Token);
        Assert.Null(result.ExecutionError);
        Assert.Equal(expectedModelCalls, httpHandler.Calls);
        Assert.Equal(expectedSends, mcp.Calls);
        if (respond)
        {
            Assert.Equal("provider-outbound-1", result.SentTurn?.ProviderMessageId);
            Assert.Equal("opaque-session-route", mcp.Arguments!["session_key"]);
            Assert.Equal("Filomena: Estou bem!", mcp.Arguments["text"]);
        }
        else Assert.Null(result.SentTurn);
    }

    private sealed class ImmediateClock : IConversationClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-09-04T18:00:01Z");
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ModelHandler(bool respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("google/gemma-4-e4b", payload.RootElement.GetProperty("model").GetString());
            var content = Calls == 1
                ? JsonSerializer.Serialize(new
                {
                    decision = respond ? "respond" : "ignore",
                    anchorRef = (string?)null,
                    targetRef = (string?)null,
                    reason = respond ? "useful_contribution" : "no_useful_contribution"
                })
                : "Estou bem!";
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ToolClient : IMcpToolClient
    {
        public int Calls { get; private set; }
        public IReadOnlyDictionary<string, object?>? Arguments { get; private set; }
        public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDescriptor>>([]);
        public Task<JsonElement> CallToolAsync(string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("messages_send", name);
            Arguments = arguments;
            return Task.FromResult(JsonSerializer.SerializeToElement(new { result = new { messageId = "provider-outbound-1" } }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
