using Filomena.Core.Arbitration;
using Filomena.Core.Conversations;
using Filomena.Llm.Arbitration;
using Filomena.Llm.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GemmaTurnArbiterTests
{
    [Fact]
    public async Task ParsesAValidTypedDecisionWithKnownReferences()
    {
        var client = new StubClient("""
            {"decision":"respond","anchorRef":"m1","targetRef":"whatsapp:5511944558218@s.whatsapp.net","reason":"direct_address"}
            """);
        var arbiter = new GemmaTurnArbiter(client);

        var decision = await arbiter.DecideAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TurnAction.Respond, decision.Decision);
        Assert.Equal("m1", decision.AnchorRef);
        Assert.Equal(TurnReason.DirectAddress, decision.Reason);
        Assert.Equal(1, client.CallCount);
        Assert.Contains("Filomena participa como uma pessoa confiante", client.SystemPrompts.Single());
        Assert.Contains("[ref=m1]", client.UserPrompts.Single());
    }

    [Fact]
    public async Task RetriesMalformedOutputOnceAndAcceptsSecondResult()
    {
        var client = new StubClient(
            "isto não é json",
            """{"decision":"ignore","anchorRef":null,"targetRef":null,"reason":"no_useful_contribution"}""");
        var arbiter = new GemmaTurnArbiter(client);

        var decision = await arbiter.DecideAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TurnAction.Ignore, decision.Decision);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task RejectsUnknownAnchorThenFailsClosedForGroup()
    {
        var invalid = """
            {"decision":"respond","anchorRef":"invented","targetRef":null,"reason":"direct_address"}
            """;
        var client = new StubClient(invalid, invalid);
        var arbiter = new GemmaTurnArbiter(client);

        var decision = await arbiter.DecideAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TurnAction.Ignore, decision.Decision);
        Assert.Equal(TurnReason.InvalidArbiterOutput, decision.Reason);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task InvalidOutputForClearlyAddressedAllowlistedDmFallsBackToRespond()
    {
        var client = new StubClient("bad", "still bad");
        var arbiter = new GemmaTurnArbiter(client);
        var groupRequest = Request();
        var dm = groupRequest.Conversation with
        {
            Id = new ConversationId(
                "whatsapp",
                "default",
                "5511944558218@s.whatsapp.net",
                "whatsapp-dm-5511944558218-s-whatsapp-net"),
            Kind = ConversationKind.DirectMessage,
        };
        var request = groupRequest with
        {
            Conversation = dm,
            Signals = groupRequest.Signals with
            {
                IsDirectMessage = true,
                IsAllowlistedDirectMessage = true,
            },
        };

        var decision = await arbiter.DecideAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(TurnAction.Respond, decision.Decision);
        Assert.Equal(TurnReason.ClearDirectMessageExpectation, decision.Reason);
        Assert.Equal("m1", decision.AnchorRef);
    }

    private static TurnArbitrationRequest Request()
    {
        var conversation = new ConversationInfo(
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
        var actor = new ActorRef(
            "whatsapp",
            "default",
            "5511944558218@s.whatsapp.net",
            "whatsapp:5511944558218@s.whatsapp.net",
            "Helena");
        var message = new InboundConversationEvent(
            1,
            conversation.SessionKey,
            conversation,
            "m1",
            "@Filomena, você acha que funciona?",
            actor,
            DateTimeOffset.Parse("2026-09-04T18:00:00Z"),
            null,
            null,
            new Dictionary<string, object?>());
        return new TurnArbitrationRequest(
            conversation,
            [message],
            new TurnSignals(
                IsDirectMessage: false,
                IsAllowlistedDirectMessage: false,
                DirectAddressed: true,
                HasQuestion: true,
                HasStructuralReply: false,
                SinceLastFilomenaTurn: null));
    }

    private sealed class StubClient(params string[] responses) : ILmStudioChatClient
    {
        private readonly Queue<string> responses = new(responses);

        public int CallCount { get; private set; }

        public List<string> SystemPrompts { get; } = [];

        public List<string> UserPrompts { get; } = [];

        public Task<string> CompleteArbitrationJsonAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            SystemPrompts.Add(systemPrompt);
            UserPrompts.Add(userPrompt);
            return Task.FromResult(responses.Dequeue());
        }
    }
}
