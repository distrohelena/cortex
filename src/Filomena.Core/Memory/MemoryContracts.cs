namespace Filomena.Core.Memory;

public enum MemoryMode { Exact, Semantic, Hybrid }
public enum MemoryScope { Person, Conversation }
public enum MemoryCategory { Boundary, Nickname, Relationship, Fact, Unclassified }

public sealed record MemoryPerson(Guid ParticipantUuid, string ActorStableId, string? DisplayName);

public sealed record MemoryQuery(
    string ConversationNamespace,
    Guid AgentUuid,
    MemoryMode Mode,
    MemoryScope Scope,
    IReadOnlyList<MemoryPerson> Persons,
    string? Text,
    IReadOnlyList<MemoryCategory> Categories,
    int MaxItems,
    int MaxCharacters,
    bool ActiveOnly = true);

public sealed record MemoryItem(
    string Id,
    string ConversationNamespace,
    string Content,
    MemoryCategory Category,
    IReadOnlyList<MemoryPerson> Subjects,
    string SourceKind,
    DateTimeOffset? ValidAt = null,
    DateTimeOffset? InvalidAt = null,
    DateTimeOffset? ExpiredAt = null,
    IReadOnlyList<string>? EvidenceEpisodeIds = null);

public interface IMemoryReader
{
    Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken);
}

public sealed class UnavailableMemoryReader : IMemoryReader
{
    public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Memory reader is not configured.");
}

public static class MemoryCategories
{
    public static string Name(MemoryCategory category) => category switch
    {
        MemoryCategory.Boundary => "limite",
        MemoryCategory.Nickname => "apelido",
        MemoryCategory.Relationship => "relacao",
        MemoryCategory.Fact => "fato",
        MemoryCategory.Unclassified => "nao_classificada",
        _ => throw new InvalidDataException("Unknown memory category.")
    };

    public static MemoryCategory Parse(string name) => name switch
    {
        "limite" => MemoryCategory.Boundary,
        "apelido" => MemoryCategory.Nickname,
        "relacao" => MemoryCategory.Relationship,
        "fato" => MemoryCategory.Fact,
        "nao_classificada" => MemoryCategory.Unclassified,
        _ => throw new InvalidDataException("Unknown memory category.")
    };
}
