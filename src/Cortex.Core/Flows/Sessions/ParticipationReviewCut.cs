using Cortex.Core.Boundaries;

namespace Cortex.Core.Flows.Sessions;

// A conservative projection of the actual compiled graph. No separate decision engine.
internal sealed record ParticipationReviewCut(TypedFlow Flow, IReadOnlyDictionary<PortRef, ParticipationReviewDecision> Decisions)
{
    internal static ParticipationReviewCut Create(TypedFlow source, ParticipationReviewDefinition definition)
    {
        var prompts = definition.PromptNodeIds.ToHashSet(StringComparer.Ordinal);
        var boundaries = definition.Boundaries.ToArray();
        if (prompts.Count == 0 || prompts.Count != definition.PromptNodeIds.Count || boundaries.Length == 0)
            throw new InvalidDataException("Empty or duplicate review scope.");
        var decisions = new Dictionary<PortRef, ParticipationReviewDecision>();
        foreach (var b in boundaries)
        {
            if (!Enum.IsDefined(b.Decision) || !source.Nodes.TryGetValue(b.NodeId, out var node)
                || node.Source.Kind is not ("switch" or "branch") || !node.Exits.Contains(b.Exit)
                || !decisions.TryAdd(new(b.NodeId, b.Exit), b.Decision))
                throw new InvalidDataException("Invalid review boundary.");
        }
        foreach (var id in prompts)
            if (!source.Nodes.TryGetValue(id, out var node) || node.Source.Kind != "prompt"
                || TypedFlowCompiler.IsTextOutput(node.Source.Config)
                || TypedFlowRunner.ResponseSchema(node) is null || BoundaryEnforcementProfile.Enabled(node.Source.Config))
                throw new InvalidDataException("Review allows only explicitly selected structured decision prompts.");

        var included = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(source.EntryId);
        while (pending.TryDequeue(out var id))
        {
            if (!included.Add(id)) continue;
            var node = source.Nodes[id];
            if (node.Source.Kind is not ("dm" or "group" or "accumulate" or "history" or "prompt" or "branch" or "switch")
                || node.Source.Kind == "prompt" && !prompts.Contains(id))
                throw new InvalidDataException("Effect outside participation review.");
            foreach (var exit in node.Exits)
            {
                var port = new PortRef(id, exit);
                if (decisions.ContainsKey(port)) continue;
                var next = source.Graph.ControlEdges.SingleOrDefault(e => e.Source == port);
                if (next is null) throw new InvalidDataException("Review path has no decision boundary.");
                pending.Enqueue(next.Target.NodeId);
            }
        }
        if (!prompts.IsSubsetOf(included) || decisions.Keys.Any(p => !included.Contains(p.NodeId)))
            throw new InvalidDataException("Review scope is not reachable from this entry.");
        pending = new Queue<string>(included);
        while (pending.TryDequeue(out var id))
            foreach (var edge in source.Graph.DataEdges.Where(e => e.Target.NodeId == id))
            {
                if (included.Contains(edge.Source.NodeId)) continue;
                var dependency = source.Nodes[edge.Source.NodeId];
                if (dependency.Source.Kind is not ("property" or "count"))
                    throw new InvalidDataException("Review has an out-of-scope data dependency.");
                included.Add(edge.Source.NodeId);
                pending.Enqueue(edge.Source.NodeId);
            }
        var nodes = source.Nodes.Where(p => included.Contains(p.Key)).ToDictionary();
        var graph = new GraphDocument(source.Graph.Id, source.Graph.FormatVersion, source.Graph.Role,
            source.Graph.CharacterId, nodes.Values.Select(n => n.Source),
            source.Graph.DataEdges.Where(e => included.Contains(e.Source.NodeId) && included.Contains(e.Target.NodeId)),
            source.Graph.ControlEdges.Where(e => included.Contains(e.Source.NodeId) && included.Contains(e.Target.NodeId)
                && !decisions.ContainsKey(e.Source)), [source.EntryId]);
        return new(new(graph, source.EntryId, nodes), decisions);
    }
}
