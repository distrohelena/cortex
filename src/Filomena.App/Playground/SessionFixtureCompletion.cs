using System.Collections.Concurrent;
using Filomena.Core.Flows;

namespace Filomena.App.Playground;

// Only caller-supplied fake responses. This type cannot access a model or transport.
internal sealed class SessionFixtureCompletion : IFlowCompletion
{
    private readonly IReadOnlyDictionary<string, ConcurrentQueue<string>> responses;

    public SessionFixtureCompletion(IReadOnlyDictionary<string, string[]>? configured)
    {
        if (configured is { Count: > 64 }) throw new InvalidDataException("Até 64 filas fake são permitidas.");
        var queues = new Dictionary<string, ConcurrentQueue<string>>(StringComparer.Ordinal);
        foreach (var (nodeId, items) in configured ?? new Dictionary<string, string[]>())
        {
            if (string.IsNullOrWhiteSpace(nodeId) || nodeId.Length > 80 || items is null || items.Length > 256 ||
                items.Any(item => item is null || item.Length > 65536))
                throw new InvalidDataException("Configuração de respostas fake inválida.");
            queues.Add(nodeId, new(items));
        }
        responses = queues;
    }

    public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!responses.TryGetValue(request.NodeId, out var queue) || !queue.TryDequeue(out var value))
            throw new InvalidDataException("Fila de respostas fake esgotada ou não configurada.");
        return Task.FromResult(value);
    }
}
