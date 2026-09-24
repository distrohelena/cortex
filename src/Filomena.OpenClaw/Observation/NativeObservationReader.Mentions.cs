using System.Text.Json;
using System.Text.RegularExpressions;

namespace Filomena.OpenClaw.Observation;

public sealed partial class NativeObservationReader
{
    private string RenderSelfMentions(JsonElement content, string text)
    {
        if (selfMentionIdentity is not { } self) return text;
        // Only current provider context, never text-shaped IDs or quoted-message mentions.
        foreach (var kind in new[] { "extendedTextMessage", "imageMessage", "videoMessage", "documentMessage" })
        {
            if (!content.TryGetProperty(kind, out var message)
                || !message.TryGetProperty("contextInfo", out var context)
                || !context.TryGetProperty("mentionedJid", out var mentions)) continue;
            if (mentions.ValueKind != JsonValueKind.Array) throw new NativeObservationException("invalid_mentions");
            foreach (var mention in mentions.EnumerateArray())
            {
                if (mention.ValueKind != JsonValueKind.String) throw new NativeObservationException("invalid_mentions");
                var jid = mention.GetString();
                if (jid is null || (jid != self.AccountPn && jid != self.AccountLid)) continue;
                var digits = jid[..jid.IndexOf('@')];
                text = Regex.Replace(text, @"(?<![\p{L}\p{N}_@])@" + Regex.Escape(digits) + @"(?![\p{L}\p{N}_])",
                    _ => "@" + self.DisplayName, RegexOptions.CultureInvariant);
            }
        }
        return text;
    }
}
