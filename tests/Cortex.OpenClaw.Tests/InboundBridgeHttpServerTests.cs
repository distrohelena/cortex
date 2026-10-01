using System.Net;
using System.Net.Sockets;
using System.Text;
using Filomena.App;
using Cortex.OpenClaw.Bridge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cortex.OpenClaw.Tests;

public sealed class InboundBridgeHttpServerTests
{
    [Fact]
    public async Task AcceptsAnInboundEventOverLoopbackHttp()
    {
        var port = ReservePort();
        var options = Options.Create(new InboundBridgeOptions
        {
            ListenUrl = new Uri($"http://127.0.0.1:{port}/"),
        });
        var receiver = new OpenClawInboundBridgeReceiver();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var server = new InboundBridgeHttpServer(
            options,
            receiver,
            loggerFactory.CreateLogger<InboundBridgeHttpServer>());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.StartAsync(cancellation.Token);

        try
        {
            Assert.Equal("ready", server.StartupPhase);
            using var client = new HttpClient { BaseAddress = options.Value.ListenUrl };
            using var content = new StringContent(Payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(
                "/v1/openclaw/events/inbound",
                content,
                cancellation.Token);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var message = await receiver.Events.ReadAsync(cancellation.Token);
            Assert.Equal("3EB0HTTP", message.ProviderMessageId);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
            server.Dispose();
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private const string Payload = """
        {
          "schemaVersion": 1,
          "sessionKey": "agent:main:whatsapp:default:direct:+5511944558218",
          "providerMessageId": "3EB0HTTP",
          "content": "bridge test",
          "timestamp": "2026-09-05T00:17:53Z",
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
            "stableId": "whatsapp:5511944558218@s.whatsapp.net"
          }
        }
        """;
}
