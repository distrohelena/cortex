using System.Text.Json;
using Cortex.Core.Arbitration;
using Cortex.Core.Conversations;
using Cortex.Inference.LmStudio;

namespace Filomena.Llm.Agent;

public sealed class FilomenaAgent(
    IResponseChatClient model,
    IConversationSender sender,
    FilomenaAgentOptions options) : IConversationTurnExecutor
{
    public async Task<SentConversationTurn?> ExecuteAsync(
        ConversationGenerationRequest request, CancellationToken cancellationToken)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Decision.Decision != TurnAction.Respond ||
            !options.AllowedConversationNamespaces.Contains(request.Conversation.Id.Namespace, StringComparer.Ordinal))
            return null;
        if (request.Messages.Any(message => message.Conversation?.Id != request.Conversation.Id))
            throw new InvalidDataException("Cross-conversation generation context rejected.");

        var conversationInstructions = request.Conversation.Kind switch
        {
            ConversationKind.DirectMessage => options.DirectMessageInstructions,
            ConversationKind.Group => options.GroupInstructions,
            _ => throw new InvalidDataException("Unknown conversation kind cannot generate a reply."),
        };
        var systemPrompt = options.SystemPrompt + "\n\n" + conversationInstructions;
        if (request.Decision.ResponseKind == TurnResponseKind.FollowUp)
        {
            if (request.Conversation.Kind != ConversationKind.DirectMessage)
                throw new InvalidDataException("Follow-up generation is only enabled for DM.");
            systemPrompt += "\n\n" + options.FollowUpInstructions;
        }
        var text = (await model.CompleteResponseAsync(systemPrompt, BuildPrompt(request), cancellationToken)).Trim();
        if (text.StartsWith("Filomena:", StringComparison.OrdinalIgnoreCase)) text = text[9..].Trim();
        if (string.IsNullOrWhiteSpace(text) || text is "NO_REPLY" or "HEARTBEAT_OK") return null;
        if (text.Length > options.MaxResponseCharacters)
            throw new InvalidDataException("Generated response exceeded the configured limit.");
        if (request.IsSuperseded?.Invoke() == true) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.SendTimeoutSeconds));
        var receipt = await sender.SendTextAsync(request.Conversation.SessionKey, text, timeout.Token);
        return new SentConversationTurn(receipt.ProviderMessageId, text, DateTimeOffset.UtcNow);
    }

    public string BuildPrompt(ConversationGenerationRequest request)
    {
        // Provider IDs stay in code; local labels disambiguate speakers with identical names.
        var actors = request.Messages.Where(m => m.Sender is not null)
            .Select(m => m.Sender!.StableId).Distinct(StringComparer.Ordinal).ToArray();
        var entries = request.Messages.Select((message, index) =>
        {
            var actorIndex = message.Sender is null ? -1 : Array.IndexOf(actors, message.Sender.StableId);
            var replyIndex = message.ReplyTo is null ? -1 : request.Messages.ToList()
                .FindIndex(item => item.ProviderMessageId == message.ReplyTo.ProviderMessageId);
            return (Time: message.Timestamp ?? DateTimeOffset.MinValue,
                Priority: request.PendingMessageIds.Contains(message.ProviderMessageId) ? 2 : 0,
                Text: JsonSerializer.Serialize(new
                {
                    localMessage = index + 1,
                    speaker = actorIndex < 0 ? "unresolved" : $"person-{actorIndex + 1}",
                    name = message.Sender?.DisplayName,
                    text = message.Content,
                    current = request.PendingMessageIds.Contains(message.ProviderMessageId),
                    anchor = request.Decision.AnchorRef == message.ProviderMessageId,
                    target = message.Sender is not null && request.Decision.TargetRef == message.Sender.StableId,
                    replyToLocalMessage = replyIndex < 0 ? (int?)null : replyIndex + 1,
                }));
        }).Concat(request.RecentOutbound.Select(item => (Time: item.SentAt, Priority: 1,
            Text: JsonSerializer.Serialize(new { speaker = "Filomena", text = item.Content }))));
        const string header = "[CONVERSA OBSERVADA; current=bloco novo; anchor=foco, não quote nativo]\n";
        var lines = new List<(DateTimeOffset Time, string Text)>();
        var remaining = options.ContextCharacterLimit - header.Length;
        foreach (var entry in entries.OrderByDescending(entry => entry.Priority).ThenByDescending(entry => entry.Time))
        {
            if (entry.Text.Length + 1 > remaining)
            {
                if (lines.Count == 0)
                {
                    lines.Add((entry.Time, entry.Text[..Math.Min(entry.Text.Length, remaining)]));
                    break;
                }
                continue;
            }
            lines.Add((entry.Time, entry.Text));
            remaining -= entry.Text.Length + 1;
        }
        return header + string.Join('\n', lines.OrderBy(entry => entry.Time).Select(entry => entry.Text));
    }
}
