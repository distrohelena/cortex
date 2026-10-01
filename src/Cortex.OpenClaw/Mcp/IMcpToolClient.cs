using System.Text.Json;

namespace Cortex.OpenClaw.Mcp;

public sealed record McpToolDescriptor(
    string Name,
    string? Description,
    JsonElement InputSchema);

public sealed record McpToolCall(
    string Name,
    IReadOnlyDictionary<string, object?> Arguments);

public interface IMcpToolClient : IAsyncDisposable
{
    Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken);

    Task<JsonElement> CallToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}
