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
        return await SessionMemoryQuery.QueryAsync(batch.Session, node, suppliedPersons, text, boundaryRegistry, memoryReader, timeProvider, token,
            (name, duration, metadata) =>
            {
                var item = Trace(batch, name, node.Source.Id, duration: duration) with { MemoryMetadata = metadata };
                if (trace is null) AddTrace(batch.Session, item); else trace(item);
            }).ConfigureAwait(false);
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
