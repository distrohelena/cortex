using System.Diagnostics;
using System.Text.Json;
using Filomena.Llm.LmStudio;

namespace Filomena.App.Playground;

// Local text-only drafts, deliberately not the larger portable-release executor.
public sealed record LocalFlowNode(string Id, string Kind, string? Prompt, double X, double Y, string? Provider = null, string? Model = null);
public sealed record LocalFlowEdge(string Source, string Target);
public sealed record LocalFlowDraft(int Version, LocalFlowNode[] Nodes, LocalFlowEdge[] Edges);
public sealed record LocalFlowRequest(LocalFlowDraft? Graph, string? Message, string? EntryNodeId = null);
public sealed record LocalFlowStep(string NodeId, string Kind, string Input, string? Output, long ElapsedMs, string? Error);
public sealed record LocalFlowResult(bool Success, string? Output, IReadOnlyList<LocalFlowStep> Steps);

public static class LocalFlowRunner
{
    private static bool IsEntry(LocalFlowNode node) => node.Kind is "input" or "dm" or "group";

    public static IReadOnlyList<LocalFlowNode> Validate(LocalFlowDraft? graph, string? entryNodeId = null)
    {
        if (graph is null || graph.Version != 1 || graph.Nodes is null || graph.Edges is null
            || graph.Nodes.Length is < 3 or > 20 || graph.Edges.Length > 20)
            throw new ArgumentException("Use entradas de DM/grupo, prompts e uma saída (até 20 blocos).");
        var nodes = new Dictionary<string, LocalFlowNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 80
                || node.Kind is not ("input" or "dm" or "group" or "prompt" or "output") || !nodes.TryAdd(node.Id, node)
                || !double.IsFinite(node.X) || !double.IsFinite(node.Y)
                || node.Prompt?.Length > 16000
                || node.Provider?.Length > 120 || node.Model?.Length > 256
                || (node.Kind == "prompt" && string.IsNullOrWhiteSpace(node.Prompt)))
                throw new ArgumentException("Bloco inválido: confira tipo, identificador único e prompt preenchido (até 16.000 caracteres).");
        }
        var entries = nodes.Values.Where(IsEntry).ToArray();
        if (entries.Length == 0 || nodes.Values.Count(n => n.Kind == "output") != 1)
            throw new ArgumentException("O flow precisa ter entradas de evento e uma saída.");
        if (entryNodeId is not null && !entries.Any(n => n.Id == entryNodeId))
            throw new ArgumentException("Selecione uma entrada de evento válida.");
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (edge is null || edge.Source is null || edge.Target is null
                || !nodes.TryGetValue(edge.Source, out var source) || !nodes.TryGetValue(edge.Target, out var target)
                || source.Kind == "output" || IsEntry(target) || edge.Source == edge.Target
                || !next.TryAdd(edge.Source, edge.Target))
                throw new ArgumentException("Conexão inválida. Cada saída aceita um fio; várias entradas podem compartilhar o mesmo caminho.");
        }
        var ordered = new List<LocalFlowNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var path = new List<LocalFlowNode>();
            var pathIds = new HashSet<string>(StringComparer.Ordinal);
            var current = entry;
            while (pathIds.Add(current.Id))
            {
                path.Add(current);
                visited.Add(current.Id);
                if (!next.TryGetValue(current.Id, out var target)) break;
                current = nodes[target];
            }
            if (path[^1].Kind != "output")
                throw new ArgumentException("Conecte cada entrada até a saída, sem ciclos.");
            if (entryNodeId is null || entry.Id == entryNodeId) ordered.AddRange(path);
        }
        if (visited.Count != nodes.Count)
            throw new ArgumentException("Conecte todos os blocos da entrada até a saída, sem ciclos ou blocos soltos.");
        return ordered;
    }

    public static async Task<LocalFlowResult> RunAsync(LocalFlowDraft? graph, string? message,
        IResponseChatClient client, CancellationToken cancellationToken, string? entryNodeId = null)
    {
        var ordered = Validate(graph, entryNodeId);
        if (entryNodeId is null && ordered.Count(IsEntry) != 1)
            throw new ArgumentException("Escolha qual entrada de evento simular.");
        foreach (var node in ordered.Where(n => n.Kind == "prompt"))
        {
            if (!string.IsNullOrWhiteSpace(node.Provider) && !string.Equals(node.Provider.Trim(), "lmstudio", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Node {node.Id}: provider '{node.Provider}' pode ser salvo no desenho, mas o executor local suporta apenas 'lmstudio' por enquanto.");
            if (!string.IsNullOrWhiteSpace(node.Model) && client is not IModelResponseChatClient)
                throw new ArgumentException($"Node {node.Id}: este cliente não suporta selecionar modelo por node.");
        }
        if (string.IsNullOrWhiteSpace(message) || message.Length > 16000)
            throw new ArgumentException("Preencha a mensagem de teste (até 16.000 caracteres).");
        var value = message;
        var steps = new List<LocalFlowStep>();
        foreach (var node in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = value;
            var watch = Stopwatch.StartNew();
            try
            {
                if (node.Kind == "prompt")
                {
                    if (input.Length > 16000) throw new InvalidDataException();
                    value = string.IsNullOrWhiteSpace(node.Model)
                        ? await client.CompleteResponseAsync(node.Prompt!, input, cancellationToken)
                        : await ((IModelResponseChatClient)client).CompleteResponseAsync(node.Prompt!, input, node.Model.Trim(), cancellationToken);
                    if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException();
                }
                steps.Add(new(node.Id, node.Kind, input, value, watch.ElapsedMilliseconds, null));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                steps.Add(new(node.Id, node.Kind, input, null, watch.ElapsedMilliseconds, "LM Studio excedeu o tempo configurado."));
                return new(false, null, steps);
            }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException or JsonException)
            {
                steps.Add(new(node.Id, node.Kind, input, null, watch.ElapsedMilliseconds,
                    "Falha no modelo ou resposta inválida/entrada muito longa. Confira o LM Studio. Os próximos blocos não foram executados."));
                return new(false, null, steps);
            }
        }
        return new(true, value, steps);
    }
}
