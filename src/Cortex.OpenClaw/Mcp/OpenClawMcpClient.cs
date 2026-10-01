using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Cortex.OpenClaw.Mcp;

public sealed record OpenClawMcpClientOptions(
    string Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null)
{
    public static OpenClawMcpClientOptions Default { get; } = new(
        "openclaw.cmd",
        ["mcp", "serve", "--claude-channel-mode", "off"]);
}

public sealed class OpenClawMcpClient : IMcpToolClient
{
    private readonly McpClient client;

    private OpenClawMcpClient(McpClient client)
    {
        this.client = client;
    }

    public static async Task<OpenClawMcpClient> ConnectAsync(
        OpenClawMcpClientOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "OpenClaw channel MCP",
                Command = options.Command,
                Arguments = options.Arguments.ToArray(),
                WorkingDirectory = options.WorkingDirectory,
                InheritEnvironmentVariables = false,
                EnvironmentVariables = environment,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            },
            loggerFactory);
        var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: loggerFactory,
            cancellationToken: cancellationToken);
        return new OpenClawMcpClient(client);
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return tools
            .Select(tool => new McpToolDescriptor(
                tool.Name,
                tool.Description,
                tool.ProtocolTool.InputSchema.Clone()))
            .ToArray();
    }

    public async Task<JsonElement> CallToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(
            name,
            arguments,
            cancellationToken: cancellationToken);
        if (result.IsError == true)
        {
            throw new InvalidOperationException($"OpenClaw MCP tool '{name}' returned an error.");
        }

        return result.StructuredContent?.Clone()
            ?? throw new InvalidOperationException($"OpenClaw MCP tool '{name}' returned no structured content.");
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
