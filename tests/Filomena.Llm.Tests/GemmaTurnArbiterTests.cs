using Filomena.Core.Arbitration;
using Filomena.Core.Conversations;
using Filomena.Llm.Arbitration;
using Filomena.Llm.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GemmaTurnArbiterTests
{
    [Theory]
    [InlineData("m1")]
    [InlineData("p2")]
    [InlineData("whatsapp:5511944558218@s.whatsapp.net")]
    public async Task RejectsWrongOrNonlocalActorRefsAndReportsSafeReason(string target)
    {
        var response = $$"""{"decision":"respond","anchorRef":"m1","targetRef":"{{target}}","reason":"direct_address","recheckAfterSeconds":null,"responseKind":"reply"}""";
        var reports = new List<string>();
        var result = await new GemmaTurnArbiter(new StubClient(response, response), (_, error) => reports.Add(error))
            .DecideAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(TurnReason.InvalidArbiterOutput, result.Reason);
        Assert.Equal(2, reports.Count);
        Assert.All(reports, error => { Assert.Contains("targetRef", error); Assert.DoesNotContain("5511944558218", error); });
    }

    [Fact]
    public async Task UnresolvedSpeakerNeverGetsAnInventedParticipant()
    {
        var request = Request();
        request = request with { Messages = [request.Messages[0] with { Sender = null }] };
        const string response = """{"decision":"respond","anchorRef":"m1","targetRef":"p1","reason":"direct_address","recheckAfterSeconds":null,"responseKind":"reply"}""";
        var result = await new GemmaTurnArbiter(new StubClient(response, response))
            .DecideAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(TurnReason.InvalidArbiterOutput, result.Reason);
        Assert.Empty(new ArbitrationReferences(request).Actors);
    }

    [Fact]
    public async Task UsesLocalReferencesAndTranslatesBackWithoutExposingProviderIds()
    {
        var request = Request();
        request = request with { Messages = [request.Messages[0] with { ProviderMessageId = "3EB01682F4DF27CCFB1B08" }] };
        var client = new StubClient("""{"decision":"respond","anchorRef":"m1","targetRef":"p1","reason":"direct_address","recheckAfterSeconds":null,"responseKind":"reply"}""");
        var result = await new GemmaTurnArbiter(client).DecideAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(TurnAction.Respond, result.Decision);
        Assert.Equal("3EB01682F4DF27CCFB1B08", result.AnchorRef);
        Assert.Equal("whatsapp:5511944558218@s.whatsapp.net", result.TargetRef);
        Assert.DoesNotContain("3EB01682F4DF27CCFB1B08", client.UserPrompts.Single());
        Assert.DoesNotContain("5511944558218", client.UserPrompts.Single());
        Assert.Contains("[actor=p1]", client.UserPrompts.Single());
    }

    [Fact]
    public async Task RetryExplainsWrongReferenceKindAndRespondDelayWithoutGuessingIdentity()
    {
        var client = new StubClient(
            """{"decision":"respond","anchorRef":"m1","targetRef":"m1","reason":"recently_participated","recheckAfterSeconds":5,"responseKind":"reply"}""",
            """{"decision":"respond","anchorRef":"m1","targetRef":"p1","reason":"direct_address","recheckAfterSeconds":null,"responseKind":"reply"}""");
        var result = await new GemmaTurnArbiter(client).DecideAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(TurnAction.Respond, result.Decision);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("targetRef", client.UserPrompts[1]);
        Assert.Contains("recheckAfterSeconds", client.UserPrompts[1]);
        Assert.Contains("null", client.UserPrompts[1]);
    }

    [Fact]
    public async Task DmPromptUsesCompletenessAndParsesSuggestedWait()
    {
        var client = new StubClient("""{"decision":"wait","anchorRef":null,"targetRef":null,"reason":"thought_incomplete","recheckAfterSeconds":40,"responseKind":"reply"}""");
        var request = DmRequest();
        var result = await new GemmaTurnArbiter(client).DecideAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(TurnAction.Wait, result.Decision);
        Assert.Equal(40, result.RecheckAfterSeconds);
        Assert.Equal(TurnReason.ThoughtIncomplete, result.Reason);
        Assert.Contains("pensamento está completo", client.SystemPrompts.Single());
        Assert.DoesNotContain("oportunidade natural para ela entrar", client.SystemPrompts.Single());
        Assert.Contains("silence_seconds=3", client.UserPrompts.Single());
        Assert.Contains("follow_up_allowed=false", client.UserPrompts.Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FollowUpRequiresSchedulerPermission(bool allowed)
    {
        const string response = """{"decision":"respond","anchorRef":"m1","targetRef":null,"reason":"incomplete_thought_follow_up","recheckAfterSeconds":null,"responseKind":"follow_up"}""";
        var client = new StubClient(response, response);
        var request = DmRequest();
        request = request with { DirectMessageTiming = request.DirectMessageTiming! with { FollowUpAllowed = allowed } };
        var decision = await new GemmaTurnArbiter(client).DecideAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(allowed ? TurnAction.Respond : TurnAction.Wait, decision.Decision);
        if (allowed) Assert.Equal(TurnResponseKind.FollowUp, decision.ResponseKind);
    }

    private static TurnArbitrationRequest DmRequest()
    {
        var group = Request();
        return group with
        {
            Conversation = group.Conversation with { Kind = ConversationKind.DirectMessage },
            Signals = group.Signals with { IsDirectMessage = true, IsAllowlistedDirectMessage = true },
            DirectMessageTiming = new(3, 3, 3, false, 15, 5, 120),
        };
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"30\"")]
    [InlineData("null")]
    public async Task InvalidDmWaitDurationFailsClosed(string duration)
    {
        var response = $$"""{"decision":"wait","anchorRef":null,"targetRef":null,"reason":"thought_incomplete","recheckAfterSeconds":{{duration}},"responseKind":"reply"}""";
        var decision = await new GemmaTurnArbiter(new StubClient(response, response))
            .DecideAsync(DmRequest(), TestContext.Current.CancellationToken);
        Assert.Equal(TurnAction.Wait, decision.Decision);
        Assert.Equal(TurnReason.InvalidArbiterOutput, decision.Reason);
        Assert.Equal(15, decision.RecheckAfterSeconds);
    }

    [Fact]
    public async Task DmCannotBeIgnoredForGroupParticipationReasons()
    {
        const string response = """{"decision":"ignore","anchorRef":null,"targetRef":null,"reason":"no_useful_contribution","recheckAfterSeconds":null,"responseKind":"reply"}""";
        var decision = await new GemmaTurnArbiter(new StubClient(response, response))
            .DecideAsync(DmRequest(), TestContext.Current.CancellationToken);
        Assert.Equal(TurnAction.Wait, decision.Decision);
        Assert.Equal(TurnReason.InvalidArbiterOutput, decision.Reason);
    }
    [Fact]
    public async Task ParsesAValidTypedDecisionWithKnownReferences()
    {
        var client = new StubClient("""
            {"decision":"respond","anchorRef":"m1","targetRef":"p1","reason":"direct_address"}
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
    public async Task InvalidOutputForAllowlistedDmWaitsRatherThanGuessingCompleteness()
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

        Assert.Equal(TurnAction.Wait, decision.Decision);
        Assert.Equal(TurnReason.InvalidArbiterOutput, decision.Reason);
        Assert.Null(decision.AnchorRef);
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
