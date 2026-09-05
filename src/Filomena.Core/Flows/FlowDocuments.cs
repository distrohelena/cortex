using System.Text.Json;

namespace Filomena.Core.Flows;

public enum GraphRole
{
    Ingress,
    Character,
}

public enum FlowEnvironment
{
    Testnet,
    Mainnet,
}

public enum JoinMode
{
    All,
    FirstValid,
    Independent,
}

public enum FlowEffect
{
    Pure,
    Model,
    ReadMemory,
    WriteMemory,
    WriteState,
    Send,
    Await,
}

public sealed record GraphDocument
{
    public GraphDocument(
        string id,
        int formatVersion,
        GraphRole role,
        string? characterId,
        IEnumerable<FlowNode> nodes,
        IEnumerable<DataEdge> dataEdges,
        IEnumerable<ControlEdge> controlEdges,
        IEnumerable<string>? entryNodeIds = null)
    {
        Id = id;
        FormatVersion = formatVersion;
        Role = role;
        CharacterId = characterId;
        Nodes = nodes.ToArray();
        DataEdges = dataEdges.ToArray();
        ControlEdges = controlEdges.ToArray();
        EntryNodeIds = (entryNodeIds ?? []).ToArray();
    }

    public string Id { get; }
    public int FormatVersion { get; }
    public GraphRole Role { get; }
    public string? CharacterId { get; }
    public IReadOnlyList<FlowNode> Nodes { get; }
    public IReadOnlyList<DataEdge> DataEdges { get; }
    public IReadOnlyList<ControlEdge> ControlEdges { get; }
    public IReadOnlyList<string> EntryNodeIds { get; }
}

public sealed record FlowNode
{
    public FlowNode(string id, string kind, int blockVersion, JsonElement config)
    {
        Id = id;
        Kind = kind;
        BlockVersion = blockVersion;
        Config = config.Clone();
    }

    public string Id { get; }
    public string Kind { get; }
    public int BlockVersion { get; }
    public JsonElement Config { get; }
}

public sealed record PortRef(string NodeId, string Port);

public sealed record DataEdge(PortRef Source, IReadOnlyList<string> FieldPath, PortRef Target)
{
    public DataEdge(PortRef source, IEnumerable<string> fieldPath, PortRef target)
        : this(source, fieldPath.ToArray(), target)
    {
    }
}

public sealed record ControlEdge(PortRef Source, PortRef Target);

public sealed record CharacterDefinition(string Id, string DisplayName, string Personality, string GraphId);

public sealed record ModelProfileDefinition(string ProfileId, string CredentialReference);
