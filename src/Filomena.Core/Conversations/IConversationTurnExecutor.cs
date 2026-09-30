using Filomena.Core.Arbitration;

namespace Filomena.Core.Conversations;

public sealed record SentConversationTurn(string ProviderMessageId, string Content, DateTimeOffset SentAt);

public sealed record ConversationGenerationRequest(
    ConversationInfo Conversation,
    IReadOnlyList<InboundConversationEvent> Messages,
    IReadOnlyList<SentConversationTurn> RecentOutbound,
    IReadOnlyList<string> PendingMessageIds,
    TurnDecision Decision)
{
    // Rechecked after generation, before any external send. No provider knowledge.
    public Func<bool>? IsSuperseded { get; init; }
}

public interface IConversationTurnExecutor
{
    Task<SentConversationTurn?> ExecuteAsync(ConversationGenerationRequest request, CancellationToken cancellationToken);
}
