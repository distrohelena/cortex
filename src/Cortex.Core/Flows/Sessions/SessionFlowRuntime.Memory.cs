using System.Globalization;
using System.Text.Json;
using Filomena.Core.Identity;
using Cortex.Core.Memory;
using Cortex.Core.Boundaries;

namespace Cortex.Core.Flows.Sessions;

public sealed partial class SessionFlowRuntime
{
    private async Task<JsonElement> QueryMemoryAsync(FlowBatch batch, TypedNode node, JsonElement? suppliedPersons,
        string? text, BoundaryExecutionRegistry boundaryRegistry, CancellationToken token, Action<SessionTraceEvent>? trace = null)
    {
        var settings = MemoryNodeSettings.Read(node.Source.Config);
        var ns = ConversationNamespace(batch.Session);
        var persons = new List<MemoryPerson>();
        var values = suppliedPersons is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().ToArray() :
            suppliedPersons is { } scalar ? [scalar] : Array.Empty<JsonElement>();
        foreach (var value in values.Where(v => v.ValueKind != JsonValueKind.Null))
        {
            FlowDomain.Check(value, FlowDomain.Person);
            var person = MemoryIdentity.Person(ns, value.GetProperty("stableId").GetString()!, value.GetProperty("nome").GetString());
            if (value.TryGetProperty("participantUuid", out var uuid) && uuid.GetString() != person.ParticipantUuid.ToString() ||
                value.TryGetProperty("conversationNamespace", out var scope) && scope.GetString() != ns)
                throw new InvalidDataException("Pessoa pertence a outra conversa ou possui UUID inválido.");
            if (!persons.Any(p => p.ParticipantUuid == person.ParticipantUuid)) persons.Add(person);
        }
        var query = new MemoryQuery(ns, FilomenaIdentity.AgentUuid(ns), settings.Mode, settings.Scope, persons, text,
            settings.Categories, settings.MaxItems, settings.MaxCharacters);
        MemoryResultPolicy.ValidateQuery(query);
        var metadata = new Dictionary<string, string>
        {
            ["mode"] = settings.Mode.ToString(),
            ["scope"] = settings.Scope.ToString(),
            ["participantCount"] = persons.Count.ToString(CultureInfo.InvariantCulture),
            ["requestedLimit"] = settings.MaxItems.ToString(CultureInfo.InvariantCulture)
        };
        void Record(SessionTraceEvent item) { if (trace is null) AddTrace(batch.Session, item); else trace(item); }
        Record(Trace(batch, "MemoryQueryStarted", node.Source.Id) with { MemoryMetadata = new Dictionary<string, string>(metadata) });
        var start = timeProvider.GetTimestamp();
        IReadOnlyList<MemoryItem> candidates;
        try { candidates = await memoryReader.QueryAsync(query, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDataException("Consulta de memória falhou (" + e.GetType().Name + ").");
        }
        token.ThrowIfCancellationRequested();
        var result = MemoryResultPolicy.Apply(query, candidates, timeProvider.GetUtcNow());
        metadata["returnedItemCount"] = result.Count.ToString(CultureInfo.InvariantCulture);
        metadata["categories"] = string.Join(',', result.Select(i => MemoryCategories.Name(i.Category)).Distinct());
        Record(Trace(batch, "MemoryQueryCompleted", node.Source.Id,
            duration: timeProvider.GetElapsedTime(start).TotalMilliseconds) with
        { MemoryMetadata = metadata });
        return boundaryRegistry.RegisterMemories(result);
    }

    internal static string ConversationNamespace(SessionKey session)
    {
        if (session.ChannelId == "whatsapp") return FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId);
        if (session.ChannelId == "telegram" && long.TryParse(session.NativeConversationId, NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var id) && id != 0 && id != long.MinValue)
            return $"telegram-{(id < 0 ? "group-neg" : "dm-pos")}-{Math.Abs(id).ToString(CultureInfo.InvariantCulture)}";
        throw new InvalidDataException("Conversation identity is not supported for memory.");
    }
}
