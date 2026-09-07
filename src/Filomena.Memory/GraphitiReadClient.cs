using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Filomena.Memory;

public interface IGraphitiReadClient
{
    Task<JsonElement> GetEntityNeighborhoodAsync(string conversationNamespace, Guid participant, int limit, CancellationToken token);
    Task<JsonElement> SearchFactsAsync(string conversationNamespace, string query, int limit, CancellationToken token);
}

// A single lazy MCP connection. Only these two read operations are exposed.
public sealed class GraphitiReadClient(Uri endpoint, TimeSpan timeout) : IGraphitiReadClient, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private McpClient? client;
    private bool disposed;

    public Task<JsonElement> GetEntityNeighborhoodAsync(string conversationNamespace, Guid participant, int limit, CancellationToken token) =>
        CallAsync("get_entity_neighborhood", new Dictionary<string, object?>
        {
            ["group_id"] = conversationNamespace,
            ["entity_uuid"] = participant.ToString(),
            ["limit"] = limit,
            ["active_only"] = true
        }, token);

    public Task<JsonElement> SearchFactsAsync(string conversationNamespace, string query, int limit, CancellationToken token) =>
        CallAsync("search_memory_facts", new Dictionary<string, object?>
        {
            ["group_ids"] = new[] { conversationNamespace },
            ["query"] = query,
            ["max_facts"] = limit,
            ["active_only"] = true
        }, token);

    private async Task<JsonElement> CallAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (client is null)
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = endpoint,
                    Name = "Graphiti read-only",
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = timeout
                });
                client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token).ConfigureAwait(false);
            }
            var response = await client.CallToolAsync(tool, arguments, cancellationToken: deadline.Token).ConfigureAwait(false);
            if (response.IsError == true) throw new InvalidDataException("Graphiti read operation failed.");
            if (response.StructuredContent is { } structured) return structured.Clone();
            var text = response.Content.OfType<TextContentBlock>().ToArray();
            if (text.Length != 1 || text[0].Text.Length > 2_000_000) throw new InvalidDataException("Invalid Graphiti response envelope.");
            using var document = JsonDocument.Parse(text[0].Text);
            return document.RootElement.Clone();
        }
        catch
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                client = null;
            }
            throw;
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            disposed = true;
            if (client is not null) await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
        finally { gate.Release(); }
    }
}
