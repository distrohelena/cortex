using System.Text.Json;

namespace Cortex.Core.Flows;

public static class FlowBindingValidator
{
    public static IReadOnlyList<FlowIssue> Validate(
        FlowWorkspaceDefinition workspace,
        IReadOnlyList<EnvironmentBindings> bindings)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(bindings);

        var issues = new List<FlowIssue>();
        issues.AddRange(FlowWorkspaceShape.Validate(workspace, bindings));
        if (issues.Count != 0) return issues;

        var characters = workspace.Characters
            .GroupBy(character => character.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var graphs = workspace.Graphs
            .GroupBy(graph => graph.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var profiles = workspace.ModelProfiles
            .GroupBy(profile => profile.ProfileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        ValidateWorkspace(workspace, characters, graphs, profiles, issues);
        ValidateEnvironmentShapes(workspace, bindings, characters, issues);
        ValidateRoutes(bindings, issues);
        ValidatePhysicalAliases(bindings, issues);
        ValidateNodeBindings(workspace, bindings, characters, issues);

        return issues
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateWorkspace(
        FlowWorkspaceDefinition workspace,
        IReadOnlyDictionary<string, CharacterDefinition[]> characters,
        IReadOnlyDictionary<string, GraphDocument[]> graphs,
        IReadOnlyDictionary<string, ModelProfileDefinition[]> profiles,
        ICollection<FlowIssue> issues)
    {
        foreach (var group in characters.Values.Where(group => group.Length > 1))
        {
            issues.Add(new FlowIssue("binding.character_duplicate", $"/characters/{group[0].Id}", "Character IDs must be unique."));
        }

        foreach (var group in graphs.Values.Where(group => group.Length > 1))
        {
            issues.Add(new FlowIssue("binding.graph_duplicate", $"/graphs/{group[0].Id}", "Graph IDs must be unique."));
        }

        foreach (var group in profiles.Values.Where(group => group.Length > 1))
        {
            issues.Add(new FlowIssue("binding.model_profile_duplicate", $"/modelProfiles/{group[0].ProfileId}", "Model profile IDs must be unique."));
        }

        foreach (var character in workspace.Characters)
        {
            if (!graphs.ContainsKey(character.GraphId))
            {
                issues.Add(new FlowIssue("binding.graph_missing", $"/characters/{character.Id}/graphId", "Character graph ID does not resolve."));
            }
            else if (graphs[character.GraphId].Any(graph => graph.Role != GraphRole.Character ||
                !string.Equals(graph.CharacterId, character.Id, StringComparison.Ordinal)))
            {
                issues.Add(new FlowIssue("binding.character_graph_invalid", $"/characters/{character.Id}/graphId", "Character graph must be owned by that character."));
            }
        }

        foreach (var graph in workspace.Graphs)
        {
            if (graph.Role == GraphRole.Ingress && graph.CharacterId is not null)
            {
                issues.Add(new FlowIssue("binding.ingress_character_forbidden", $"/graphs/{graph.Id}/characterId", "Ingress graphs cannot carry character identity."));
            }

            if (graph.Role == GraphRole.Character && string.IsNullOrEmpty(graph.CharacterId))
            {
                issues.Add(new FlowIssue("binding.character_required", $"/graphs/{graph.Id}/characterId", "Character graphs require a character ID."));
            }
            else if (!string.IsNullOrEmpty(graph.CharacterId) && !characters.ContainsKey(graph.CharacterId))
            {
                issues.Add(new FlowIssue("binding.character_missing", $"/graphs/{graph.Id}/characterId", "Graph character ID does not resolve."));
            }
        }

        foreach (var reference in workspace.CharacterModelProfileReferences)
        {
            if (!characters.ContainsKey(reference.Key))
            {
                issues.Add(new FlowIssue("binding.character_missing", $"/modelProfileReferences/{reference.Key}", "Model profile reference character ID does not resolve."));
            }

            if (!profiles.ContainsKey(reference.Value))
            {
                issues.Add(new FlowIssue("binding.model_profile_missing", $"/modelProfileReferences/{reference.Key}", "Model profile ID does not resolve."));
            }
        }

        foreach (var graph in workspace.Graphs)
        {
            foreach (var (node, index) in graph.Nodes.Select((node, index) => (node, index)))
            {
                var path = $"/graphs/{graph.Id}/nodes/{index}";
                if (TryGetString(node.Config, "modelProfileId", out var profileId) && !profiles.ContainsKey(profileId))
                {
                    issues.Add(new FlowIssue("binding.model_profile_missing", $"{path}/config/modelProfileId", "Node model profile ID does not resolve."));
                }

                if (TryGetString(node.Config, "characterId", out var characterId) && !characters.ContainsKey(characterId))
                {
                    issues.Add(new FlowIssue("binding.character_missing", $"{path}/config/characterId", "Node character ID does not resolve."));
                }

                if (IsInvocation(node.Kind) && TryGetString(node.Config, "grantCharacterId", out var grantedCharacterId) &&
                    !string.Equals(grantedCharacterId, graph.CharacterId, StringComparison.Ordinal))
                {
                    issues.Add(new FlowIssue("binding.authority_escalation", $"{path}/config/grantCharacterId", "Invocation cannot grant the caller another character's authority."));
                }

                if (IsInvocation(node.Kind) && TryGetString(node.Config, "authorityCharacterId", out var authorityCharacterId) &&
                    !string.Equals(authorityCharacterId, graph.CharacterId, StringComparison.Ordinal))
                {
                    issues.Add(new FlowIssue("binding.authority_escalation", $"{path}/config/authorityCharacterId", "Invocation cannot grant the caller another character's authority."));
                }
            }
        }

        var calls = characters.Keys.ToDictionary(characterId => characterId, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var graph in workspace.Graphs.Where(graph => graph.Role == GraphRole.Character && graph.CharacterId is not null))
        {
            if (!calls.TryGetValue(graph.CharacterId!, out var graphCalls))
            {
                continue;
            }

            foreach (var node in graph.Nodes.Where(node => IsInvocation(node.Kind)))
            {
                if (TryGetString(node.Config, "characterId", out var targetCharacterId) && characters.ContainsKey(targetCharacterId))
                {
                    graphCalls.Add(targetCharacterId);
                }
            }
        }

        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var character in workspace.Characters.OrderBy(character => character.Id, StringComparer.Ordinal))
        {
            if (HasCycle(character.Id, calls, states, new HashSet<string>(StringComparer.Ordinal)))
            {
                issues.Add(new FlowIssue("binding.character_recursion", $"/characters/{character.Id}", "Character invocation graph must be acyclic."));
            }
        }
    }

    private static bool HasCycle(
        string characterId,
        IReadOnlyDictionary<string, HashSet<string>> calls,
        IDictionary<string, int> states,
        ISet<string> path)
    {
        if (states.TryGetValue(characterId, out var state))
        {
            return state == 1 && path.Contains(characterId);
        }

        states[characterId] = 1;
        path.Add(characterId);
        foreach (var target in calls[characterId].OrderBy(value => value, StringComparer.Ordinal))
        {
            if (HasCycle(target, calls, states, path))
            {
                return true;
            }
        }

        path.Remove(characterId);
        states[characterId] = 2;
        return false;
    }

    private static void ValidateEnvironmentShapes(
        FlowWorkspaceDefinition workspace,
        IReadOnlyList<EnvironmentBindings> bindings,
        IReadOnlyDictionary<string, CharacterDefinition[]> characters,
        ICollection<FlowIssue> issues)
    {
        var environments = new HashSet<FlowEnvironment>();
        foreach (var environment in bindings)
        {
            if (!environments.Add(environment.Environment))
            {
                issues.Add(new FlowIssue("binding.environment_duplicate", $"/environments/{environment.Environment}", "The complete registry must contain one binding set per environment."));
            }

            foreach (var route in environment.RouteOwners)
            {
                if (route.OwnerCharacterId is null && route.IngressGraphId is null)
                {
                    issues.Add(new FlowIssue("binding.route_owner_required", $"/environments/{environment.Environment}/routes/{route.RouteId}", "Routes require a character owner or an ingress graph ID."));
                }

                if (route.OwnerCharacterId is not null && route.IngressGraphId is not null)
                {
                    issues.Add(new FlowIssue("binding.route_owner_ambiguous", $"/environments/{environment.Environment}/routes/{route.RouteId}", "Routes must identify either a character owner or an ingress graph, not both."));
                }

                if (route.OwnerCharacterId is not null && !characters.ContainsKey(route.OwnerCharacterId))
                {
                    issues.Add(new FlowIssue("binding.character_missing", $"/environments/{environment.Environment}/routes/{route.RouteId}", "Route owner character ID does not resolve."));
                }

                if (route.IngressGraphId is not null && workspace.Graphs.All(graph =>
                    !string.Equals(graph.Id, route.IngressGraphId, StringComparison.Ordinal) || graph.Role != GraphRole.Ingress))
                {
                    issues.Add(new FlowIssue("binding.ingress_graph_invalid", $"/environments/{environment.Environment}/routes/{route.RouteId}", "Ingress-owned routes must resolve to an ingress graph."));
                }
            }

            foreach (var space in environment.SpaceBindings)
            {
                var path = $"/environments/{environment.Environment}/spaces/{space.LogicalSpaceId}";
                if (space.Environment != environment.Environment)
                {
                    issues.Add(new FlowIssue("binding.environment_mismatch", path, "Space binding environment does not match its registry entry."));
                }

                if (string.IsNullOrEmpty(space.PhysicalStoreKey))
                {
                    issues.Add(new FlowIssue("binding.physical_store_required", path, "Space bindings require an opaque physical store key."));
                }

                if (space.Scope == FlowSpaceScope.Private && string.IsNullOrEmpty(space.OwnerCharacterId))
                {
                    issues.Add(new FlowIssue("binding.space_owner_required", path, "Private spaces require an owner character ID."));
                }
                else if (space.Scope == FlowSpaceScope.Private && !characters.ContainsKey(space.OwnerCharacterId!))
                {
                    issues.Add(new FlowIssue("binding.space_owner_missing", path, "Private space owner does not resolve."));
                }

                if (space.Scope == FlowSpaceScope.Shared && space.OwnerCharacterId is not null)
                {
                    issues.Add(new FlowIssue("binding.shared_owner_forbidden", path, "Shared spaces must not have a private owner."));
                }

                if (space.Scope == FlowSpaceScope.Shared && space.ExplicitConversationIds.Count == 0)
                {
                    issues.Add(new FlowIssue("binding.shared_conversations_required", path, "Shared spaces require explicit conversation IDs."));
                }

                if (space.Scope == FlowSpaceScope.Private && space.ExplicitConversationIds.Count != 1)
                {
                    issues.Add(new FlowIssue("binding.private_conversation_required", path, "Private spaces require exactly one explicit conversation ID."));
                }
            }

            foreach (var grant in environment.Grants)
            {
                var path = $"/environments/{environment.Environment}/grants/{grant.CharacterId}/{grant.LogicalSpaceId}";
                if (!characters.ContainsKey(grant.CharacterId))
                    issues.Add(new FlowIssue("binding.grant_character_missing", path, "Grant character does not resolve."));
                if (!environment.SpaceBindings.Any(space => string.Equals(space.LogicalSpaceId, grant.LogicalSpaceId, StringComparison.Ordinal)))
                    issues.Add(new FlowIssue("binding.grant_space_missing", path, "Grant space does not resolve in this environment."));
                if (grant.Mode == FlowAccessMode.Mediated && string.IsNullOrWhiteSpace(grant.ProtectorId))
                    issues.Add(new FlowIssue("binding.grant_protector_required", path, "Mediated grants require a protector."));
            }

            var destinationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var destination in environment.Destinations)
            {
                if (!destinationIds.Add(destination.DestinationId))
                    issues.Add(new FlowIssue("binding.destination_duplicate", $"/environments/{environment.Environment}/destinations/{destination.DestinationId}", "Destination IDs must be unique within an environment."));

                if (destination.Environment != environment.Environment)
                {
                    issues.Add(new FlowIssue("binding.environment_mismatch", $"/environments/{environment.Environment}/destinations/{destination.DestinationId}", "Destination binding environment does not match its registry entry."));
                }
            }
        }
    }

    private static void ValidateRoutes(
        IReadOnlyList<EnvironmentBindings> bindings,
        ICollection<FlowIssue> issues)
    {
        var routes = bindings
            .SelectMany(environment => environment.RouteOwners.Select(route => (EnvironmentBinding: environment, Route: route)))
            .GroupBy(item => (item.Route.AccountId, item.Route.ConversationId));

        foreach (var group in routes.Where(group => group.Count() > 1).OrderBy(group => group.Key.AccountId, StringComparer.Ordinal).ThenBy(group => group.Key.ConversationId, StringComparer.Ordinal))
        {
            var entries = group.ToArray();
            var validExplicitDuplication = entries.All(entry =>
            {
                var route = entry.Route;
                return route.ExplicitDuplicate &&
                    !string.IsNullOrEmpty(route.DuplicationGrantId) &&
                    entry.EnvironmentBinding.RouteDuplicationGrants.Any(grant =>
                        string.Equals(grant.GrantId, route.DuplicationGrantId, StringComparison.Ordinal) &&
                        string.Equals(grant.RouteId, route.RouteId, StringComparison.Ordinal) &&
                        string.Equals(grant.CharacterId, route.OwnerCharacterId, StringComparison.Ordinal));
            });
            var distinctRouteIds = entries.Select(entry => entry.Route.RouteId).Distinct(StringComparer.Ordinal).Count() == entries.Length;

            if (!validExplicitDuplication || !distinctRouteIds)
            {
                issues.Add(new FlowIssue("binding.route_ambiguous", $"/routes/{group.Key.AccountId}/{group.Key.ConversationId}", "Each account/conversation must have one route owner unless every duplicate is separately marked and granted."));
            }
        }
    }

    private static void ValidatePhysicalAliases(IReadOnlyList<EnvironmentBindings> bindings, ICollection<FlowIssue> issues)
    {
        var spaces = bindings
            .SelectMany(environment => environment.SpaceBindings.Select(space => (environment.Environment, Space: space)))
            .Where(item => !string.IsNullOrEmpty(item.Space.PhysicalStoreKey))
            .GroupBy(item => item.Space.PhysicalStoreKey, StringComparer.Ordinal);

        foreach (var group in spaces.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var entries = group.ToArray();
            var environments = entries.Select(entry => entry.Environment).Distinct().ToArray();

            if (environments.Length > 1)
            {
                issues.Add(new FlowIssue("binding.cross_environment_alias", $"/physicalStores/{group.Key}", "A physical store key must not be resolved by more than one environment, including shared spaces."));
            }

            var scopes = entries.Select(entry => entry.Space.Scope).Distinct().ToArray();
            if (scopes.Length > 1)
            {
                issues.Add(new FlowIssue("binding.physical_scope_conflict", $"/physicalStores/{group.Key}", "A physical store key must not mix private and shared storage scopes."));
            }

            if (scopes is [FlowSpaceScope.Shared])
            {
                var logicalSpaceIds = entries
                    .Select(entry => entry.Space.LogicalSpaceId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (logicalSpaceIds.Length > 1)
                {
                    issues.Add(new FlowIssue("binding.physical_logical_alias", $"/physicalStores/{group.Key}", "Shared bindings sharing a physical store key must declare the same logical space ID."));
                }

                var conversationSets = entries
                    .Select(entry => string.Join("\u001f", entry.Space.ExplicitConversationIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (conversationSets.Length > 1)
                {
                    issues.Add(new FlowIssue("binding.shared_conversation_alias", $"/physicalStores/{group.Key}", "Repeated shared bindings must declare the same conversation ID set."));
                }
            }
            else if (scopes is [FlowSpaceScope.Private] && entries.Length > 1)
            {
                issues.Add(new FlowIssue("binding.private_physical_duplicate", $"/physicalStores/{group.Key}", "A physical store key may resolve to only one private binding; repeated physical bindings are reserved for identical shared declarations."));
            }

            var privateOwners = entries
                .Where(entry => entry.Space.Scope == FlowSpaceScope.Private)
                .Select(entry => entry.Space.OwnerCharacterId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (privateOwners.Length > 1)
            {
                issues.Add(new FlowIssue("binding.private_physical_alias", $"/physicalStores/{group.Key}", "Private spaces for different characters must not share a physical store key."));
            }

            var privateConversationKeys = entries
                .Where(entry => entry.Space.Scope == FlowSpaceScope.Private)
                .SelectMany(entry => entry.Space.ExplicitConversationIds.Select(conversationId => (entry.Space.OwnerCharacterId, conversationId)))
                .Distinct()
                .ToArray();
            if (privateConversationKeys.Length > 1)
            {
                issues.Add(new FlowIssue("binding.private_conversation_alias", $"/physicalStores/{group.Key}", "Private spaces for different conversations must not share a physical store key."));
            }
        }
    }

    private static void ValidateNodeBindings(
        FlowWorkspaceDefinition workspace,
        IReadOnlyList<EnvironmentBindings> bindings,
        IReadOnlyDictionary<string, CharacterDefinition[]> characters,
        ICollection<FlowIssue> issues)
    {
        foreach (var environment in bindings)
        {
            var spaces = environment.SpaceBindings
                .GroupBy(space => space.LogicalSpaceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var destinations = environment.Destinations
                .GroupBy(destination => destination.DestinationId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            foreach (var graph in workspace.Graphs)
            {
                foreach (var (node, index) in graph.Nodes.Select((node, index) => (node, index)))
                {
                    var path = $"/environments/{environment.Environment}/graphs/{graph.Id}/nodes/{index}";
                    if (TryGetString(node.Config, "environment", out var requestedEnvironment) &&
                        !string.Equals(requestedEnvironment, environment.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new FlowIssue("binding.environment_mismatch", $"{path}/config/environment", "Node environment does not match the validated environment."));
                    }

                    var hasDefinition = TryGetDefinition(node, out var definition);
                    if ((hasDefinition && definition.Effect == FlowEffect.Send) || IsSend(node.Kind))
                    {
                        ValidateDestination(node, path, destinations, issues);
                    }

                    if (hasDefinition)
                    {
                        ValidateCapabilities(definition, environment, path, issues);
                    }

                    if ((hasDefinition && (definition.Effect is FlowEffect.ReadMemory or FlowEffect.WriteMemory or FlowEffect.WriteState)) || IsMemory(node.Kind))
                    {
                        ValidateSpaceAccess(node, graph, path, spaces, environment, characters, issues);
                    }
                }
            }
        }
    }

    private static void ValidateDestination(
        FlowNode node,
        string path,
        IReadOnlyDictionary<string, DestinationBinding[]> destinations,
        ICollection<FlowIssue> issues)
    {
        if (!TryGetString(node.Config, "destinationId", out var destinationId))
        {
            issues.Add(new FlowIssue("binding.destination_required", $"{path}/config/destinationId", "Send nodes require an explicit destination ID."));
            return;
        }

        if (!destinations.ContainsKey(destinationId))
        {
            issues.Add(new FlowIssue("binding.destination_unknown", $"{path}/config/destinationId", "Destination ID does not resolve in the validated environment."));
        }
    }

    private static void ValidateSpaceAccess(
        FlowNode node,
        GraphDocument graph,
        string path,
        IReadOnlyDictionary<string, SpaceBinding[]> spaces,
        EnvironmentBindings environment,
        IReadOnlyDictionary<string, CharacterDefinition[]> characters,
        ICollection<FlowIssue> issues)
    {
        if (!TryGetString(node.Config, "spaceId", out var spaceId))
        {
            issues.Add(new FlowIssue("binding.space_required", $"{path}/config/spaceId", "Memory nodes require an explicit logical space ID."));
            return;
        }

        if (!spaces.TryGetValue(spaceId, out var candidates) || candidates.Length == 0)
        {
            issues.Add(new FlowIssue("binding.space_unknown", $"{path}/config/spaceId", "Space ID does not resolve in the validated environment."));
            return;
        }

        if (candidates.Length > 1)
        {
            issues.Add(new FlowIssue("binding.space_ambiguous", $"{path}/config/spaceId", "Space ID resolves to more than one binding in the validated environment."));
            return;
        }

        var space = candidates[0];
        var caller = graph.CharacterId;
        if (caller is null || !characters.ContainsKey(caller))
        {
            issues.Add(new FlowIssue("binding.character_required", path, "Memory access requires a character graph owner."));
            return;
        }

        var mediated = TryGetString(node.Config, "accessMode", out var accessMode) &&
            string.Equals(accessMode, "mediated", StringComparison.OrdinalIgnoreCase);
        var hasDefinition = TryGetDefinition(node, out var definition);
        var operation = (hasDefinition && (definition.Effect is FlowEffect.WriteMemory or FlowEffect.WriteState)) || IsWriteMemory(node.Kind)
            ? FlowAccessOperation.Write
            : FlowAccessOperation.Read;

        if (mediated)
        {
            if (!TryGetString(node.Config, "protectorId", out var protectorId))
            {
                issues.Add(new FlowIssue("binding.protector_required", $"{path}/config/protectorId", "Mediated access requires an explicit protector ID."));
            }
            else if (!HasGrant(environment.Grants, caller, space.LogicalSpaceId, operation, FlowAccessMode.Mediated, protectorId))
            {
                issues.Add(new FlowIssue("binding.mediated_grant_required", path, "Mediated access requires a matching protector grant."));
            }

            return;
        }

        if (space.Scope == FlowSpaceScope.Private && !string.Equals(space.OwnerCharacterId, caller, StringComparison.Ordinal) &&
            !HasGrant(environment.Grants, caller, space.LogicalSpaceId, operation, FlowAccessMode.Direct, protectorId: null))
        {
            issues.Add(new FlowIssue("binding.direct_grant_required", path, "Direct access to another character's private space requires a grant."));
        }
    }

    private static bool HasGrant(
        IEnumerable<FlowGrant> grants,
        string characterId,
        string spaceId,
        FlowAccessOperation operation,
        FlowAccessMode mode,
        string? protectorId)
    {
        return grants.Any(grant =>
            string.Equals(grant.CharacterId, characterId, StringComparison.Ordinal) &&
            string.Equals(grant.LogicalSpaceId, spaceId, StringComparison.Ordinal) &&
            grant.Operation == operation &&
            grant.Mode == mode &&
            string.Equals(grant.ProtectorId, protectorId, StringComparison.Ordinal));
    }

    private static bool TryGetString(JsonElement config, string propertyName, out string value)
    {
        value = string.Empty;
        if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = element.GetString();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool IsInvocation(string kind) => kind is "invoke" or "character.invoke" or "call-character" or "character-call" or "character-dispatch";
    private static bool IsSend(string kind) => kind is "send" or "destination.send";
    private static bool IsMemory(string kind) => kind is "read-memory" or "write-memory" or "memory.read" or "memory.write" or "history-record" or "history-read" or "memory-learn" or "memory-read" or "state-read" or "state-update";
    private static bool IsWriteMemory(string kind) => kind is "write-memory" or "memory.write" or "history-record" or "memory-learn" or "state-update";

    private static bool TryGetDefinition(FlowNode node, out NodeDefinition definition) =>
        NodeCatalog.V1.TryGet(node.Kind, node.BlockVersion, out definition!);

    private static void ValidateCapabilities(NodeDefinition definition, EnvironmentBindings environment, string path, ICollection<FlowIssue> issues)
    {
        var capability = definition.Effect switch
        {
            FlowEffect.Model => "model",
            FlowEffect.ReadMemory => "memory.read",
            FlowEffect.WriteMemory => "memory.write",
            FlowEffect.WriteState => "state.write",
            FlowEffect.Send => "send",
            FlowEffect.Await => "await",
            _ => null,
        };

        if (capability is not null && !environment.Capabilities.Contains(capability))
        {
            issues.Add(new FlowIssue("binding.capability_missing", $"{path}/capabilities/{capability}", $"Environment does not provide the capability required by {definition.Effect}."));
        }
    }
}
