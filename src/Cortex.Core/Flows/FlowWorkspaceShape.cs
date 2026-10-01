namespace Cortex.Core.Flows;

// Direct callers can supply null collection elements despite nullable annotations.
// Validate before traversing the object graph; never turn malformed input into a release.
internal static class FlowWorkspaceShape
{
    internal static IReadOnlyList<FlowIssue> Validate(
        FlowWorkspaceDefinition workspace,
        IReadOnlyList<EnvironmentBindings> environments)
    {
        var issues = new List<FlowIssue>();
        bool Present(object? value, string path)
        {
            if (value is not null) return true;
            issues.Add(new FlowIssue("compiler.shape_invalid", path, "A required value is null."));
            return false;
        }

        void Items<T>(IReadOnlyList<T>? values, string path, Action<T, string>? inspect = null)
        {
            if (!Present(values, path)) return;
            for (var i = 0; i < values!.Count; i++)
            {
                var itemPath = $"{path}/{i}";
                if (Present(values[i], itemPath)) inspect?.Invoke(values[i], itemPath);
            }
        }

        Items(workspace.Characters, "/characters");
        Items(workspace.ModelProfiles, "/modelProfiles");
        Items(workspace.Graphs, "/graphs", (graph, path) =>
        {
            Items(graph.Nodes, $"{path}/nodes");
            Items(graph.DataEdges, $"{path}/dataEdges", (edge, edgePath) =>
            {
                Present(edge.Source, $"{edgePath}/source");
                Present(edge.Target, $"{edgePath}/target");
                Items(edge.FieldPath, $"{edgePath}/fieldPath");
            });
            Items(graph.ControlEdges, $"{path}/controlEdges", (edge, edgePath) =>
            {
                Present(edge.Source, $"{edgePath}/source");
                Present(edge.Target, $"{edgePath}/target");
            });
        });
        Items(environments, "/environments", (environment, path) =>
        {
            Items(environment.RouteOwners, $"{path}/routeOwners");
            Items(environment.Grants, $"{path}/grants");
            Items(environment.RouteDuplicationGrants, $"{path}/routeDuplicationGrants");
            Items(environment.Destinations, $"{path}/destinations");
            Items(environment.SpaceBindings, $"{path}/spaceBindings", (space, spacePath) =>
                Items(space.ExplicitConversationIds, $"{spacePath}/explicitConversationIds"));
        });
        return issues;
    }
}
