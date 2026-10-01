using Cortex.Core.Arbitration;
using Cortex.Core.Conversations;
using Xunit;

namespace Cortex.Core.Tests;

public sealed class TurnArbitrationTests
{
    private static readonly ConversationInfo Group = new(
        new ConversationId(
            "whatsapp",
            "default",
            "5511981042361-1589823314@g.us",
            "whatsapp-group-5511981042361-1589823314-g-us"),
        "agent:main:whatsapp:group:5511981042361-1589823314@g.us",
        ConversationKind.Group,
        "Alunos de Drift do Fiuk™",
        null,
        null,
        null,
        null);

    private static readonly ActorRef Helena = new(
        "whatsapp",
        "default",
        "5511944558218@s.whatsapp.net",
        "whatsapp:5511944558218@s.whatsapp.net",
        "Helena");

    [Fact]
    public void AcceptsRespondDecisionWhoseReferencesBelongToContext()
    {
        var request = Request(Group, isAllowlistedDirectMessage: false, directAddressed: true);
        var decision = new TurnDecision(
            TurnAction.Respond,
            "message-1",
            Helena.StableId,
            TurnReason.DirectAddress);

        Assert.True(TurnDecisionPolicy.IsValid(decision, request));
    }

    [Theory]
    [InlineData("other-message", "whatsapp:5511944558218@s.whatsapp.net")]
    [InlineData("message-1", "whatsapp:5511888888888@s.whatsapp.net")]
    public void RejectsReferencesThatDoNotBelongToContext(string anchor, string target)
    {
        var request = Request(Group, isAllowlistedDirectMessage: false, directAddressed: true);
        var decision = new TurnDecision(
            TurnAction.Respond,
            anchor,
            target,
            TurnReason.DirectAddress);

        Assert.False(TurnDecisionPolicy.IsValid(decision, request));
    }

    [Fact]
    public void InvalidGroupOutputFailsClosedToIgnore()
    {
        var request = Request(Group, isAllowlistedDirectMessage: false, directAddressed: true);

        var fallback = TurnDecisionPolicy.InvalidOutputFallback(request);

        Assert.Equal(TurnAction.Ignore, fallback.Decision);
        Assert.Equal(TurnReason.InvalidArbiterOutput, fallback.Reason);
    }

    [Fact]
    public void InvalidOutputForAllowlistedDmWaitsWithoutGuessingCompleteness()
    {
        var dm = Group with
        {
            Id = new ConversationId(
                "whatsapp",
                "default",
                "5511944558218@s.whatsapp.net",
                "whatsapp-dm-5511944558218-s-whatsapp-net"),
            Kind = ConversationKind.DirectMessage,
        };
        var request = Request(dm, isAllowlistedDirectMessage: true, directAddressed: false);

        var fallback = TurnDecisionPolicy.InvalidOutputFallback(request);

        Assert.Equal(TurnAction.Wait, fallback.Decision);
        Assert.Null(fallback.AnchorRef);
        Assert.Null(fallback.TargetRef);
        Assert.Equal(TurnReason.InvalidArbiterOutput, fallback.Reason);
    }

    [Fact]
    public void AllowlistedDmWithoutClearReplyExpectationStillFailsClosed()
    {
        var dm = Group with { Kind = ConversationKind.DirectMessage };
        var request = new TurnArbitrationRequest(
            dm,
            [Message("sem pergunta nem chamada")],
            new TurnSignals(
                IsDirectMessage: true,
                IsAllowlistedDirectMessage: true,
                DirectAddressed: false,
                HasQuestion: false,
                HasStructuralReply: false,
                SinceLastFilomenaTurn: null));

        Assert.Equal(TurnAction.Wait, TurnDecisionPolicy.InvalidOutputFallback(request).Decision);
    }

    private static TurnArbitrationRequest Request(
        ConversationInfo conversation,
        bool isAllowlistedDirectMessage,
        bool directAddressed) =>
        new(
            conversation,
            [Message("@Filomena, funciona?")],
            new TurnSignals(
                conversation.Kind == ConversationKind.DirectMessage,
                isAllowlistedDirectMessage,
                directAddressed,
                HasQuestion: true,
                HasStructuralReply: false,
                SinceLastFilomenaTurn: null));

    private static InboundConversationEvent Message(string content) => new(
        1,
        Group.SessionKey,
        Group,
        "message-1",
        content,
        Helena,
        DateTimeOffset.Parse("2026-09-04T18:00:00Z"),
        null,
        null,
        new Dictionary<string, object?>());
}
