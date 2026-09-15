using System.Globalization;
using System.Text;
using System.Text.Json;
using Filomena.Core.Identity;
using Filomena.Core.Memory;

namespace Filomena.Memory;

public sealed class GraphitiMemoryReader(IGraphitiReadClient client, TimeProvider? timeProvider = null) : IMemoryReader
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private static readonly HashSet<string> GenericTerms = ["amiga", "amigo", "amigue", "amor", "bonitinha", "bonitinho",
        "darling", "dear", "honey", "meu bem", "minha querida", "meu querido", "querida", "querido", "sweetheart"];

    public async Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken)
    {
        MemoryResultPolicy.ValidateQuery(query);
        var candidates = new List<MemoryItem>();
        if (query.Mode != MemoryMode.Semantic)
            foreach (var person in query.Persons.DistinctBy(p => p.ParticipantUuid))
                Read(await client.GetEntityNeighborhoodAsync(query.ConversationNamespace, person.ParticipantUuid, 50, cancellationToken), "exact", person.ParticipantUuid);
        if (query.Mode != MemoryMode.Exact && !string.IsNullOrWhiteSpace(query.Text))
            Read(await client.SearchFactsAsync(query.ConversationNamespace, query.Text, 50, cancellationToken), "semantic", null);
        return MemoryResultPolicy.Apply(query, candidates, time.GetUtcNow());

        void Read(JsonElement response, string source, Guid? required)
        {
            response = GraphitiResponseValidation.Facts(response, query.ConversationNamespace);
            var facts = response.GetProperty("facts");
            foreach (var fact in facts.EnumerateArray())
            {
                if (Text(fact, "group_id") != query.ConversationNamespace) throw new InvalidDataException("Graphiti returned a different namespace.");
                var subjects = new List<MemoryPerson>();
                var agent = false;
                var safe = Endpoint("source_node", "source_node_uuid") & Endpoint("target_node", "target_node_uuid");
                if (!safe || required is not null && !subjects.Any(p => p.ParticipantUuid == required)) continue;
                if (query.Scope == MemoryScope.Person && !subjects.Any(s => query.Persons.Any(p => p.ParticipantUuid == s.ParticipantUuid))) continue;
                var name = Text(fact, "name");
                var category = subjects.Count == 0 ? MemoryCategory.Unclassified : name switch
                {
                    "REQUESTED_BOUNDARY" or "PREFERS_NOT" or "DISLIKED_INTERACTION" => MemoryCategory.Boundary,
                    _ when agent => MemoryCategory.Relationship,
                    _ => MemoryCategory.Fact
                };
                if (name == "USED_ADDRESS_TERM")
                {
                    if (!agent || subjects.Count != 1 || !EstablishedNickname(fact)) continue;
                    category = MemoryCategory.Nickname;
                }
                var id = Text(fact, "uuid");
                var content = Text(fact, "fact");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("Graphiti fact has no structural ID or content.");
                candidates.Add(new(id, query.ConversationNamespace, content, category,
                    subjects.DistinctBy(p => p.ParticipantUuid).OrderBy(p => p.ParticipantUuid).ToArray(), source,
                    Date(fact, "valid_at"), Date(fact, "invalid_at"), Date(fact, "expired_at"), Strings(fact, "episodes")));

                bool Endpoint(string key, string uuidKey)
                {
                    if (!fact.TryGetProperty(key, out var endpoint) || endpoint.ValueKind != JsonValueKind.Object ||
                        Text(endpoint, "uuid") != Text(fact, uuidKey) || !endpoint.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array) return false;
                    var stable = Text(endpoint, "stable_id");
                    var isPerson = Strings(endpoint, "labels").Contains("Person");
                    var isAgent = Strings(endpoint, "labels").Contains("Agent");
                    if (isAgent || stable == "agent:filomena")
                    {
                        if (stable != "agent:filomena" || !Guid.TryParse(Text(endpoint, "uuid"), out var uuid) || uuid != query.AgentUuid) return false;
                        agent = true;
                        return true;
                    }
                    if (!isPerson && stable is null) return true;
                    if (stable is null) return false;
                    try
                    {
                        var person = MemoryIdentity.Person(query.ConversationNamespace, stable, null);
                        if (!Guid.TryParse(Text(endpoint, "uuid"), out var uuid) || uuid != person.ParticipantUuid) return false;
                        // Social presentation comes from the explicitly connected Person, not a technical node name.
                        subjects.Add(query.Persons.FirstOrDefault(p => p.ParticipantUuid == uuid) ?? person);
                        return true;
                    }
                    catch (InvalidDataException) { return false; }
                }
            }
        }
    }

    private static bool EstablishedNickname(JsonElement fact)
    {
        if (!fact.TryGetProperty("attributes", out var attributes)) return false;
        var term = Text(attributes, "display_term");
        if (string.IsNullOrWhiteSpace(term)) return false;
        var normalized = string.Concat(term.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
        normalized = string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return !GenericTerms.Contains(normalized) && Strings(attributes, "evidence_run_ids").Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(2).Count() >= 2;
    }

    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static string[] Strings(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Array
        ? field.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
    private static DateTimeOffset? Date(JsonElement value, string key)
    {
        if (!value.TryGetProperty(key, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(field.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)) throw new InvalidDataException("Invalid memory temporal metadata.");
        return parsed;
    }
}
