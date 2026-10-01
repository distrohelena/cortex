using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Core.Tests.Flows;

internal static class FlowTestFixtures
{
    public static GraphDocument MinimalTextFlow() => new(
        "flow-minimal",
        1,
        GraphRole.Character,
        "filomena",
        [
            Node("manual", "manual-input", new { }),
            Node("ai", "ai", new { }),
            Node("send", "send", new { destinationId = "fake-destination" }),
        ],
        [
            new DataEdge(new PortRef("manual", "text"), [], new PortRef("ai", "prompt")),
            new DataEdge(new PortRef("ai", "text"), [], new PortRef("send", "body")),
        ],
        [
            new ControlEdge(new PortRef("manual", "success"), new PortRef("ai", "run")),
            new ControlEdge(new PortRef("ai", "success"), new PortRef("send", "run")),
        ],
        ["manual"]);

    public static GraphDocument DeclaredDmGroupInputSet() => new(
        "flow-input-set",
        1,
        GraphRole.Ingress,
        null,
        [
            Node("dm", "dm-input", new { }),
            Node("group", "group-input", new { }),
            Node("ai", "ai", new { }),
            Node("send", "send", new { destinationId = "fake-destination" }),
        ],
        [
            new DataEdge(new PortRef("dm", "text"), [], new PortRef("ai", "prompt")),
            new DataEdge(new PortRef("group", "text"), [], new PortRef("ai", "prompt")),
            new DataEdge(new PortRef("ai", "text"), [], new PortRef("send", "body")),
        ],
        [
            new ControlEdge(new PortRef("dm", "success"), new PortRef("ai", "run")),
            new ControlEdge(new PortRef("group", "success"), new PortRef("ai", "run")),
            new ControlEdge(new PortRef("ai", "success"), new PortRef("send", "run")),
        ],
        ["dm", "group"]);

    public static GraphDocument WithDuplicateNodeId()
    {
        var graph = MinimalTextFlow();
        return new GraphDocument(graph.Id, graph.FormatVersion, graph.Role, graph.CharacterId,
            [.. graph.Nodes, Node("ai", "ai", new { })], graph.DataEdges, graph.ControlEdges, graph.EntryNodeIds);
    }

    public static GraphDocument WithUnknownTargetPort() => ReplaceDataEdges(
        MinimalTextFlow(),
        edge => edge.Target.NodeId == "send"
            ? edge with { Target = new PortRef("send", "missing") }
            : edge);

    public static GraphDocument WithUnknownBlockVersion()
    {
        var graph = MinimalTextFlow();
        return ReplaceNodes(graph, node => node.Id == "ai" ? new FlowNode(node.Id, node.Kind, 99, node.Config) : node);
    }

    public static GraphDocument WithMissingRequiredDataInput() => ReplaceDataEdges(
        MinimalTextFlow(),
        edge => edge.Target.NodeId == "ai" ? null : edge);

    public static GraphDocument WithWrongFieldType()
    {
        var graph = MinimalTextFlow();
        var structuredConfig = Json("{\"outputSchema\":{\"kind\":\"object\",\"fields\":{\"payload\":{\"type\":{\"kind\":\"number\"},\"required\":true}}}}" );
        var structured = ReplaceNodes(graph, node => node.Id == "ai" ? new FlowNode(node.Id, node.Kind, node.BlockVersion, structuredConfig) : node);
        return ReplaceDataEdges(structured, edge => edge.Source.NodeId == "ai"
            ? edge with { Source = new PortRef("ai", "output"), FieldPath = ["payload"] }
            : edge);
    }

    public static GraphDocument WithDuplicateProducer() => new(
        MinimalTextFlow().Id,
        MinimalTextFlow().FormatVersion,
        MinimalTextFlow().Role,
        MinimalTextFlow().CharacterId,
        MinimalTextFlow().Nodes,
        [
            .. MinimalTextFlow().DataEdges,
            new DataEdge(new PortRef("manual", "text"), [], new PortRef("send", "body")),
        ],
        MinimalTextFlow().ControlEdges,
        MinimalTextFlow().EntryNodeIds);

    public static GraphDocument WithMissingDecisionExit()
    {
        var graph = new GraphDocument(
            "flow-condition",
            1,
            GraphRole.Character,
            "filomena",
            [
                Node("manual", "manual-input", new { }),
                Node("condition", "condition", new { field = "value", op = "equals", value = "yes" }),
                Node("send", "send", new { destinationId = "fake-destination" }),
            ],
            [new DataEdge(new PortRef("manual", "text"), [], new PortRef("condition", "value"))],
            [
                new ControlEdge(new PortRef("manual", "success"), new PortRef("condition", "run")),
                new ControlEdge(new PortRef("condition", "true"), new PortRef("send", "run")),
            ],
            ["manual"]);

        return graph;
    }

