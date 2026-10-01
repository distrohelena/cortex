using System.Text.RegularExpressions;
using Cortex.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Cortex.Core.Shadow;
using Cortex.Core.Outbound;

namespace Filomena.App;

public static class NativeObservationHostBinding
{
    public static ShadowSessionBinding CreateManagedMemberDm(ManualShadowConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var nativeId = config.Session.NativeConversationId;
        if (config.Session.ChannelId != "whatsapp"
            || !Regex.IsMatch(nativeId, @"\A[0-9]+@s\.whatsapp\.net\z")
            || config.SelfActorIds.Length != 1
            || !Regex.IsMatch(config.SelfActorIds[0], @"\Awhatsapp:[0-9]+@s\.whatsapp\.net\z")
            || config.SelfActorIds[0] == "whatsapp:" + nativeId
            || config.ConversationNamespace != FilomenaIdentity.WhatsAppConversationNamespace(nativeId)
            || config.PeerActorId != "whatsapp:" + nativeId
            || config.GraphitiProbes.Length != 0)
            throw new ArgumentException("Invalid managed member-DM host binding.");
        return new(config.Session, config.ConversationNamespace, config.SelfActorIds, [config.PeerActorId]);
    }

    // Operational project validation, not a participation/FSM rule. Candidate paths
    // must cross the existing Send boundary before any state-switch exit.
    public static void ValidateGroupSendWiring(PinnedShadowProject pin, OutboundBindings roles)
        => ValidateScopedSendWiring(pin, roles, "group", "Group");

    public static void ValidateManagedMemberDmSendWiring(PinnedShadowProject pin, OutboundBindings roles)
        => ValidateScopedSendWiring(pin, roles, "dm", "Member-DM");

    private static void ValidateScopedSendWiring(PinnedShadowProject pin, OutboundBindings roles,
        string entryKind, string scopeName)
    {
        foreach (var state in pin.Project.Document.GetProperty("states").EnumerateArray())
        {
            var graph = state.GetProperty("graph");
            var nodes = graph.GetProperty("nodes").EnumerateArray()
                .ToDictionary(n => n.GetProperty("id").GetString()!, StringComparer.Ordinal);
            var data = graph.GetProperty("edges").EnumerateArray().ToArray();
            var control = graph.GetProperty("controlEdges").EnumerateArray().ToArray();
            var groupReachable = new HashSet<string>(StringComparer.Ordinal);
            var groupPending = new Queue<string>(nodes.Where(pair => pair.Value.GetProperty("kind").GetString() == entryKind)
                .Select(pair => pair.Key));
            while (groupPending.TryDequeue(out var source))
            {
                if (!groupReachable.Add(source)) continue;
                foreach (var edge in control.Where(edge => edge.GetProperty("source").GetString() == source))
                    groupPending.Enqueue(edge.GetProperty("target").GetString()!);
            }
            foreach (var candidate in roles.CandidatePorts)
            {
                if (!groupReachable.Contains(candidate.Key) || !data.Any(edge =>
                    edge.GetProperty("source").GetString() == candidate.Key
                    && edge.GetProperty("sourcePort").GetString() == candidate.Value
                    && edge.GetProperty("targetPort").GetString() == "message"
                    && nodes[edge.GetProperty("target").GetString()!].GetProperty("kind").GetString() == "send"))
                    throw new InvalidDataException($"{scopeName} candidate has no bound Send node.");
                var pending = new Queue<string>([candidate.Key]);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var reachedSend = false;
                while (pending.TryDequeue(out var id))
                {
                    if (!visited.Add(id)) continue;
                    var node = nodes[id];
                    var kind = node.GetProperty("kind").GetString();
                    if (kind == "send") { reachedSend = true; continue; }
                    if (kind == "exit" && node.TryGetProperty("settings", out var settings)
                        && settings.TryGetProperty("mode", out var mode) && mode.GetString() == "switch")
                        throw new InvalidDataException($"{scopeName} candidate bypasses Send before a transition.");
                    foreach (var edge in control.Where(edge => edge.GetProperty("source").GetString() == id))
                        pending.Enqueue(edge.GetProperty("target").GetString()!);
                }
                if (!reachedSend) throw new InvalidDataException($"{scopeName} Send node is unreachable.");
            }
        }
    }

    public static ShadowSessionBinding Create(ManualShadowConfig config, string nativeConversationId,
        IReadOnlyDictionary<string, string>? participants, bool live, bool allowExactGroupDelivery = false)
    {
        var session = config.Session;
        if (session.ChannelId != "whatsapp" || config.SelfActorIds.Length != 1
            || config.ConversationNamespace != FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId))
            throw new ArgumentException("Invalid trusted conversation binding.");

        var group = session.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal);
        if (allowExactGroupDelivery && (!live || !group))
            throw new ArgumentException("Group delivery authority requires a live group binding.");
        string[] peers;
        if (group)
        {
            if ((live && !allowExactGroupDelivery) || !Regex.IsMatch(session.NativeConversationId, @"\A[0-9]+(-[0-9]+)?@g\.us\z")
                || nativeConversationId != session.NativeConversationId
                || participants is null || participants.Count == 0
                || participants.Any(p => !Regex.IsMatch(p.Key, @"^\d+@(lid|s\.whatsapp\.net)$")
                    || !Regex.IsMatch(p.Value, @"^whatsapp:\d+@s\.whatsapp\.net$")))
                throw new ArgumentException("Invalid group authority.");
            peers = participants.Values.Distinct(StringComparer.Ordinal).ToArray();
            if (!peers.Contains(config.PeerActorId, StringComparer.Ordinal))
                throw new ArgumentException("Unbound primary participant.");
        }
        else
        {
            if (participants is not null
                || session.NativeConversationId != FilomenaIdentity.NormalizeWhatsAppActorNativeId(session.NativeConversationId)
                || !Regex.IsMatch(nativeConversationId, @"^\d+@(lid|s\.whatsapp\.net)$")
                || config.PeerActorId != "whatsapp:" + session.NativeConversationId)
                throw new ArgumentException("Invalid DM authority.");
            peers = [config.PeerActorId];
        }

        // Every canonical actor has both isolation controls, never just the primary DM actor.
        if (config.GraphitiProbes.Length != peers.Length * 2
            || config.GraphitiProbes.Any(p => p.ConversationNamespace != config.ConversationNamespace
                || !peers.Contains(p.ParticipantStableId, StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(p.LocalCanary) || string.IsNullOrWhiteSpace(p.ForeignCanary)
                || string.IsNullOrWhiteSpace(p.ForeignNamespace) || p.ForeignNamespace == config.ConversationNamespace
                || p.LocalCanary == p.ForeignCanary)
            || peers.Any(actor => config.GraphitiProbes.Where(p => p.ParticipantStableId == actor)
                .Select(p => p.ForeignNamespace).Distinct(StringComparer.Ordinal).Count() != 2))
            throw new ArgumentException("Incomplete participant memory preflight binding.");

        return new(session, config.ConversationNamespace, config.SelfActorIds, peers,
            group ? SessionEntryKind.Group : SessionEntryKind.Dm);
    }
}
