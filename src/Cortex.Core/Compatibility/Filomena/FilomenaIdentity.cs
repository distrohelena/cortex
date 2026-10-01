using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Filomena.Core.Identity;

public static partial class FilomenaIdentity
{
    public static readonly Guid InboundEpisodeNamespaceV1 = Guid.Parse("b7897b2d-9c72-5ea9-8f1f-19b097cd37f8");
    public static readonly Guid ParticipantNamespaceV1 = Guid.Parse("cc5bf3bb-68cb-5e0e-a63e-cf8e76cf5f8b");
    public static readonly Guid AgentNamespaceV1 = Guid.Parse("409fb013-87b0-5ac0-a152-e07b7e6074a0");
    public static readonly Guid OutboundEpisodeNamespaceV1 = Guid.Parse("7e5bb18f-2c95-582d-9107-a809ddd3759c");

    public static string WhatsAppConversationNamespace(string nativeConversationId, string? accountId = null)
    {
        _ = accountId;
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeConversationId);

        var raw = RemoveWhatsAppPrefix(nativeConversationId);
        var kind = raw.EndsWith("@g.us", StringComparison.Ordinal) ? "group" : "dm";
        var canonicalId = kind == "group" ? raw : NormalizeWhatsAppActorNativeId(raw);

        return $"whatsapp-{kind}-{LegacyNamespaceSegment(canonicalId)}";
    }

    public static string NormalizeWhatsAppActorNativeId(string nativeActorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeActorId);

        var raw = RemoveWhatsAppPrefix(nativeActorId);
        if (DigitsOnlyPattern().IsMatch(raw))
        {
            var digits = NonDigitPattern().Replace(raw, string.Empty);
            if (digits.Length is >= 7 and <= 15)
            {
                return $"{digits}@s.whatsapp.net";
            }
        }

        var separator = raw.LastIndexOf('@');
        if (separator <= 0 || separator == raw.Length - 1)
        {
            throw new ArgumentException("WhatsApp actor ID is not structurally valid.", nameof(nativeActorId));
        }

        var local = DeviceSuffixPattern().Replace(raw[..separator], string.Empty);
        var domain = raw[(separator + 1)..] == "c.us" ? "s.whatsapp.net" : raw[(separator + 1)..];
        if (!WhatsAppLocalPattern().IsMatch(local) || !WhatsAppDomainPattern().IsMatch(domain))
        {
            throw new ArgumentException("WhatsApp actor ID is not structurally valid.", nameof(nativeActorId));
        }

        return $"{local}@{domain}";
    }

    public static string WhatsAppActorStableId(string nativeActorId) =>
        $"whatsapp:{NormalizeWhatsAppActorNativeId(nativeActorId)}";

    public static Guid ParticipantUuid(string conversationNamespace, string actorStableId) =>
        UuidV5(ParticipantNamespaceV1, $"{conversationNamespace}\0{actorStableId}");

    public static Guid AgentUuid(string conversationNamespace) =>
        UuidV5(AgentNamespaceV1, $"{conversationNamespace}\0agent:filomena");

    public static Guid InboundEpisodeUuid(string channel, string nativeConversationId, string providerMessageId) =>
        UuidV5(InboundEpisodeNamespaceV1, $"{channel}\0{nativeConversationId}\0{providerMessageId}");

    public static Guid OutboundEpisodeUuid(string channel, string nativeConversationId, string providerMessageId) =>
        UuidV5(OutboundEpisodeNamespaceV1, $"{channel}\0{nativeConversationId}\0{providerMessageId}");

    public static Guid UuidV5(Guid namespaceId, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes, bigEndian: true, out _);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var input = new byte[namespaceBytes.Length + keyBytes.Length];
        namespaceBytes.CopyTo(input);
        keyBytes.CopyTo(input.AsSpan(namespaceBytes.Length));

        Span<byte> digest = stackalloc byte[20];
        SHA1.HashData(input, digest);
        digest[6] = (byte)((digest[6] & 0x0f) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3f) | 0x80);

        return Guid.ParseExact(Convert.ToHexString(digest[..16]), "N");
    }

    private static string RemoveWhatsAppPrefix(string value)
    {
        var raw = value.Trim().ToLowerInvariant();
        return raw.StartsWith("whatsapp:", StringComparison.Ordinal)
            ? raw["whatsapp:".Length..]
            : raw;
    }

    private static string LegacyNamespaceSegment(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var withoutMarks = string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
        var normalized = InvalidNamespaceCharacterPattern().Replace(withoutMarks.ToLowerInvariant(), "-");
        normalized = RepeatedHyphenPattern().Replace(normalized, "-").Trim('-', '_');
        return string.IsNullOrEmpty(normalized)
            ? throw new ArgumentException("Conversation ID produces an empty namespace segment.", nameof(value))
            : normalized;
    }

    [GeneratedRegex(@"^\+?[\d\s().-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsOnlyPattern();

    [GeneratedRegex(@"\D", RegexOptions.CultureInvariant)]
    private static partial Regex NonDigitPattern();

    [GeneratedRegex(@":\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceSuffixPattern();

    [GeneratedRegex(@"^[a-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WhatsAppLocalPattern();

    [GeneratedRegex(@"^[a-z0-9.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WhatsAppDomainPattern();

    [GeneratedRegex(@"[^a-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidNamespaceCharacterPattern();

    [GeneratedRegex(@"-+", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedHyphenPattern();
}