    public static GraphDocument WithUnsupportedCodeNode()
    {
        var graph = MinimalTextFlow();
        return new GraphDocument(graph.Id, graph.FormatVersion, graph.Role, graph.CharacterId,
            [.. graph.Nodes, Node("code", "code", new { source = "return 1;" })], graph.DataEdges, graph.ControlEdges, graph.EntryNodeIds);
    }

    public static GraphDocument WithUnreachableNode()
    {
        var graph = MinimalTextFlow();
        return new GraphDocument(graph.Id, graph.FormatVersion, graph.Role, graph.CharacterId,
            [.. graph.Nodes, Node("unreachable", "context", new { })], graph.DataEdges, graph.ControlEdges, graph.EntryNodeIds);
    }

    public static GraphDocument WithMultipleImplicitStarts() => new(
        "flow-multiple-starts",
        1,
        GraphRole.Character,
        "filomena",
        [Node("first", "manual-input", new { }), Node("second", "manual-input", new { })],
        [],
        [],
        []);

    public static GraphDocument WithDataCycle() => new(
        "flow-data-cycle",
        1,
        GraphRole.Character,
        "filomena",
        [Node("manual", "manual-input", new { }), Node("left", "burst", new { }), Node("right", "burst", new { })],
        [
            new DataEdge(new PortRef("manual", "text"), [], new PortRef("left", "text")),
            new DataEdge(new PortRef("left", "text"), [], new PortRef("right", "text")),
            new DataEdge(new PortRef("right", "text"), [], new PortRef("left", "text")),
        ],
        [
            new ControlEdge(new PortRef("manual", "success"), new PortRef("left", "run")),
            new ControlEdge(new PortRef("left", "success"), new PortRef("right", "run")),
        ],
        ["manual"]);

    public static GraphDocument WithControlCycle()
    {
        var graph = MinimalTextFlow();
        return new GraphDocument(graph.Id, graph.FormatVersion, graph.Role, graph.CharacterId,
            graph.Nodes, graph.DataEdges,
            [.. graph.ControlEdges, new ControlEdge(new PortRef("send", "success"), new PortRef("manual", "run"))],
            graph.EntryNodeIds);
    }

    public static GraphDocument WithBranchLocalData()
    {
        return new GraphDocument(
            "flow-branch",
            1,
            GraphRole.Character,
            "filomena",
            [
                Node("manual", "manual-input", new { }),
                Node("condition", "condition", new { field = "value", op = "equals", value = "yes" }),
                Node("context", "context", new { }),
                Node("send-true", "send", new { destinationId = "fake-destination" }),
                Node("send-false", "send", new { destinationId = "fake-destination" }),
            ],
            [
                new DataEdge(new PortRef("manual", "text"), [], new PortRef("condition", "value")),
                new DataEdge(new PortRef("context", "text"), [], new PortRef("send-false", "body")),
            ],
            [
                new ControlEdge(new PortRef("manual", "success"), new PortRef("condition", "run")),
                new ControlEdge(new PortRef("condition", "true"), new PortRef("context", "run")),
                new ControlEdge(new PortRef("context", "success"), new PortRef("send-true", "run")),
                new ControlEdge(new PortRef("condition", "false"), new PortRef("send-false", "run")),
            ],
            ["manual"]);
    }

    public static GraphDocument WithCrossedNamedJoinData() => new(
        "flow-crossed-join",
        1,
        GraphRole.Character,
        "filomena",
        [
            Node("manual", "manual-input", new { }),
            Node("parallel", "parallel", new { branches = new[] { "left", "right" } }),
            Node("left-context", "context", new { }),
            Node("right-context", "context", new { }),
            Node("join", "join", new { mode = "all", branches = new[] { "left", "right" } }),
        ],
        [new DataEdge(new PortRef("left-context", "text"), [], new PortRef("join", "right"))],
        [
            new ControlEdge(new PortRef("manual", "success"), new PortRef("parallel", "run")),
            new ControlEdge(new PortRef("parallel", "left"), new PortRef("left-context", "run")),
            new ControlEdge(new PortRef("parallel", "right"), new PortRef("right-context", "run")),
            new ControlEdge(new PortRef("left-context", "success"), new PortRef("join", "left")),
            new ControlEdge(new PortRef("right-context", "success"), new PortRef("join", "right")),
        ],
        ["manual"]);

    public static GraphDocument WithMissingGroupAlternativeData()
    {
        var graph = DeclaredDmGroupInputSet();
        return new GraphDocument(
            graph.Id,
            graph.FormatVersion,
            graph.Role,
            graph.CharacterId,
            graph.Nodes,
            graph.DataEdges.Where(edge => edge.Source.NodeId != "group"),
            graph.ControlEdges,
            graph.EntryNodeIds);
    }

