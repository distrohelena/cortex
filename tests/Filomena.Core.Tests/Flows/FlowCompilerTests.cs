using System.Text.Json;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class FlowCompilerTests
{
    [Theory]
    [InlineData("graph", "/graphs/0")]
    [InlineData("node", "/graphs/0/nodes/0")]
    [InlineData("character", "/characters/0")]
    [InlineData("profile", "/modelProfiles/0")]
    [InlineData("environment", "/environments/0")]
    [InlineData("space", "/environments/0/spaceBindings/0")]
    [InlineData("destination", "/environments/0/destinations/0")]
    [InlineData("dataEdge", "/graphs/0/dataEdges/0")]
    [InlineData("controlEdge", "/graphs/0/controlEdges/0")]
    [InlineData("source", "/graphs/0/dataEdges/0/source")]
    [InlineData("fieldPath", "/graphs/0/dataEdges/0/fieldPath")]
    [InlineData("field", "/graphs/0/dataEdges/0/fieldPath/0")]
    [InlineData("conversation", "/environments/0/spaceBindings/0/explicitConversationIds/0")]
    public void MalformedDirectObjectsReturnSafeIssues(string malformed, string expectedPath)
    {
        var workspace = CompilerFixtures.ValidWorkspace();
        var registry = CompilerFixtures.CompleteRegistry().ToArray();
        var graph = workspace.Graphs[0];
        var edge = graph.DataEdges[0];
        switch (malformed)
        {
            case "graph": ((GraphDocument[])workspace.Graphs)[0] = null!; break;
            case "node": ((FlowNode[])graph.Nodes)[0] = null!; break;
            case "character": ((CharacterDefinition[])workspace.Characters)[0] = null!; break;
            case "profile": ((ModelProfileDefinition[])workspace.ModelProfiles)[0] = null!; break;
            case "environment": registry[0] = null!; break;
            case "space": ((SpaceBinding[])registry[0].SpaceBindings)[0] = null!; break;
            case "destination": ((DestinationBinding[])registry[0].Destinations)[0] = null!; break;
            case "dataEdge": ((DataEdge[])graph.DataEdges)[0] = null!; break;
            case "controlEdge": ((ControlEdge[])graph.ControlEdges)[0] = null!; break;
            case "source": ((DataEdge[])graph.DataEdges)[0] = edge with { Source = null! }; break;
            case "fieldPath": ((DataEdge[])graph.DataEdges)[0] = edge with { FieldPath = null! }; break;
            case "field": ((DataEdge[])graph.DataEdges)[0] = edge with { FieldPath = new string[] { null! } }; break;
            case "conversation": ((string[])registry[0].SpaceBindings[0].ExplicitConversationIds)[0] = null!; break;
        }

        var result = FlowCompiler.Compile(workspace, registry, FlowEnvironment.Testnet, "release-malformed");

        Assert.Null(result.Release);
        Assert.Contains(result.Issues, issue => issue.Code == "compiler.shape_invalid" && issue.Path == expectedPath);
    }

    [Fact]
    public void OversizedImportIsRejectedBeforeAllocatingItsUtf8Payload()
    {
        var registry = CompilerFixtures.CompleteRegistry();
        _ = FlowCompiler.Import("{}", registry, FlowEnvironment.Testnet);
        var oversized = new string('x', FlowLimits.MaxInputUtf8Bytes * 8);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = FlowCompiler.Import(oversized, registry, FlowEnvironment.Testnet);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Null(result.Release);
        Assert.Contains(result.Issues, issue => issue.Code == "artifact.input_too_large");
        Assert.True(allocated < FlowLimits.MaxInputUtf8Bytes, $"Rejection allocated {allocated} bytes.");
    }

    [Fact]
    public void ImportRejectsInvalidUtf16WithSafeEncodingIssue()
    {
        var result = FlowCompiler.Import("\ud800", CompilerFixtures.CompleteRegistry(), FlowEnvironment.Testnet);

        Assert.Null(result.Release);
        Assert.Contains(result.Issues, issue => issue.Code == "artifact.encoding_invalid");
    }

    [Fact]
    public void CompiledNestedSchemaRemainsImmutableThroughEveryCollectionBoundary()
    {
        var workspace = CompilerFixtures.ValidWorkspace();
        var graph = workspace.Graphs[0];
        var config = Json("""
            {"outputSchema":{"kind":"object","fields":{
              "text":{"type":{"kind":"string"},"required":true},
              "nested":{"type":{"kind":"array","elementType":{"kind":"nullable","innerType":{
                "kind":"object","fields":{"mood":{"type":{"kind":"enum","values":["calm","happy"]},"required":true}}
              }}},"required":true}
            }}}
            """);
        ((FlowNode[])graph.Nodes)[1] = new FlowNode("ai", "ai", 1, config);
        ((DataEdge[])graph.DataEdges)[1] = new DataEdge(new PortRef("ai", "output"), new string[] { "text" }, new PortRef("send", "body"));
        var result = FlowCompiler.Compile(workspace, CompilerFixtures.CompleteRegistry(), FlowEnvironment.Testnet, "nested-release");
        var release = Assert.IsType<CompiledFlowRelease>(result.Release);
        var plan = release.Graphs.Single().OrderedNodes.Single(node => node.Id == "ai");
        var root = plan.DataOutputs.Single().Type;
        var nested = root.Fields["nested"].Type;
        var nullable = Assert.IsType<CompiledFlowType>(nested.ElementType);
        var inner = Assert.IsType<CompiledFlowType>(nullable.InnerType);
        var mood = inner.Fields["mood"].Type;

        Assert.Throws<NotSupportedException>(() => ((IList<string>)mood.Values)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, CompiledFlowField>)inner.Fields).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, CompiledFlowField>)root.Fields).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<CompiledDataPort>)plan.DataOutputs).Clear());
        ((FlowNode[])graph.Nodes)[1] = new FlowNode("ai", "ai", 1, Json("{}"));
        ((string[])graph.DataEdges[1].FieldPath)[0] = "changed";

        Assert.Equal("object", root.Kind);
        Assert.Equal("array", nested.Kind);
        Assert.Equal("nullable", nullable.Kind);
        Assert.Equal("object", inner.Kind);
        Assert.Equal("enum", mood.Kind);
        Assert.Equal(["calm", "happy"], mood.Values);
        Assert.True(inner.Fields["mood"].Required);
        Assert.Equal(config.GetRawText(), plan.Config.GetRawText());
        Assert.Equal("text", release.Graphs.Single().DataEdges.Single(edge => edge.Source.NodeId == "ai").FieldPath.Single());
    }

    [Fact]
    public void InvalidWorkspaceReturnsNoRelease()
    {
        var workspace = CompilerFixtures.ValidWorkspace();
        var invalid = CompilerFixtures.WithGraph(workspace, FlowTestFixtures.WithUnsupportedCodeNode());

        var result = FlowCompiler.Compile(invalid, CompilerFixtures.CompleteRegistry(), FlowEnvironment.Testnet, "release-invalid");

        Assert.Null(result.Release);
        Assert.Contains(result.Issues, issue => issue.Code == "node.kind_unsupported");
    }

    [Fact]
    public void CompiledGraphUsesDeterministicOrdinalTopologicalNodeOrder()
    {
        var workspace = CompilerFixtures.WithGraph(CompilerFixtures.ValidWorkspace(), CompilerFixtures.TiedBranchesGraph());

        var result = FlowCompiler.Compile(workspace, CompilerFixtures.CompleteRegistry(), FlowEnvironment.Testnet, "release-order");

        Assert.NotNull(result.Release);
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == FlowIssueSeverity.Error);
        var graph = Assert.Single(result.Release!.Graphs);
        Assert.Equal(["manual", "parallel", "branch-a", "branch-b", "join"], graph.OrderedNodes.Select(node => node.Id));
    }

    [Fact]
    public void DraftMutationsCannotChangeACompiledRelease()
    {
        var workspace = CompilerFixtures.ValidWorkspace();
        var registry = CompilerFixtures.CompleteRegistry();
        var result = FlowCompiler.Compile(workspace, registry, FlowEnvironment.Testnet, "release-frozen");

        Assert.NotNull(result.Release);
        var release = result.Release!;
        var graph = ((GraphDocument[])workspace.Graphs)[0];
        ((CharacterDefinition[])workspace.Characters)[0] = new CharacterDefinition("char-a", "changed", "changed personality", "graph-a");
        ((ModelProfileDefinition[])workspace.ModelProfiles)[0] = new ModelProfileDefinition("profile-a", "changed-credential-handle");
        ((FlowNode[])graph.Nodes)[1] = new FlowNode("ai", "ai", 1, Json("{\"outputSchema\":{\"kind\":\"object\",\"fields\":{\"nested\":{\"type\":{\"kind\":\"array\",\"elementType\":{\"kind\":\"string\"}}}}}}"));
        ((DestinationBinding[])registry[0].Destinations)[0] = new DestinationBinding("destination-a", FlowEnvironment.Testnet, "changed-account", "changed-conversation");

        Assert.Equal("A", release.Characters.Single().Personality);
        Assert.Equal("model-a", release.ModelProfiles.Single().CredentialReference);
        Assert.Equal("{}", release.Graphs.Single().OrderedNodes.Single(node => node.Id == "ai").Config.GetRawText());
        Assert.Equal("account-testnet", release.EnvironmentBindings.Destinations.Single().AccountId);
    }

    [Fact]
    public void SameDraftPublishesIndependentEnvironmentBoundReleases()
    {
        var workspace = CompilerFixtures.ValidWorkspace();
        var registry = CompilerFixtures.CompleteRegistry();

        var testnet = FlowCompiler.Compile(workspace, registry, FlowEnvironment.Testnet, "release-testnet");
        var mainnet = FlowCompiler.Compile(workspace, registry, FlowEnvironment.Mainnet, "release-mainnet");

        Assert.NotNull(testnet.Release);
        Assert.NotNull(mainnet.Release);
        Assert.Equal(FlowEnvironment.Testnet, testnet.Release!.Environment);
        Assert.Equal(FlowEnvironment.Mainnet, mainnet.Release!.Environment);
        Assert.Equal("physical-testnet", testnet.Release.EnvironmentBindings.Spaces.Single().PhysicalStoreKey);
        Assert.Equal("physical-mainnet", mainnet.Release.EnvironmentBindings.Spaces.Single().PhysicalStoreKey);
        Assert.Equal("release-testnet", testnet.Release.ReleaseId);
    }

    [Fact]
    public void FreezesGraphOwnedIngressRouteBinding()
    {
        var result = FlowCompiler.Compile(
            CompilerFixtures.IngressWorkspace(),
            CompilerFixtures.CompleteRegistry(includeIngressRoute: true),
            FlowEnvironment.Testnet,
            "release-ingress");

        Assert.NotNull(result.Release);
        Assert.Equal("ingress-a", result.Release!.EnvironmentBindings.RouteOwners.Single().IngressGraphId);
        Assert.Null(result.Release.EnvironmentBindings.RouteOwners.Single().OwnerCharacterId);
    }

    [Fact]
    public void DetectsCharacterCallRecursionAcrossGraphsBeforeFreezing()
    {
        var workspace = CompilerFixtures.RecursiveWorkspace();

        var result = FlowCompiler.Compile(workspace, CompilerFixtures.CompleteRegistry(), FlowEnvironment.Testnet, "release-recursive");

        Assert.Null(result.Release);
        Assert.Contains(result.Issues, issue => issue.Code == "binding.character_recursion");
    }

    [Fact]
    public void ExportImportRoundTripRevalidatesAndAllowsExplicitTargetRebinding()
    {
        var workspace = CompilerFixtures.ValidWorkspace();
        var registry = CompilerFixtures.CompleteRegistry();
        var compiled = FlowCompiler.Compile(workspace, registry, FlowEnvironment.Testnet, "release-portable");
        Assert.NotNull(compiled.Release);

        var artifact = FlowCompiler.Export(compiled.Release!);
        var imported = FlowCompiler.Import(artifact, registry, FlowEnvironment.Mainnet);

        Assert.NotNull(imported.Release);
        Assert.Empty(imported.Issues);
        Assert.Equal("release-portable", imported.Release!.ReleaseId);
        Assert.Equal(FlowEnvironment.Mainnet, imported.Release.Environment);
        Assert.Equal("model-a", imported.Release.ModelProfiles.Single().CredentialReference);
        Assert.Equal(compiled.Release.Graphs.Single().OrderedNodes.Select(node => node.Id), imported.Release.Graphs.Single().OrderedNodes.Select(node => node.Id));
        Assert.Equal("physical-mainnet", imported.Release.EnvironmentBindings.Spaces.Single().PhysicalStoreKey);
    }

    [Fact]
    public void ImportRejectsUnsupportedArtifactAndCatalogVersions()
    {
        var workspace = CompilerFixtures.ValidWorkspace();
        var registry = CompilerFixtures.CompleteRegistry();
        var compiled = FlowCompiler.Compile(workspace, registry, FlowEnvironment.Testnet, "release-versioned");
        Assert.NotNull(compiled.Release);

        var artifact = FlowCompiler.Export(compiled.Release!);
        var unsupportedArtifact = artifact.Replace("\"artifactFormatVersion\":1", "\"artifactFormatVersion\":99", StringComparison.Ordinal);
        var unsupportedCatalog = artifact.Replace("\"catalogVersion\":1", "\"catalogVersion\":99", StringComparison.Ordinal);

        var artifactResult = FlowCompiler.Import(unsupportedArtifact, registry, FlowEnvironment.Testnet);
        var catalogResult = FlowCompiler.Import(unsupportedCatalog, registry, FlowEnvironment.Testnet);

        Assert.Null(artifactResult.Release);
        Assert.Contains(artifactResult.Issues, issue => issue.Code == "artifact.version_unsupported");
        Assert.Null(catalogResult.Release);
        Assert.Contains(catalogResult.Issues, issue => issue.Code == "artifact.catalog_unsupported");
    }

    [Fact]
    public void ImportRevalidatesTamperedGraphInsteadOfTrustingArtifact()
    {
        var workspace = CompilerFixtures.ValidWorkspace();
        var registry = CompilerFixtures.CompleteRegistry();
        var compiled = FlowCompiler.Compile(workspace, registry, FlowEnvironment.Testnet, "release-tampered");
        Assert.NotNull(compiled.Release);

        var artifact = FlowCompiler.Export(compiled.Release!).Replace("\"kind\":\"ai\"", "\"kind\":\"code\"", StringComparison.Ordinal);
        var result = FlowCompiler.Import(artifact, registry, FlowEnvironment.Testnet);

        Assert.Null(result.Release);
        Assert.Contains(result.Issues, issue => issue.Code == "node.kind_unsupported");
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static class CompilerFixtures
    {
        public static FlowWorkspaceDefinition ValidWorkspace()
        {
            var source = FlowTestFixtures.MinimalTextFlow();
            var graph = new GraphDocument("graph-a", 1, GraphRole.Character, "char-a", source.Nodes, source.DataEdges, source.ControlEdges, source.EntryNodeIds);
            return new FlowWorkspaceDefinition(
                [new CharacterDefinition("char-a", "Alex", "A", "graph-a")],
                [graph],
                [new ModelProfileDefinition("profile-a", "model-a")],
                new Dictionary<string, string>(StringComparer.Ordinal) { ["char-a"] = "profile-a" });
        }

        public static IReadOnlyList<EnvironmentBindings> CompleteRegistry(bool includeIngressRoute = false) =>
        [
            Environment(FlowEnvironment.Testnet, "physical-testnet", "account-testnet", "conversation-testnet", includeIngressRoute),
            Environment(FlowEnvironment.Mainnet, "physical-mainnet", "account-mainnet", "conversation-mainnet", includeIngressRoute),
        ];

        public static FlowWorkspaceDefinition IngressWorkspace()
        {
            var workspace = ValidWorkspace();
            return new FlowWorkspaceDefinition(
                workspace.Characters,
                workspace.Graphs.Append(new GraphDocument(
                    "ingress-a",
                    1,
                    GraphRole.Ingress,
                    null,
                    [
                        Node("manual", "manual-input"),
                        Node("dispatch", "character-dispatch", "{\"characterId\":\"char-a\"}"),
                        Node("send", "send", "{\"destinationId\":\"fake-destination\"}"),
                    ],
                    [
                        new DataEdge(new PortRef("manual", "text"), [], new PortRef("dispatch", "input")),
                        new DataEdge(new PortRef("dispatch", "result"), [], new PortRef("send", "body")),
                    ],
                    [
                        new ControlEdge(new PortRef("manual", "success"), new PortRef("dispatch", "run")),
                        new ControlEdge(new PortRef("dispatch", "success"), new PortRef("send", "run")),
                    ],
                    ["manual"])),
                workspace.ModelProfiles,
                workspace.CharacterModelProfileReferences);
        }

        public static FlowWorkspaceDefinition WithGraph(FlowWorkspaceDefinition workspace, GraphDocument graph) =>
            new(workspace.Characters, [graph], workspace.ModelProfiles, workspace.CharacterModelProfileReferences);

        public static GraphDocument TiedBranchesGraph() => new(
            "graph-a",
            1,
            GraphRole.Character,
            "char-a",
            [
                Node("join", "join", "{\"mode\":\"all\",\"branches\":[\"a\",\"b\"]}"),
                Node("branch-b", "context"),
                Node("parallel", "parallel", "{\"branches\":[\"a\",\"b\"]}"),
                Node("manual", "manual-input"),
                Node("branch-a", "context"),
            ],
            [],
            [
                new ControlEdge(new PortRef("manual", "success"), new PortRef("parallel", "run")),
                new ControlEdge(new PortRef("parallel", "a"), new PortRef("branch-b", "run")),
                new ControlEdge(new PortRef("parallel", "b"), new PortRef("branch-a", "run")),
                new ControlEdge(new PortRef("branch-b", "success"), new PortRef("join", "a")),
                new ControlEdge(new PortRef("branch-a", "success"), new PortRef("join", "b")),
            ],
            ["manual"]);

        public static FlowWorkspaceDefinition RecursiveWorkspace()
        {
            return new FlowWorkspaceDefinition(
                [
                    new CharacterDefinition("char-a", "A", "A", "graph-a"),
                    new CharacterDefinition("char-b", "B", "B", "graph-b"),
                ],
                [
                    new GraphDocument("graph-a", 1, GraphRole.Character, "char-a", [Node("invoke-b", "character-dispatch", "{\"characterId\":\"char-b\"}")], [], [], ["invoke-b"]),
                    new GraphDocument("graph-b", 1, GraphRole.Character, "char-b", [Node("invoke-a", "character-dispatch", "{\"characterId\":\"char-a\"}")], [], [], ["invoke-a"]),
                ],
                [new ModelProfileDefinition("profile-a", "model-a"), new ModelProfileDefinition("profile-b", "model-b")],
                new Dictionary<string, string>(StringComparer.Ordinal) { ["char-a"] = "profile-a", ["char-b"] = "profile-b" });
        }

        private static EnvironmentBindings Environment(FlowEnvironment environment, string physicalStoreKey, string accountId, string conversationId, bool includeIngressRoute) =>
            new(
                environment,
                includeIngressRoute
                    ? [new RouteOwnerBinding("route-ingress", accountId, conversationId, null, IngressGraphId: "ingress-a")]
                    : [],
                [new SpaceBinding("space-a", environment, FlowSpaceScope.Private, "char-a", [conversationId], physicalStoreKey)],
                [],
                ["model", "send"],
                [new DestinationBinding("fake-destination", environment, accountId, conversationId, "display-only")]);

        private static FlowNode Node(string id, string kind, string config = "{}") => new(id, kind, 1, Json(config));
    }
}
