using System.Text.RegularExpressions;
using Filomena.Core.Identity;

namespace Cortex.Core.Memory;

public static partial class MemoryIdentity
{
    public static MemoryPerson Person(string conversationNamespace, string stableId, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(conversationNamespace) || string.IsNullOrWhiteSpace(stableId))
            throw new InvalidDataException("Canonical conversation and actor identities are required.");

        string canonical;
        if (stableId.StartsWith("whatsapp:", StringComparison.Ordinal) &&
            conversationNamespace.StartsWith("whatsapp-", StringComparison.Ordinal))
        {
            try { canonical = FilomenaIdentity.WhatsAppActorStableId(stableId); }
            catch (ArgumentException) { throw new InvalidDataException("Invalid canonical WhatsApp actor."); }
            if (!WhatsAppPerson().IsMatch(canonical))
                throw new InvalidDataException("Actor is not a structurally identified participant.");
        }
        else if (TelegramPerson().IsMatch(stableId) && conversationNamespace.StartsWith("telegram-", StringComparison.Ordinal))
            canonical = stableId;
        else
            throw new InvalidDataException("Unsupported or invalid canonical actor identity.");

        return new(FilomenaIdentity.ParticipantUuid(conversationNamespace, canonical), canonical, displayName);
    }

    [GeneratedRegex(@"^whatsapp:[0-9]+@(s\.whatsapp\.net|lid)$", RegexOptions.CultureInvariant)]
    private static partial Regex WhatsAppPerson();

    [GeneratedRegex(@"^telegram:user:[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TelegramPerson();
}