    public static GraphDocument WithDuplicateNamedJoinControlProducer()
    {
        var graph = WithFirstValidEffect();
        return new GraphDocument(
            graph.Id,
            graph.FormatVersion,
            graph.Role,
            graph.CharacterId,
            graph.Nodes,
            graph.DataEdges,
            [.. graph.ControlEdges, new ControlEdge(new PortRef("parallel", "left"), new PortRef("join", "left"))],
            graph.EntryNodeIds);
    }

    public static GraphDocument WithNumericJoinMode()
    {
        var graph = WithFirstValidEffect();
        return ReplaceNodes(graph, node => node.Id == "join"
            ? new FlowNode(node.Id, node.Kind, node.BlockVersion, JsonSerializer.SerializeToElement(new { mode = 99, branches = new[] { "left", "right" } }))
            : node);
    }

    public static GraphDocument WithInvalidConditionOperator()
    {
        var graph = WithMissingDecisionExit();
        return ReplaceNodes(graph, node => node.Id == "condition"
            ? new FlowNode(node.Id, node.Kind, node.BlockVersion, JsonSerializer.SerializeToElement(new { field = "value", op = "contains", value = 1 }))
            : node);
    }

    public static GraphDocument WithAwaitMissingTimeoutExit()
    {
        var graph = new GraphDocument(
            "flow-await",
            1,
            GraphRole.Character,
            "filomena",
            [
                Node("manual", "manual-input", new { }),
                Node("await", "await", new { resumePredicate = new { eventKind = "manual", conversationId = "conversation-1" }, deadline = "2026-09-06T00:00:00Z" }),
            ],
            [],
            [new ControlEdge(new PortRef("manual", "success"), new PortRef("await", "run"))],
            ["manual"]);

        return graph;
    }

    public static GraphDocument WithBooleanAwaitPredicate()
    {
        var graph = WithAwaitMissingTimeoutExit();
        return ReplaceNodes(graph, node => node.Id == "await"
            ? new FlowNode(node.Id, node.Kind, node.BlockVersion, JsonSerializer.SerializeToElement(new { resumePredicate = true, deadline = "2026-09-06T00:00:00Z" }))
            : node);
    }

    public static GraphDocument WithUnknownAwaitPredicateField()
    {
        var graph = WithAwaitMissingTimeoutExit();
        return ReplaceNodes(graph, node => node.Id == "await"
            ? new FlowNode(node.Id, node.Kind, node.BlockVersion, JsonSerializer.SerializeToElement(new
            {
                resumePredicate = new { eventKind = "manual", conversationId = "conversation-1", unexpected = "value" },
                deadline = "2026-09-06T00:00:00Z",
            }))
            : node);
    }

    public static GraphDocument WithBranchOverflow()
    {
        var graph = new GraphDocument(
            "flow-branch-overflow",
            1,
            GraphRole.Character,
            "filomena",
            [Node("manual", "manual-input", new { }), Node("parallel", "parallel", new { branches = Enumerable.Range(0, 65).Select(index => "branch-" + index).ToArray() })],
            [],
            [new ControlEdge(new PortRef("manual", "success"), new PortRef("parallel", "run"))],
            ["manual"]);

        return graph;
    }

    public static GraphDocument WithFirstValidEffect()
    {
        var graph = new GraphDocument(
            "flow-first-valid",
            1,
            GraphRole.Character,
            "filomena",
            [
                Node("manual", "manual-input", new { }),
                Node("parallel", "parallel", new { branches = new[] { "left", "right" } }),
                Node("send", "send", new { destinationId = "fake-destination" }),
                Node("join", "join", new { mode = "first-valid", branches = new[] { "left", "right" } }),
            ],
            [],
            [
                new ControlEdge(new PortRef("manual", "success"), new PortRef("parallel", "run")),
                new ControlEdge(new PortRef("parallel", "left"), new PortRef("send", "run")),
                new ControlEdge(new PortRef("parallel", "right"), new PortRef("join", "right")),
                new ControlEdge(new PortRef("send", "success"), new PortRef("join", "left")),
            ],
            ["manual"]);

        return graph;
    }

    private static FlowNode Node(string id, string kind, object config) =>
        new(id, kind, 1, JsonSerializer.SerializeToElement(config));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static GraphDocument ReplaceNodes(GraphDocument graph, Func<FlowNode, FlowNode> replace) =>
        new(graph.Id, graph.FormatVersion, graph.Role, graph.CharacterId, graph.Nodes.Select(replace), graph.DataEdges, graph.ControlEdges, graph.EntryNodeIds);

    private static GraphDocument ReplaceDataEdges(GraphDocument graph, Func<DataEdge, DataEdge?> replace) =>
        new(graph.Id, graph.FormatVersion, graph.Role, graph.CharacterId, graph.Nodes, graph.DataEdges.Select(replace).OfType<DataEdge>(), graph.ControlEdges, graph.EntryNodeIds);
}
