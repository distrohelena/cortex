using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Memory;
using Filomena.Core.Identity;

namespace Cortex.Core.Flows.Sessions;

internal static class SessionMemoryQuery
{
    internal static async Task<JsonElement> QueryAsync(SessionKey session, TypedNode node, JsonElement? suppliedPersons,
        string? text, BoundaryExecutionRegistry registry, IMemoryReader reader, TimeProvider clock, CancellationToken token)
    {
        var settings = MemoryNodeSettings.Read(node.Source.Config);
        var ns = SessionFlowRuntime.ConversationNamespace(session);
        var persons = new List<MemoryPerson>();
        var values = suppliedPersons is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().ToArray()
            : suppliedPersons is { } scalar ? [scalar] : Array.Empty<JsonElement>();
        foreach (var value in values.Where(v => v.ValueKind != JsonValueKind.Null))
        {
            FlowDomain.Check(value, FlowDomain.Person);
            var person = MemoryIdentity.Person(ns, value.GetProperty("stableId").GetString()!, value.GetProperty("nome").GetString());
            if (value.TryGetProperty("participantUuid", out var uuid) && uuid.GetString() != person.ParticipantUuid.ToString()
                || value.TryGetProperty("conversationNamespace", out var scope) && scope.GetString() != ns) throw new InvalidDataException("Foreign memory person.");
            if (!persons.Any(p => p.ParticipantUuid == person.ParticipantUuid)) persons.Add(person);
        }
        var query = new MemoryQuery(ns, FilomenaIdentity.AgentUuid(ns), settings.Mode, settings.Scope, persons, text, settings.Categories, settings.MaxItems, settings.MaxCharacters);
        MemoryResultPolicy.ValidateQuery(query);
        IReadOnlyList<MemoryItem> candidates;
        try { candidates = await reader.QueryAsync(query, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException) { throw new InvalidDataException("Memory query failed (" + error.GetType().Name + ")."); }
        token.ThrowIfCancellationRequested();
        return registry.RegisterMemories(MemoryResultPolicy.Apply(query, candidates, clock.GetUtcNow()));
    }
}
