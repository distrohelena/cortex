using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.OpenClaw.Mcp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filomena.App;

public sealed class OpenClawProbeWorker(
    IOptions<ProbeOptions> configuredOptions,
    ILoggerFactory loggerFactory,
    ILogger<OpenClawProbeWorker> logger,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var options = configuredOptions.Value;
            var observedMessages = new ObservedMessageRegistry();
            var connectedAtLeastOnce = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var clientOptions = new OpenClawMcpClientOptions(
                        options.OpenClawCommand,
                        options.EffectiveOpenClawArguments);
                    await using var client = await OpenClawMcpClient.ConnectAsync(
                        clientOptions,
                        loggerFactory,
                        stoppingToken);
                    connectedAtLeastOnce = true;
                    var transport = new OpenClawMcpTransport(client, observedMessages);
                    await RunConnectedProbeAsync(transport, options, stoppingToken);
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (connectedAtLeastOnce)
                {
                    logger.LogWarning(
                        exception,
                        "OpenClaw MCP connection was lost; reconnecting in {Seconds} seconds.",
                        options.ReconnectDelaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(options.ReconnectDelaySeconds), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Milestone-A observation probe cancelled.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Milestone-A observation probe failed.");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    private async Task RunConnectedProbeAsync(
        OpenClawMcpTransport transport,
        ProbeOptions options,
        CancellationToken stoppingToken)
    {
        if (options.Mode is "inspect" or "milestone-a")
        {
            await PrintToolsAsync(transport, stoppingToken);
        }

        if (options.Mode is "list" or "wait" or "milestone-a")
        {
            _ = await PrintConversationsAsync(transport, options, stoppingToken);
        }

        if (options.Mode is not ("wait" or "milestone-a"))
        {
            return;
        }

        logger.LogInformation(
            "Waiting up to {Seconds} seconds for one inbound OpenClaw MCP event. No response will be sent.",
            options.WaitTimeoutSeconds);
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        waitCancellation.CancelAfter(TimeSpan.FromSeconds(options.WaitTimeoutSeconds));
        try
        {
            await PrintOneInboundAsync(transport, waitCancellation.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("The observation window ended before an inbound event arrived.");
        }
    }

    private static async Task PrintToolsAsync(
        OpenClawMcpTransport transport,
        CancellationToken cancellationToken)
    {
        var tools = await transport.ListToolsAsync(cancellationToken);
        await PrintJsonAsync(new
        {
            kind = "openclaw_mcp_tools",
            tools = tools.Select(tool => new
            {
                tool.Name,
                tool.Description,
                inputSchema = SafeJson.Sanitize(tool.InputSchema),
            }),
        });
    }

    private static async Task<IReadOnlyList<ConversationInfo>> PrintConversationsAsync(
        OpenClawMcpTransport transport,
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        var conversations = await transport.ListConversationsAsync("whatsapp", cancellationToken);
        var targets = new HashSet<string>(options.TargetConversationIds, StringComparer.OrdinalIgnoreCase);
        var selected = conversations.Where(conversation => targets.Contains(conversation.Id.NativeId)).ToArray();
        await PrintJsonAsync(new
        {
            kind = "target_conversations",
            discovered = selected.Length,
            requested = targets.Count,
            conversations = selected.Select(ToSafeConversation),
            missingNativeIds = targets.Except(selected.Select(item => item.Id.NativeId), StringComparer.OrdinalIgnoreCase),
        });
        return selected;
    }

    private static async Task PrintOneInboundAsync(
        OpenClawMcpTransport transport,
        CancellationToken cancellationToken)
    {
        await foreach (var message in transport.ListenAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            await PrintJsonAsync(new
            {
                kind = "normalized_inbound_event",
                message.Cursor,
                message.SessionKey,
                conversation = message.Conversation is null ? null : ToSafeConversation(message.Conversation),
                message.ProviderMessageId,
                message.Content,
                message.Timestamp,
                message.MessageSequence,
                sender = message.Sender,
                replyToProviderMessageId = message.ReplyTo?.ProviderMessageId,
                message.StructuralMetadata,
            });
            return;
        }
    }

    private static object ToSafeConversation(ConversationInfo conversation) => new
    {
        conversation.SessionKey,
        conversation.Id.Channel,
        conversation.Id.AccountId,
        conversation.Id.NativeId,
        conversation.Id.Namespace,
        conversation.Id.ThreadId,
        kind = conversation.Kind.ToString(),
        conversation.DisplayName,
        conversation.Label,
        conversation.DerivedTitle,
        conversation.UpdatedAt,
    };

    private static async Task PrintJsonAsync(object value)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value, OutputJson));
    }
}
