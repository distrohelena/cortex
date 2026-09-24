using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Shadow;
using Filomena.Memory;

namespace Filomena.App;

public static class NativeInboundContext
{
    public static async Task<CanonicalShadowInbound> EnrichAsync(CanonicalShadowInbound input,
        ShadowSessionBinding binding, GraphitiParticipantReader participants, CancellationToken token)
    {
        if (!binding.Accepts(input)) throw new InvalidDataException("identity_enrichment_binding_mismatch");
        if (input.FromMe || input.SkipNonText || binding.IsSelf(input.ActorId)) return input;
        var person = await participants.ResolveAsync(binding.ConversationNamespace, input.ActorId, token);
        var message = JsonNode.Parse(input.Message.Message.GetRawText())!;
        message["remetente"]!["nome"] = person.DisplayName ?? "";
        if (message["reply"] is JsonObject quote && quote["remetente"] is JsonObject sender
            && sender["stableId"]?.GetValue<string>() is { } quotedActor && binding.IsPeer(quotedActor))
        {
            var quotedPerson = quotedActor == input.ActorId ? person
                : await participants.ResolveAsync(binding.ConversationNamespace, quotedActor, token);
            sender["nome"] = quotedPerson.DisplayName ?? "";
        }
        return input with { Message = input.Message with { Message = JsonSerializer.SerializeToElement(message) } };
    }
}
