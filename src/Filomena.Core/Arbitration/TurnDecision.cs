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
}

public sealed record TurnDecision(
    TurnAction Decision,
    string? AnchorRef,
    string? TargetRef,
    TurnReason Reason);
