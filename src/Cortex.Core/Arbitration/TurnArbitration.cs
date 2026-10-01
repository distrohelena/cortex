using Cortex.Core.Conversations;

namespace Cortex.Core.Arbitration;

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
    TurnSignals Signals)
{
    public IReadOnlyList<SentConversationTurn> RecentOutbound { get; init; } = [];
    public DirectMessageTiming? DirectMessageTiming { get; init; }
    public IReadOnlyList<string> PendingMessageIds { get; init; } = [];
}

public sealed record DirectMessageTiming(
    double SilenceSeconds,
    double PendingSeconds,
    int RemainingTimedRechecks,
    bool FollowUpAllowed,
    int DefaultRecheckSeconds,
    int MinRecheckSeconds,
    int MaxRecheckSeconds);

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
        TurnArbitrationRequest request) => ValidationError(decision, request) is null;

    public static string? ValidationError(TurnDecision decision, TurnArbitrationRequest request)
    {
        if (request.DirectMessageTiming is not null &&
            ((decision.Decision == TurnAction.Wait && decision.RecheckAfterSeconds is null) ||
             (decision.Decision == TurnAction.Ignore && decision.Reason != TurnReason.ConversationClosed)))
            return "DM wait requires recheckAfterSeconds; DM ignore requires reason=conversation_closed.";
        if (decision.RecheckAfterSeconds is <= 0 ||
            (decision.Decision != TurnAction.Wait && decision.RecheckAfterSeconds is not null))
            return "recheckAfterSeconds must be positive for wait and null for respond/ignore.";
        if (decision.ResponseKind == TurnResponseKind.FollowUp &&
            (decision.Decision != TurnAction.Respond ||
             request.DirectMessageTiming?.FollowUpAllowed != true ||
             !request.Signals.IsAllowlistedDirectMessage))
            return "responseKind=follow_up requires respond and explicit DM follow_up_allowed permission.";
        if (decision.Reason == TurnReason.IncompleteThoughtFollowUp && decision.ResponseKind != TurnResponseKind.FollowUp)
            return "reason=incomplete_thought_follow_up requires responseKind=follow_up.";
        if (decision.Decision is not TurnAction.Respond &&
            (decision.AnchorRef is not null || decision.TargetRef is not null))
        {
            return "anchorRef and targetRef must be null for wait/ignore.";
        }

        if (decision.AnchorRef is not null &&
            !request.Messages.Any(message =>
                string.Equals(
                    message.ProviderMessageId,
                    decision.AnchorRef,
                    StringComparison.Ordinal)))
        {
            return "anchorRef does not belong to this evaluation.";
        }

        return decision.TargetRef is null || request.Messages.Any(message =>
            string.Equals(
                message.Sender?.StableId,
                decision.TargetRef,
                StringComparison.Ordinal)) ? null : "targetRef does not belong to this evaluation.";
    }

    public static TurnDecision InvalidOutputFallback(TurnArbitrationRequest request)
    {
        if (request.Signals.IsDirectMessage &&
            request.Signals.IsAllowlistedDirectMessage &&
            request.Messages.Count > 0)
        {
            return new TurnDecision(TurnAction.Wait, null, null, TurnReason.InvalidArbiterOutput)
            {
                RecheckAfterSeconds = request.DirectMessageTiming?.DefaultRecheckSeconds ?? 15,
            };
        }

        return new TurnDecision(
            TurnAction.Ignore,
            null,
            null,
            TurnReason.InvalidArbiterOutput);
    }
}
