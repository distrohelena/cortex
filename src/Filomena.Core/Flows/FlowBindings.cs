using System.Collections.Frozen;

namespace Filomena.Core.Flows;

public enum FlowSpaceScope
{
    Private,
    Shared,
}

public enum FlowAccessOperation
{
    Read,
    Write,
}

public enum FlowAccessMode
{
    Direct,
    Mediated,
}

public sealed class FlowWorkspaceDefinition
{
    public FlowWorkspaceDefinition(
        IEnumerable<CharacterDefinition> characters,
        IEnumerable<GraphDocument> graphs,
        IEnumerable<ModelProfileDefinition> modelProfiles,
        IReadOnlyDictionary<string, string>? characterModelProfileReferences = null)
    {
        Characters = characters.ToArray();
        Graphs = graphs.ToArray();
        ModelProfiles = modelProfiles.ToArray();
        CharacterModelProfileReferences = (characterModelProfileReferences ?? new Dictionary<string, string>(StringComparer.Ordinal))
            .ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IReadOnlyList<CharacterDefinition> Characters { get; }
    public IReadOnlyList<GraphDocument> Graphs { get; }
    public IReadOnlyList<ModelProfileDefinition> ModelProfiles { get; }
    public IReadOnlyDictionary<string, string> CharacterModelProfileReferences { get; }
    public IReadOnlyDictionary<string, string> ModelProfileReferences => CharacterModelProfileReferences;
}

public sealed class EnvironmentBindings
{
    public EnvironmentBindings(
        FlowEnvironment environment,
        IEnumerable<RouteOwnerBinding> routeOwners,
        IEnumerable<SpaceBinding> spaceBindings,
        IEnumerable<FlowGrant> grants,
        IEnumerable<string> capabilities,
        IEnumerable<DestinationBinding> destinations,
        IEnumerable<RouteDuplicationGrant>? routeDuplicationGrants = null)
    {
        Environment = environment;
        RouteOwners = routeOwners.ToArray();
        SpaceBindings = spaceBindings.ToArray();
        Grants = grants.ToArray();
        Capabilities = capabilities.ToFrozenSet(StringComparer.Ordinal);
        Destinations = destinations.ToArray();
        RouteDuplicationGrants = (routeDuplicationGrants ?? []).ToArray();
    }

    public FlowEnvironment Environment { get; }
    public IReadOnlyList<RouteOwnerBinding> RouteOwners { get; }
    public IReadOnlyList<SpaceBinding> SpaceBindings { get; }
    public IReadOnlyList<FlowGrant> Grants { get; }
    public IReadOnlySet<string> Capabilities { get; }
    public IReadOnlyList<DestinationBinding> Destinations { get; }
    public IReadOnlyList<RouteDuplicationGrant> RouteDuplicationGrants { get; }
}

public sealed record RouteOwnerBinding(
    string RouteId,
    string AccountId,
    string ConversationId,
    string? OwnerCharacterId,
    bool ExplicitDuplicate = false,
    string? DuplicationGrantId = null,
    string? IngressGraphId = null);

public sealed record RouteDuplicationGrant(
    string GrantId,
    string RouteId,
    string CharacterId);

public sealed class SpaceBinding
{
    public SpaceBinding(
        string logicalSpaceId,
        FlowEnvironment environment,
        FlowSpaceScope scope,
        string? ownerCharacterId,
        IEnumerable<string> explicitConversationIds,
        string physicalStoreKey)
    {
        LogicalSpaceId = logicalSpaceId;
        Environment = environment;
        Scope = scope;
        OwnerCharacterId = ownerCharacterId;
        ExplicitConversationIds = explicitConversationIds.ToArray();
        PhysicalStoreKey = physicalStoreKey;
    }

    public string LogicalSpaceId { get; }
    public FlowEnvironment Environment { get; }
    public FlowSpaceScope Scope { get; }
    public string? OwnerCharacterId { get; }
    public IReadOnlyList<string> ExplicitConversationIds { get; }
    public string PhysicalStoreKey { get; }
}

public sealed record FlowGrant(
    string CharacterId,
    string LogicalSpaceId,
    FlowAccessOperation Operation,
    FlowAccessMode Mode,
    string? ProtectorId = null);

public sealed record DestinationBinding(
    string DestinationId,
    FlowEnvironment Environment,
    string AccountId,
    string ConversationId,
    string? DisplayName = null);
