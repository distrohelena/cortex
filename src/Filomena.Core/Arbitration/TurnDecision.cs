namespace Filomena.Core.Arbitration;

public enum TurnAction
{
    Respond,
    Wait,
    Ignore,
}

public enum TurnReason
{
    DirectAddress,
    StructuralReply,
    UsefulContribution,
    NaturalSocialOpportunity,
    ConversationStillFlowing,
    RecentlyParticipated,
    NoUsefulContribution,
    ClearDirectMessageExpectation,
    InvalidArbiterOutput,
    WaitLimitReached,
    ThoughtComplete,
    ThoughtIncomplete,
    WaitingForContinuation,
    IncompleteThoughtFollowUp,
    ConversationClosed,
}

public enum TurnResponseKind { Reply, FollowUp }

public sealed record TurnDecision(
    TurnAction Decision,
    string? AnchorRef,
    string? TargetRef,
    TurnReason Reason)
{
    public int? RecheckAfterSeconds { get; init; }
    public TurnResponseKind ResponseKind { get; init; } = TurnResponseKind.Reply;
}
