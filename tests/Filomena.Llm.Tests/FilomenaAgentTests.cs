using Filomena.Core.Arbitration;
using Filomena.Core.Conversations;
using Filomena.Llm.Agent;
using Filomena.Llm.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class FilomenaAgentTests
{
    [Fact]
    public async Task GeneratesAndSendsExactlyOnceToTheObservedRoute()
    {
        var io = new FakeIo();
        var agent = Create(io);
        var sent = await agent.ExecuteAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal("sent-1", sent?.ProviderMessageId);
        Assert.Equal("Filomena: Tudo bem!", io.SentText);
        Assert.Equal("opaque-route", io.Route);
        Assert.Equal(1, io.GenerationCount);
        Assert.Equal(1, io.SendCount);
        Assert.DoesNotContain("whatsapp:", io.Prompt);
        Assert.Contains("como vai?", io.Prompt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("NO_REPLY")]
    [InlineData("HEARTBEAT_OK")]
    [InlineData("Filomena: NO_REPLY")]
    public async Task NeverSendsEmptyOrSilentTokens(string response)
    {
        var io = new FakeIo { Response = response };
        Assert.Null(await Create(io).ExecuteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(0, io.SendCount);
    }

    [Theory]
    [InlineData(TurnAction.Ignore)]
    [InlineData(TurnAction.Wait)]
    public async Task NonRespondNeverGenerates(TurnAction action)
    {
        var io = new FakeIo();
        var request = Request() with { Decision = new(action, null, null, TurnReason.NoUsefulContribution) };
        Assert.Null(await Create(io).ExecuteAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(0, io.GenerationCount);
    }

    [Fact]
    public async Task UnknownConversationNeverGeneratesOrSends()
    {
        var io = new FakeIo();
        var request = Request();
        request = request with { Conversation = request.Conversation with { Id = request.Conversation.Id with { Namespace = "other" } } };
        Assert.Null(await Create(io).ExecuteAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(0, io.GenerationCount);
    }

    [Fact]
    public async Task UncertainSendIsNotRetried()
    {
        var io = new FakeIo { FailSend = true };
        await Assert.ThrowsAsync<IOException>(() => Create(io).ExecuteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(1, io.SendCount);
    }

    private static FilomenaAgent Create(FakeIo io) => new(io, io, new FilomenaAgentOptions
    {
        AllowedConversationNamespaces = ["test-group"],
    });

    [Fact]
    public void PromptBudgetPreservesCurrentInboundAheadOfLongPriorOutbound()
    {
        var io = new FakeIo();
        var agent = new FilomenaAgent(io, io, new FilomenaAgentOptions { ContextCharacterLimit = 512 });
        var request = Request() with
        {
            RecentOutbound = [new("old-send", new string('x', 4000), DateTimeOffset.UtcNow.AddSeconds(1))],
        };
        var prompt = agent.BuildPrompt(request);
        Assert.True(prompt.Length <= 512);
        Assert.Contains("como vai?", prompt);
    }

    [Fact]
    public async Task MixedConversationContextFailsBeforeModelCall()
    {
        var io = new FakeIo();
        var request = Request();
        var other = request.Messages[0] with { Conversation = request.Conversation with { Id = request.Conversation.Id with { Namespace = "private" } } };
        request = request with { Messages = [other] };
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(io).ExecuteAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(0, io.GenerationCount);
    }

    [Fact]
    public async Task CancellationPreventsGenerationAndSending()
    {
        var io = new FakeIo();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(io).ExecuteAsync(Request(), cancellation.Token));
        Assert.Equal(0, io.GenerationCount);
        Assert.Equal(0, io.SendCount);
    }

    [Fact]
    public async Task OversizeResponseIsNotSent()
    {
        var io = new FakeIo { Response = new string('x', 4000) };
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(io).ExecuteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(0, io.SendCount);
    }

    private static ConversationGenerationRequest Request()
    {
        var conversation = new ConversationInfo(new("whatsapp", "default", "group@g.us", "test-group"), "opaque-route",
            ConversationKind.Group, "Group", null, null, null, null);
        var message = new InboundConversationEvent(0, "opaque-route", conversation, "m1", "como vai?",
            new("whatsapp", "default", "123@s.whatsapp.net", "whatsapp:123@s.whatsapp.net", "Helena"),
            DateTimeOffset.UtcNow, null, null, new Dictionary<string, object?>());
        return new(conversation, [message], [], ["m1"], new(TurnAction.Respond, "m1", message.Sender!.StableId, TurnReason.DirectAddress));
    }

    private sealed class FakeIo : IResponseChatClient, IConversationSender
    {
        public string Response { get; init; } = "Tudo bem!";
        public bool FailSend { get; init; }
        public int GenerationCount { get; private set; }
        public int SendCount { get; private set; }
        public string? Route { get; private set; }
        public string? SentText { get; private set; }
        public string Prompt { get; private set; } = "";
        public Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            GenerationCount++;
            Prompt = userPrompt;
            return Task.FromResult(Response);
        }
        public Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken)
        {
            SendCount++;
            Route = sessionKey;
            SentText = text;
            if (FailSend) throw new IOException("uncertain");
            return Task.FromResult(new SendReceipt("sent-1"));
        }
    }
}
