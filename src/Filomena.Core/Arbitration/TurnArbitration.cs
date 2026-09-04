using Filomena.Core.Conversations;

namespace Filomena.Core.Arbitration;

public sealed record TurnSignals(
    bool IsDirectMessage,
    bool IsAllowlistedDirectMessage,
    bool DirectAddressed,
    bool HasQuestion,
    bool HasStructuralReply,
    TimeSpan? SinceLastFilomenaTurn)
{
    public bool HasClearReplyExpectation =>
        DirectAddressed || HasQuestion || HasStructuralReply;
}

public sealed record TurnArbitrationRequest(
    ConversationInfo Conversation,
    IReadOnlyList<InboundConversationEvent> Messages,
    TurnSignals Signals);

public interface ITurnArbiter
{
    Task<TurnDecision> DecideAsync(
        TurnArbitrationRequest request,
        CancellationToken cancellationToken);
}

public static class TurnDecisionPolicy
{
    public static bool IsValid(
        TurnDecision decision,
        TurnArbitrationRequest request)
    {
        if (decision.Decision is not TurnAction.Respond &&
            (decision.AnchorRef is not null || decision.TargetRef is not null))
        {
            return false;
        }

        if (decision.AnchorRef is not null &&
            !request.Messages.Any(message =>
                string.Equals(
                    message.ProviderMessageId,
                    decision.AnchorRef,
                    StringComparison.Ordinal)))
        {
            return false;
        }

        return decision.TargetRef is null || request.Messages.Any(message =>
            string.Equals(
                message.Sender?.StableId,
                decision.TargetRef,
                StringComparison.Ordinal));
    }

    public static TurnDecision InvalidOutputFallback(TurnArbitrationRequest request)
    {
        if (request.Signals.IsDirectMessage &&
            request.Signals.IsAllowlistedDirectMessage &&
            request.Signals.HasClearReplyExpectation &&
            request.Messages.Count > 0)
        {
            var latest = request.Messages[^1];
            return new TurnDecision(
                TurnAction.Respond,
                latest.ProviderMessageId,
                latest.Sender?.StableId,
                TurnReason.ClearDirectMessageExpectation);
        }

        return new TurnDecision(
            TurnAction.Ignore,
            null,
            null,
            TurnReason.InvalidArbiterOutput);
    }
}
