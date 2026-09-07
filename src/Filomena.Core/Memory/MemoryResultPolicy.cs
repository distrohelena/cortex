using Filomena.Core.Identity;

namespace Filomena.Core.Memory;

public static class MemoryResultPolicy
{
    public static void ValidateQuery(MemoryQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.ConversationNamespace) || query.ConversationNamespace.Contains('\0') ||
            query.AgentUuid != FilomenaIdentity.AgentUuid(query.ConversationNamespace))
            throw new InvalidDataException("Invalid memory conversation identity.");
        if (!query.ActiveOnly || query.MaxItems is < 1 or > 50 || query.MaxCharacters is < 1 or > 100000 ||
            !Enum.IsDefined(query.Mode) || !Enum.IsDefined(query.Scope) || query.Categories.Any(c => !Enum.IsDefined(c)))
            throw new InvalidDataException("Invalid memory query configuration.");
        if (query.Scope == MemoryScope.Person && query.Persons.Count == 0 ||
            query.Mode == MemoryMode.Exact && (query.Scope != MemoryScope.Person || query.Persons.Count == 0) ||
            query.Mode == MemoryMode.Semantic && (query.Scope != MemoryScope.Conversation || string.IsNullOrWhiteSpace(query.Text)) ||
            query.Scope == MemoryScope.Conversation && string.IsNullOrWhiteSpace(query.Text))
            throw new InvalidDataException("Memory mode requires a canonical person or explicit query.");
        foreach (var person in query.Persons) ValidatePerson(query.ConversationNamespace, person);
    }

    public static IReadOnlyList<MemoryItem> Apply(MemoryQuery query, IReadOnlyList<MemoryItem> candidates, DateTimeOffset now)
    {
        ValidateQuery(query);
        foreach (var item in candidates)
        {
            if (item.ConversationNamespace != query.ConversationNamespace)
                throw new InvalidDataException("Memory reader returned a different conversation namespace.");
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Content) || !Enum.IsDefined(item.Category))
                throw new InvalidDataException("Memory reader returned an invalid fact.");
            foreach (var subject in item.Subjects) ValidatePerson(query.ConversationNamespace, subject);
        }
        var requested = query.Persons.Select(p => p.ParticipantUuid).ToHashSet();
        var ordered = candidates.Where(m => m.ExpiredAt is null && (m.InvalidAt is null || m.InvalidAt > now) &&
                (m.ValidAt is null || m.ValidAt <= now) && query.Categories.Contains(m.Category) &&
                (query.Scope != MemoryScope.Person || m.Subjects.Any(s => requested.Contains(s.ParticipantUuid))))
            .OrderBy(m => (int)m.Category).ThenBy(m => m.SourceKind == "exact" ? 0 : 1)
            .ThenBy(m => m.Id, StringComparer.Ordinal).DistinctBy(m => m.Id);
        var result = new List<MemoryItem>();
        var characters = 0;
        foreach (var item in ordered)
        {
            if (result.Count >= query.MaxItems || item.Content.Length > query.MaxCharacters - characters) break;
            result.Add(item);
            characters += item.Content.Length;
        }
        return result;
    }

    private static void ValidatePerson(string conversationNamespace, MemoryPerson person)
    {
        var canonical = MemoryIdentity.Person(conversationNamespace, person.ActorStableId, person.DisplayName);
        if (canonical.ParticipantUuid != person.ParticipantUuid || canonical.ActorStableId != person.ActorStableId)
            throw new InvalidDataException("Participant identity does not belong to the current conversation.");
    }
}
