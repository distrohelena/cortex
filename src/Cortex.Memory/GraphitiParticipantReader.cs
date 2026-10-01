using System.Text.Json;
using Cortex.Core.Memory;

namespace Cortex.Memory;

/// <summary>Read-only canonical social presentation. Never establishes transport authority.</summary>
public sealed class GraphitiParticipantReader(IGraphitiReadClient client)
{
    public async Task<MemoryPerson> ResolveAsync(string conversationNamespace, string actor, CancellationToken token)
    {
        var person = MemoryIdentity.Person(conversationNamespace, actor, null);
        var root = GraphitiResponseValidation.Facts(await client.GetEntityNeighborhoodAsync(conversationNamespace,
            person.ParticipantUuid, 1, token), conversationNamespace, expectedParticipant: person.ParticipantUuid);
        if (root.TryGetProperty("status", out _)) return person; // Validated exact absence, not outage.
        if (!root.TryGetProperty("entity", out var entity)
            || Text(entity, "group_id") != conversationNamespace
            || Text(entity, "stable_id") != person.ActorStableId
            || !Guid.TryParse(Text(entity, "uuid"), out var id) || id != person.ParticipantUuid
            || !entity.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array
            || !labels.EnumerateArray().Any(l => l.ValueKind == JsonValueKind.String && l.GetString() == "Person"))
            throw new InvalidDataException("canonical_participant_identity_invalid");
        var name = Text(entity, "display_name_current");
        return person with { DisplayName = string.IsNullOrWhiteSpace(name) ? null : name };
    }

    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
