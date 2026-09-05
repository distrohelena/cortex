using System.Text;
using Filomena.Core.Arbitration;

namespace Filomena.Llm.Arbitration;

public static class ArbiterPrompt
{
    public const string System = """
        Filomena participa como uma pessoa confiante em uma conversa. Decida apenas se existe uma oportunidade natural para ela entrar agora.

        RESPOND quando ela foi claramente chamada, recebeu uma pergunta, tem conhecimento útil relevante, tem uma opinião realmente interessante, há uma oportunidade natural de humor/provocação, ou existe continuidade clara de algo que ela discutia.
        WAIT quando as pessoas ainda estão enviando um bloco rápido e observar um pouco mais melhora a resposta.
        IGNORE quando a contribuição só repetiria o que foi dito, interromperia artificialmente, monopolizaria a conversa, ou entraria em conflito alheio sem convite.

        Menção e reply são sinais sociais fortes, não obrigações binárias. Em DM autorizado existe expectativa normal de resposta, mas mensagens rápidas devem ser agregadas. Não gere a resposta da Filomena. Retorne somente o JSON solicitado. Use apenas refs e actor IDs presentes no contexto; nunca invente identidade ou referência.
        """;

    public static string BuildUser(TurnArbitrationRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"conversation_kind={request.Conversation.Kind}");
        builder.AppendLine($"allowlisted_dm={request.Signals.IsAllowlistedDirectMessage.ToString().ToLowerInvariant()}");
        builder.AppendLine($"direct_address={request.Signals.DirectAddressed.ToString().ToLowerInvariant()}");
        builder.AppendLine($"question={request.Signals.HasQuestion.ToString().ToLowerInvariant()}");
        builder.AppendLine($"reply_to_filomena={request.Signals.HasStructuralReply.ToString().ToLowerInvariant()}");
        builder.AppendLine($"since_filomena_seconds={Seconds(request.Signals.SinceLastFilomenaTurn)}");
        builder.AppendLine("recent_chat:");
        foreach (var message in request.Messages)
        {
            var actor = message.Sender?.StableId ?? "unresolved-actor";
            var name = message.Sender?.DisplayName ?? actor;
            var reply = message.ReplyTo is null
                ? string.Empty
                : $" reply_to={message.ReplyTo.ProviderMessageId}";
            builder.Append("[ref=").Append(message.ProviderMessageId).Append("] ")
                .Append("[actor=").Append(actor).Append(']')
                .Append(reply).Append(' ')
                .Append(name).Append(": ")
                .AppendLine(message.Content);
        }

        if (request.RecentOutbound.Count > 0)
        {
            builder.AppendLine("recent_confirmed_filomena_messages:");
            var remaining = 800;
            foreach (var sent in request.RecentOutbound.Reverse())
            {
                if (remaining <= 0) break;
                var text = sent.Content[..Math.Min(sent.Content.Length, remaining)];
                builder.AppendLine(global::System.Text.Json.JsonSerializer.Serialize(new { sentAt = sent.SentAt, text }));
                remaining -= text.Length;
            }
        }
        return builder.ToString();
    }

    private static string Seconds(TimeSpan? value) =>
        value is null ? "unknown" : Math.Max(0, value.Value.TotalSeconds).ToString("F0", global::System.Globalization.CultureInfo.InvariantCulture);
}
