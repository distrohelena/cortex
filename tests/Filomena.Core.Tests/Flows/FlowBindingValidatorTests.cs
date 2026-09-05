using System.Text.Json;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class FlowBindingValidatorTests
{
    [Fact]
    public void ResolvesCharactersProfilesSpacesAndDestinationsByStructuralIds()
    {
        var (workspace, bindings) = FlowTestFixtures.ValidWorkspace();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Empty(issues);
    }

    [Fact]
    public void SameDisplayNamesDoNotMakeDifferentCharacterIdsEquivalent()
    {
        var (workspace, bindings) = FlowTestFixtures.ValidWorkspace();
        var renamed = new FlowWorkspaceDefinition(
            [new CharacterDefinition("char-a", "Same name", "A", "graph-a"), new CharacterDefinition("char-b", "Same name", "B", "graph-b")],
            workspace.Graphs,
            workspace.ModelProfiles,
            workspace.CharacterModelProfileReferences);

        var issues = FlowBindingValidator.Validate(renamed, bindings);

        Assert.Empty(issues);
    }

    [Fact]
    public void ReportsMissingCharacterAndModelProfileReferences()
    {
        var (workspace, bindings) = FlowTestFixtures.ValidWorkspace(missingReferences: true);

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.character_missing");
        Assert.Contains(issues, issue => issue.Code == "binding.model_profile_missing");
    }

    [Fact]
    public void RejectsRecursiveCharacterCallsAcrossGraphs()
    {
        var (workspace, bindings) = FlowTestFixtures.RecursiveCharacters();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.character_recursion");
    }

    [Fact]
    public void RequiresGrantForDirectPrivateAccessToAnotherCharacterSpace()
    {
        var (workspace, bindings) = FlowTestFixtures.PrivateAccessWithoutGrant();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.direct_grant_required");
    }

    [Fact]
    public void RequiresExactlyOneConversationForPrivateSpaceBindings()
    {
        var (workspace, bindings) = FlowTestFixtures.PrivateSpaceWithoutConversation();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.private_conversation_required");
    }

    [Fact]
    public void RejectsPrivatePhysicalAliasesAcrossConversationsEvenForTheSameOwner()
    {
        var (workspace, bindings) = FlowTestFixtures.PrivateCrossConversationAlias();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.private_conversation_alias");
    }

    [Fact]
    public void RequiresProtectorAndMediatedGrantForMediatedPrivateAccess()
    {
        var (workspace, bindings) = FlowTestFixtures.MediatedAccessWithoutProtector();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.protector_required");
    }

    [Fact]
    public void RequiresMatchingMediatedGrantForProtectedAccess()
    {
        var (workspace, bindings) = FlowTestFixtures.MediatedAccessWithoutGrant();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.mediated_grant_required");
    }

    [Fact]
    public void RejectsUnknownDestinationAndEnvironmentMismatch()
    {
        var (workspace, bindings) = FlowTestFixtures.UnknownDestinationAndMismatchedEnvironment();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.destination_unknown");
        Assert.Contains(issues, issue => issue.Code == "binding.environment_mismatch");
    }

    [Fact]
    public void RejectsCrossEnvironmentPhysicalAliasesUsingTheCompleteRegistry()
    {
        var (workspace, bindings) = FlowTestFixtures.CrossEnvironmentPhysicalAlias();

        Assert.Contains(FlowBindingValidator.Validate(workspace, bindings),
            issue => issue.Code == "binding.cross_environment_alias");
    }

    [Fact]
    public void RejectsSharedPhysicalAliasesAcrossEnvironmentsEvenWhenLogicalSpaceMatches()
    {
        var (workspace, bindings) = FlowTestFixtures.SharedCrossEnvironmentAlias();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.cross_environment_alias");
    }

    [Fact]
    public void RejectsPrivateAndSharedBindingsThatShareOnePhysicalKeyInAnEnvironment()
    {
        var (workspace, bindings) = FlowTestFixtures.PrivateAndSharedSameEnvironmentAlias();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.physical_scope_conflict");
    }

    [Fact]
    public void RejectsDistinctSharedLogicalSpacesThatShareOnePhysicalKeyInAnEnvironment()
    {
        var (workspace, bindings) = FlowTestFixtures.IndependentSharedLogicalAliases();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.physical_logical_alias");
    }

    [Fact]
    public void RejectsAmbiguousRouteOwnersAcrossTheCompleteRegistry()
    {
        var (workspace, bindings) = FlowTestFixtures.AmbiguousRoutes();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.route_ambiguous");
    }

    [Fact]
    public void AllowsOnlyMarkedRouteDuplicationWithASeparateGrant()
    {
        var (workspace, bindings) = FlowTestFixtures.ExplicitDuplicateRoutes();

        Assert.Empty(FlowBindingValidator.Validate(workspace, bindings));
    }

    [Fact]
    public void RejectsPrivateCrossCharacterPhysicalAliasesButAllowsDeclaredSharedSpace()
    {
        var (workspace, bindings) = FlowTestFixtures.PrivateAndSharedPhysicalAliases();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.private_physical_alias");
    }

    [Fact]
    public void ASendNodeWithoutAnExplicitDestinationCannotPublish()
    {
        var (workspace, bindings) = FlowTestFixtures.SendWithoutDestination();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.destination_required");
    }

    [Fact]
    public void RequiresCapabilityDeclaredByTheTrustedCatalogEffect()
    {
        var (workspace, bindings) = FlowTestFixtures.SendWithoutDestination();
        var restricted = new EnvironmentBindings(
            bindings[0].Environment,
            bindings[0].RouteOwners,
            bindings[0].SpaceBindings,
            bindings[0].Grants,
            [],
            bindings[0].Destinations);

        var issues = FlowBindingValidator.Validate(workspace, [restricted]);

        Assert.Contains(issues, issue => issue.Code == "binding.capability_missing");
    }

    [Fact]
    public void InvocationCannotGrantCallerAnotherCharactersAuthority()
    {
        var (workspace, bindings) = FlowTestFixtures.InvocationAuthorityEscalation();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.authority_escalation");
    }

    [Fact]
    public void InvalidDuplicateAndMissingCharacterReferencesReturnIssuesInsteadOfThrowing()
    {
        var (workspace, bindings) = FlowTestFixtures.InvalidCharacterReferences();

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.character_duplicate");
        Assert.Contains(issues, issue => issue.Code == "binding.character_missing");
    }

    [Fact]
    public void AllowsIngressOwnedRouteForSharedFanout()
    {
        var (workspace, bindings) = FlowTestFixtures.IngressOwnedRoute();

        Assert.Empty(FlowBindingValidator.Validate(workspace, bindings));
    }

    [Fact]
    public void CharacterRenameAndProfileDisplayNamesDoNotChangeIdentity()
    {
        var (workspace, bindings) = FlowTestFixtures.ValidWorkspace();
        var changedLabels = new FlowWorkspaceDefinition(
            [new CharacterDefinition("char-a", "Renamed", "A", "graph-a"), new CharacterDefinition("char-b", "Renamed too", "B", "graph-b")],
            workspace.Graphs,
            [new ModelProfileDefinition("profile-a", "renamed-model"), new ModelProfileDefinition("profile-b", "another-model")],
            workspace.CharacterModelProfileReferences);

        Assert.Empty(FlowBindingValidator.Validate(changedLabels, bindings));
    }

    [Fact]
    public void DestinationDisplayNamesAreNotUsedForResolution()
    {
        var (workspace, bindings) = FlowTestFixtures.ValidWorkspace(destinationConfig: "Friendly destination");

        var issues = FlowBindingValidator.Validate(workspace, bindings);

        Assert.Contains(issues, issue => issue.Code == "binding.destination_unknown");
    }

    private static JsonElement Config(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static FlowNode Node(string id, string kind, string config = "{}") =>
        new(id, kind, 1, Config(config));

    private static GraphDocument CharacterGraph(string id, string characterId, params FlowNode[] nodes) =>
        new(id, 1, GraphRole.Character, characterId, nodes, [], []);

    private static GraphDocument IngressGraph(string id, params FlowNode[] nodes) =>
        new(id, 1, GraphRole.Ingress, null, nodes, [], []);

    private static class FlowTestFixtures
    {
        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) ValidWorkspace(
            bool missingReferences = false,
            string? destinationConfig = null)
        {
            var workspace = new FlowWorkspaceDefinition(
                [new CharacterDefinition("char-a", "Alex", "A", "graph-a"), new CharacterDefinition("char-b", "Alex", "B", "graph-b")],
                [CharacterGraph("graph-a", "char-a"), CharacterGraph("graph-b", "char-b")],
                [new ModelProfileDefinition("profile-a", "model-a"), new ModelProfileDefinition("profile-b", "model-b")],
                missingReferences
                    ? new Dictionary<string, string> { ["char-a"] = "missing-profile", ["missing-character"] = "profile-a" }
                    : new Dictionary<string, string> { ["char-a"] = "profile-a", ["char-b"] = "profile-b" });

            var environment = new EnvironmentBindings(
                FlowEnvironment.Testnet,
                [new RouteOwnerBinding("route-a", "account-1", "conversation-1", "char-a")],
                [new SpaceBinding("space-a", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-a", ["conversation-1"], "physical-a")],
                [],
                ["memory.read", "memory.write", "send"],
                [new DestinationBinding("destination-a", FlowEnvironment.Testnet, "account-1", "conversation-1", "Friendly destination")]);

            if (destinationConfig is not null)
            {
                workspace = WithNode(workspace, CharacterGraph("graph-a", "char-a", Node("send", "send", $"{{\"destinationId\":\"{destinationConfig}\"}}")));
            }

            return (workspace, [environment]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) RecursiveCharacters()
        {
            var (workspace, bindings) = ValidWorkspace();
            return (WithGraphs(workspace, [
                CharacterGraph("graph-a", "char-a", Node("invoke-b", "character-dispatch", "{\"characterId\":\"char-b\"}")),
                CharacterGraph("graph-b", "char-b", Node("invoke-a", "character-dispatch", "{\"characterId\":\"char-a\"}"))]), bindings);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) PrivateAccessWithoutGrant()
        {
            var (workspace, bindings) = ValidWorkspace();
            return (WithNode(workspace, CharacterGraph("graph-a", "char-a", Node("read", "memory-read", "{\"spaceId\":\"space-b\"}"))),
                [WithSpace(bindings[0], new SpaceBinding("space-b", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-b", ["conversation-1"], "physical-b"))]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) MediatedAccessWithoutProtector()
        {
            var (workspace, bindings) = ValidWorkspace();
            var graph = CharacterGraph("graph-a", "char-a", Node("read", "memory-read", "{\"spaceId\":\"space-b\",\"accessMode\":\"mediated\"}"));
            return (WithNode(workspace, graph), [WithGrant(WithSpace(bindings[0], new SpaceBinding("space-b", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-b", ["conversation-1"], "physical-b")),
                new FlowGrant("char-a", "space-b", FlowAccessOperation.Read, FlowAccessMode.Mediated))]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) PrivateSpaceWithoutConversation()
        {
            var (workspace, bindings) = ValidWorkspace();
            return (workspace, [new EnvironmentBindings(
                bindings[0].Environment,
                bindings[0].RouteOwners,
                [new SpaceBinding("space-a", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-a", [], "physical-a")],
                bindings[0].Grants,
                bindings[0].Capabilities,
                bindings[0].Destinations)]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) PrivateCrossConversationAlias()
        {
            var (workspace, bindings) = ValidWorkspace();
            var environment = new EnvironmentBindings(
                bindings[0].Environment,
                bindings[0].RouteOwners,
                [
                    new SpaceBinding("space-a", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-a", ["conversation-1"], "physical-shared"),
                    new SpaceBinding("space-b", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-a", ["conversation-2"], "physical-shared"),
                ],
                bindings[0].Grants,
                bindings[0].Capabilities,
                bindings[0].Destinations);
            return (workspace, [environment]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) MediatedAccessWithoutGrant()
        {
            var (workspace, bindings) = ValidWorkspace();
            var graph = CharacterGraph("graph-a", "char-a", Node("read", "memory-read", "{\"spaceId\":\"space-b\",\"accessMode\":\"mediated\",\"protectorId\":\"protector-a\"}"));
            return (WithNode(workspace, graph), [WithSpace(bindings[0], new SpaceBinding("space-b", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-b", ["conversation-1"], "physical-b"))]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) UnknownDestinationAndMismatchedEnvironment()
        {
            var (workspace, bindings) = ValidWorkspace();
            var changed = WithNode(workspace, CharacterGraph("graph-a", "char-a", Node("send", "send", "{\"destinationId\":\"missing-destination\",\"environment\":\"mainnet\"}")));
            return (changed, bindings);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) CrossEnvironmentPhysicalAlias()
        {
            var (workspace, bindings) = ValidWorkspace();
            var mainnet = new EnvironmentBindings(
                FlowEnvironment.Mainnet,
                [],
                [new SpaceBinding("space-b", FlowEnvironment.Mainnet, FlowSpaceScope.Private, "char-b", ["conversation-1"], "physical-a")],
                [],
                ["memory.read"],
                []);
            return (workspace, [bindings[0], mainnet]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) SharedCrossEnvironmentAlias()
        {
            var (workspace, bindings) = ValidWorkspace();
            var testnet = new EnvironmentBindings(
                FlowEnvironment.Testnet,
                bindings[0].RouteOwners,
                [new SpaceBinding("shared", FlowEnvironment.Testnet, FlowSpaceScope.Shared, null, ["conversation-1"], "physical-shared")],
                bindings[0].Grants,
                bindings[0].Capabilities,
                bindings[0].Destinations);
            var mainnet = new EnvironmentBindings(
                FlowEnvironment.Mainnet,
                [],
                [new SpaceBinding("shared", FlowEnvironment.Mainnet, FlowSpaceScope.Shared, null, ["conversation-1"], "physical-shared")],
                [],
                [],
                []);
            return (workspace, [testnet, mainnet]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) PrivateAndSharedSameEnvironmentAlias()
        {
            var (workspace, bindings) = ValidWorkspace();
            var environment = new EnvironmentBindings(
                bindings[0].Environment,
                bindings[0].RouteOwners,
                [
                    new SpaceBinding("private", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-a", ["conversation-1"], "physical-overlap"),
                    new SpaceBinding("shared", FlowEnvironment.Testnet, FlowSpaceScope.Shared, null, ["conversation-1"], "physical-overlap"),
                ],
                bindings[0].Grants,
                bindings[0].Capabilities,
                bindings[0].Destinations);
            return (workspace, [environment]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) IndependentSharedLogicalAliases()
        {
            var (workspace, bindings) = ValidWorkspace();
            var environment = new EnvironmentBindings(
                bindings[0].Environment,
                bindings[0].RouteOwners,
                [
                    new SpaceBinding("shared-a", FlowEnvironment.Testnet, FlowSpaceScope.Shared, null, ["conversation-1"], "physical-overlap"),
                    new SpaceBinding("shared-b", FlowEnvironment.Testnet, FlowSpaceScope.Shared, null, ["conversation-1"], "physical-overlap"),
                ],
                bindings[0].Grants,
                bindings[0].Capabilities,
                bindings[0].Destinations);
            return (workspace, [environment]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) AmbiguousRoutes()
        {
            var (workspace, bindings) = ValidWorkspace();
            return (workspace, [WithRoute(bindings[0], new RouteOwnerBinding("route-b", "account-1", "conversation-1", "char-b"))]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) ExplicitDuplicateRoutes()
        {
            var (workspace, bindings) = ValidWorkspace();
            var environment = new EnvironmentBindings(
                bindings[0].Environment,
                [new RouteOwnerBinding("route-a", "account-1", "conversation-1", "char-a", true, "grant-route-a"), new RouteOwnerBinding("route-b", "account-1", "conversation-1", "char-b", true, "grant-route-b")],
                bindings[0].SpaceBindings,
                bindings[0].Grants,
                bindings[0].Capabilities,
                bindings[0].Destinations,
                [new RouteDuplicationGrant("grant-route-a", "route-a", "char-a"), new RouteDuplicationGrant("grant-route-b", "route-b", "char-b")]);
            return (workspace, [environment]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) PrivateAndSharedPhysicalAliases()
        {
            var (workspace, bindings) = ValidWorkspace();
            var env = WithSpace(bindings[0],
                new SpaceBinding("space-b", FlowEnvironment.Testnet, FlowSpaceScope.Private, "char-b", ["conversation-1"], "physical-a"),
                new SpaceBinding("shared", FlowEnvironment.Testnet, FlowSpaceScope.Shared, null, ["conversation-1"], "physical-shared"));
            var mainnet = new EnvironmentBindings(
                FlowEnvironment.Mainnet,
                [],
                [new SpaceBinding("shared", FlowEnvironment.Mainnet, FlowSpaceScope.Shared, null, ["conversation-1"], "physical-shared")],
                [],
                [],
                []);
            return (workspace, [env, mainnet]);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) SendWithoutDestination()
        {
            var (workspace, bindings) = ValidWorkspace();
            return (WithNode(workspace, CharacterGraph("graph-a", "char-a", Node("send", "send"))), bindings);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) InvocationAuthorityEscalation()
        {
            var (workspace, bindings) = ValidWorkspace();
            return (WithNode(workspace, CharacterGraph("graph-a", "char-a", Node("invoke", "character-dispatch", "{\"characterId\":\"char-b\",\"grantCharacterId\":\"char-b\"}"))), bindings);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) InvalidCharacterReferences()
        {
            var (workspace, bindings) = ValidWorkspace();
            var invalid = new FlowWorkspaceDefinition(
                [new CharacterDefinition("char-a", "Alex", "A", "graph-a"), new CharacterDefinition("char-a", "Alex duplicate", "A", "graph-a")],
                [CharacterGraph("graph-a", "missing-character")],
                workspace.ModelProfiles,
                new Dictionary<string, string> { ["missing-character"] = "profile-a" });
            return (invalid, bindings);
        }

        public static (FlowWorkspaceDefinition Workspace, IReadOnlyList<EnvironmentBindings> Bindings) IngressOwnedRoute()
        {
            var (workspace, bindings) = ValidWorkspace();
            var withIngress = WithGraphs(workspace, workspace.Graphs.Append(IngressGraph("ingress-graph")));
            var environment = new EnvironmentBindings(
                bindings[0].Environment,
                [new RouteOwnerBinding("ingress-route", "account-1", "conversation-2", null, IngressGraphId: "ingress-graph")],
                bindings[0].SpaceBindings,
                bindings[0].Grants,
                bindings[0].Capabilities,
                bindings[0].Destinations);
            return (withIngress, [environment]);
        }

        private static FlowWorkspaceDefinition WithNode(FlowWorkspaceDefinition workspace, GraphDocument replacement) =>
            WithGraphs(workspace, workspace.Graphs.Select(graph => graph.Id == replacement.Id ? replacement : graph));

        private static FlowWorkspaceDefinition WithGraphs(FlowWorkspaceDefinition workspace, IEnumerable<GraphDocument> graphs) =>
            new(workspace.Characters, graphs, workspace.ModelProfiles, workspace.CharacterModelProfileReferences);

        private static EnvironmentBindings WithSpace(EnvironmentBindings environment, params SpaceBinding[] additions) =>
            new(environment.Environment, environment.RouteOwners, environment.SpaceBindings.Concat(additions), environment.Grants, environment.Capabilities, environment.Destinations);

        private static EnvironmentBindings WithGrant(EnvironmentBindings environment, FlowGrant grant) =>
            new(environment.Environment, environment.RouteOwners, environment.SpaceBindings, environment.Grants.Append(grant), environment.Capabilities, environment.Destinations);

        private static EnvironmentBindings WithRoute(EnvironmentBindings environment, RouteOwnerBinding route) =>
            new(environment.Environment, environment.RouteOwners.Append(route), environment.SpaceBindings, environment.Grants, environment.Capabilities, environment.Destinations);
    }
}
