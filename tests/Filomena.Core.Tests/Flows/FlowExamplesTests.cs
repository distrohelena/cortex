using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class FlowExamplesTests
{
    [Theory]
    [InlineData("minimal-text")]
    [InlineData("structured-response")]
    [InlineData("two-characters")]
    public void LiteralPortableExamplesImportAndCompile(string exampleName)
    {
        var result = FlowCompiler.Import(
            LoadArtifact(exampleName),
            CompleteRegistry(exampleName),
            FlowEnvironment.Testnet);

        AssertNoCompilationErrors(result);
        Assert.NotNull(result.Release);
    }

    [Fact]
    public void StructuredResponseWiresOnlyTextoToTheSendNode()
    {
        var release = CompileExample("structured-response");
        var graph = Assert.Single(release.Graphs, graph => graph.Id == "structured-response");
        var ai = Assert.Single(graph.OrderedNodes, node => node.Id == "ai");

        Assert.Equal("object", ai.DataOutputs.Single().Type.Kind);
        Assert.Equal(["humor", "reacao", "texto"], ai.DataOutputs.Single().Type.Fields.Keys.OrderBy(value => value, StringComparer.Ordinal));

        var sendEdges = graph.DataEdges
            .Where(edge => edge.Target.NodeId == "send")
            .ToArray();
        var sendEdge = Assert.Single(sendEdges);
        Assert.Equal(new PortRef("ai", "output"), sendEdge.Source);
        Assert.Equal(["texto"], sendEdge.FieldPath);
        Assert.DoesNotContain(graph.DataEdges, edge => edge.FieldPath.Contains("humor", StringComparer.Ordinal));
        Assert.DoesNotContain(graph.DataEdges, edge => edge.FieldPath.Contains("reacao", StringComparer.Ordinal));
    }

    [Fact]
    public void AddingAnUnconnectedStructuredFieldDoesNotCreateAnImplicitBoundInput()
    {
        var root = JsonNode.Parse(LoadArtifact("structured-response"))!.AsObject();
        var graph = root["workspace"]!["graphs"]!.AsArray()
            .Single(node => node!["id"]!.GetValue<string>() == "structured-response")!
            .AsObject();
        var ai = graph["nodes"]!.AsArray()
            .Single(node => node!["id"]!.GetValue<string>() == "ai")!
            .AsObject();
        ai["config"]!["outputSchema"]!["fields"]!.AsObject()["fixtureOnly"] = JsonNode.Parse(
            "{\"type\":{\"kind\":\"string\"},\"required\":false}");

        var result = FlowCompiler.Import(
            root.ToJsonString(),
            CompleteRegistry("structured-response"),
            FlowEnvironment.Testnet);

        AssertNoCompilationErrors(result);
        Assert.NotNull(result.Release);
        var compiledGraph = Assert.Single(result.Release!.Graphs, value => value.Id == "structured-response");
        var sendEdges = compiledGraph.DataEdges
            .Where(edge => edge.Target.NodeId == "send")
            .ToArray();
        var sendEdge = Assert.Single(sendEdges);
        Assert.Equal(["texto"], sendEdge.FieldPath);
        Assert.DoesNotContain(compiledGraph.DataEdges, edge => edge.FieldPath.Contains("fixtureOnly", StringComparer.Ordinal));
    }

    [Fact]
    public void TwoCharactersFanOutThroughIngressAndKeepPrivateSpacesDistinct()
    {
        var release = CompileExample("two-characters");
        var ingress = Assert.Single(release.Graphs, graph => graph.Id == "two-characters-ingress");

        Assert.Equal(GraphRole.Ingress, ingress.Role);
        Assert.Null(ingress.CharacterId);
        Assert.Equal(
            ["lia", "noa"],
            ingress.OrderedNodes
                .Where(node => node.Kind == "character-dispatch")
                .Select(node => node.Config.GetProperty("characterId").GetString())
                .Order(StringComparer.Ordinal));
        Assert.Contains(ingress.DataEdges, edge => edge.Source == new PortRef("input", "text") && edge.Target == new PortRef("dispatch-lia", "input"));
        Assert.Contains(ingress.DataEdges, edge => edge.Source == new PortRef("input", "text") && edge.Target == new PortRef("dispatch-noa", "input"));

        Assert.Equal(["character-lia", "character-noa", "two-characters-ingress"], release.Graphs.Select(graph => graph.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["lia", "noa"], release.Characters.Select(character => character.Id).Order(StringComparer.Ordinal));

        var spaces = release.EnvironmentBindings.Spaces.OrderBy(space => space.LogicalSpaceId, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, spaces.Length);
        Assert.All(spaces, space => Assert.Equal(FlowSpaceScope.Private, space.Scope));
        Assert.Equal(["lia", "noa"], spaces.Select(space => space.OwnerCharacterId).Order(StringComparer.Ordinal));
        Assert.Equal(["conversation-lia-testnet", "conversation-noa-testnet"], spaces.SelectMany(space => space.ExplicitConversationIds).Order(StringComparer.Ordinal));
        Assert.Equal(2, spaces.Select(space => space.PhysicalStoreKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("minimal-text")]
    [InlineData("structured-response")]
    [InlineData("two-characters")]
    public void PortableExamplesLeaveEnvironmentBindingsToTheImportTarget(string exampleName)
    {
        using var document = JsonDocument.Parse(LoadArtifact(exampleName));
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("bindings", out _));
        Assert.False(root.TryGetProperty("environmentBindings", out _));
        Assert.True(root.TryGetProperty("workspace", out _));
    }

    private static CompiledFlowRelease CompileExample(string exampleName)
    {
        var result = FlowCompiler.Import(
            LoadArtifact(exampleName),
            CompleteRegistry(exampleName),
            FlowEnvironment.Testnet);

        AssertNoCompilationErrors(result);
        Assert.NotNull(result.Release);
        Assert.Empty(result.Issues);
        return result.Release!;
    }

    private static void AssertNoCompilationErrors(FlowCompilationResult result)
    {
        var errors = result.Issues
            .Where(issue => issue.Severity == FlowIssueSeverity.Error)
            .Select(issue => $"{issue.Code} at {issue.Path}: {issue.Message}")
            .ToArray();
        Assert.True(errors.Length == 0, "Flow compilation errors:\n" + string.Join('\n', errors));
    }

    private static string LoadArtifact(string exampleName)
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "examples", "flows", exampleName + ".json");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Filomena.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Filomena repository root.");
    }

    private static IReadOnlyList<EnvironmentBindings> CompleteRegistry(string exampleName) =>
    [
        CreateEnvironmentBindings(exampleName, FlowEnvironment.Testnet),
        CreateEnvironmentBindings(exampleName, FlowEnvironment.Mainnet),
    ];

    private static EnvironmentBindings CreateEnvironmentBindings(string exampleName, FlowEnvironment environment)
    {
        var environmentName = environment == FlowEnvironment.Testnet ? "testnet" : "mainnet";
        var accountId = "fake-account-" + environmentName;
        var routeConversationId = "fake-inbox-conversation-" + environmentName;
        string[] characterIds = exampleName == "two-characters" ? ["lia", "noa"] : ["filomena"];
        string[] destinations = exampleName == "two-characters"
            ? ["fake-destination-lia", "fake-destination-noa"]
            : ["fake-destination"];
        RouteOwnerBinding[] routeOwners = exampleName == "two-characters"
            ? [new RouteOwnerBinding(
                "fake-ingress-route",
                accountId,
                routeConversationId,
                null,
                IngressGraphId: "two-characters-ingress")]
            : [new RouteOwnerBinding(
                "fake-character-route",
                accountId,
                "fake-character-conversation-" + environmentName,
                "filomena")];

        return new EnvironmentBindings(
            environment,
            routeOwners,
            characterIds.Select(characterId => new SpaceBinding(
                "private-" + characterId,
                environment,
                FlowSpaceScope.Private,
                characterId,
                ["conversation-" + characterId + "-" + environmentName],
                "fake-store-" + characterId + "-" + environmentName)),
            [],
            ["model", "send"],
            destinations.Select(destinationId => new DestinationBinding(
                destinationId,
                environment,
                accountId,
                "conversation-" + destinationId + "-" + environmentName,
                "Fake destination")));
    }
}
