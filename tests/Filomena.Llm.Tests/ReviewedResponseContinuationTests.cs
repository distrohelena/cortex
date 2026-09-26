using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Conversations;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Outbound;
using Filomena.Core.Tests.Flows;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ReviewedResponseContinuationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("send")]
    [InlineData("block")]
    [InlineData("regenerate")]
    [InlineData("outage")]
    [InlineData("uncertain")]
    public async Task Reviewed_original_uses_normal_pipeline_without_readmission_and_commits_only_confirmed_speech(string scenario)
    {
        await using var h = await Harness.Start();
        h.Model.BlockAll = scenario == "block";
        h.Model.BlockFirst = scenario == "regenerate";
        h.Memory.WithBoundary = scenario is "block" or "regenerate";
        h.Memory.AllowEmpty = scenario != "outage";
        h.Sender.Fail = scenario == "uncertain";
        var review = await h.Runtime.ReviewParticipationAsync(h.Session, "original", SessionParticipationReviewTests.Definition(), Token);
        Assert.Equal(ParticipationReviewDecision.Respond, review.Decision);
        var result = await h.Runtime.ContinueReviewedResponseAsync(h.Session, review,
            new("ironia", null, "high", "Contexto novo esclareceu a brincadeira."), Token);
        if (scenario is "send" or "block" or "regenerate")
            Assert.True(result.Outcome?.Success == true, JsonSerializer.Serialize(new
            {
                result.Status,
                result.Diagnostic,
                trace = h.Runtime.ReadTrace(h.Session).Where(t => t.ErrorCode is not null).Select(t => new { t.EventName, t.NodeId, t.ErrorCode })
            }));
        var sent = scenario is "send" or "regenerate";
        Assert.Equal(sent ? 1 : 0, h.Runtime.ReadHistory(h.Session).Count(m => m.Direction == SessionMessageDirection.Outbound));
        Assert.Single(h.Runtime.ReadHistory(h.Session), m => m.ProviderMessageId == "original");
        Assert.Equal(scenario is "send" or "regenerate" or "uncertain" ? 1 : 0, h.Sender.Calls);
        var continuation = Assert.IsType<SessionFlowOutcome>(result.Outcome);
        Assert.Equal("original", continuation.ReassessmentOfMessageId);
        Assert.Equal(sent, continuation.TransitionApplied);
        Assert.Equal(3, h.Runtime.ReadOutcomes(h.Session).Count);
        Assert.Equal(2, h.Runtime.ReadOutcomes(h.Session).Count(o => o.ReassessmentOfMessageId is null));
        Assert.Single(h.Model.Reviews, r => r.NodeId == "group-situation");
        Assert.Single(h.Model.Reviews, r => r.NodeId == "group-presence");
        if (scenario != "outage")
        {
            var generation = Assert.Single(h.Model.Reviews, r => r.NodeId == "group-generation");
            Assert.Equal("ironia", generation.Inputs.GetProperty("reavaliacao").GetProperty("interpretacao_contextual").GetProperty("Reading").GetString());
        }
        if (scenario == "regenerate") Assert.Single(h.Model.Reviews, r => r.NodeId == "group-regeneration");
        if (scenario == "uncertain") Assert.True(h.Host.RequiresReconciliation(h.Session));
        var calls = h.Sender.Calls;
        var duplicate = await h.Runtime.ContinueReviewedResponseAsync(h.Session, review, null, Token);
        Assert.Equal(ParticipationReviewStatus.Rejected, duplicate.Status);
        Assert.Equal(calls, h.Sender.Calls);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("revision")]
    [InlineData("session")]
    [InlineData("disarmed")]
    [InlineData("shadow")]
    [InlineData("report-copy")]
    public async Task Stale_or_untrusted_review_cannot_start_late_generation(string mutation)
    {
        await using var h = await Harness.Start();
        var review = await h.Runtime.ReviewParticipationAsync(h.Session, "original", SessionParticipationReviewTests.Definition(), Token);
        if (mutation == "context") await h.Admit("newest", "Mudamos de assunto.");
        if (mutation == "revision") h.Runtime.UpdateProject(new("review-project", 2, "R2", h.Project));
        if (mutation == "disarmed") h.Host.Rearm(OutboundMode.Disabled);
        if (mutation == "shadow") h.Host.Rearm(OutboundMode.Shadow);
        if (mutation == "report-copy") review = JsonSerializer.Deserialize<SessionParticipationReviewResult>(JsonSerializer.Serialize(review))!;
        var result = await h.Runtime.ContinueReviewedResponseAsync(
            mutation == "session" ? h.Session with { AccountId = "foreign" } : h.Session, review, null, Token);
        Assert.Equal(ParticipationReviewStatus.Rejected, result.Status);
        Assert.Null(result.Outcome);
        Assert.DoesNotContain(h.Model.Requests, r => r.NodeId == "group-generation");
        Assert.Equal(0, h.Sender.Calls);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("route")]
    public async Task New_context_while_late_response_is_inflight_prevents_dispatch(string phase)
    {
        await using var h = await Harness.Start();
        var review = await h.Runtime.ReviewParticipationAsync(h.Session, "original", SessionParticipationReviewTests.Definition(), Token);
        Task Arrive() => h.Admit("newest", "Mudamos de assunto.", wait: false);
        if (phase == "generation") h.Model.BeforeGeneration = _ => Arrive();
        else h.Sender.BeforeDescribe = Arrive;
        await h.Runtime.ContinueReviewedResponseAsync(h.Session, review, null, Token);
        await h.Runtime.WaitForIdleAsync(h.Session, Token);
        Assert.Equal(0, h.Sender.Calls);
        Assert.DoesNotContain(h.Runtime.ReadHistory(h.Session), m => m.Direction == SessionMessageDirection.Outbound);
        Assert.False(h.Host.RequiresReconciliation(h.Session)); // No provider call was made.
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("revision")]
    [InlineData("disarm")]
    public async Task Authorization_loss_during_route_check_does_not_become_an_uncertain_send(string mutation)
    {
        await using var h = await Harness.Start();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var review = await h.Runtime.ReviewParticipationAsync(h.Session, "original", SessionParticipationReviewTests.Definition(), Token);
        h.Sender.BeforeDescribe = () =>
        {
            if (mutation == "cancel") cancellation.Cancel();
            else if (mutation == "revision") h.Runtime.UpdateProject(new("review-project", 2, "R2", h.Project));
            else h.Host.Rearm(OutboundMode.Disabled);
            return Task.CompletedTask;
        };
        await h.Runtime.ContinueReviewedResponseAsync(h.Session, review, null, cancellation.Token);
        Assert.Equal(0, h.Sender.Calls);
        Assert.False(h.Host.RequiresReconciliation(h.Session));
        Assert.DoesNotContain(h.Runtime.ReadHistory(h.Session), m => m.Direction == SessionMessageDirection.Outbound);
        Assert.Equal(0, h.Runtime.ReadSession(h.Session)!.PendingExecutionCount);
    }

    internal sealed class Harness : IAsyncDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("reviewed-response-");
        internal readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        internal readonly SessionKey Session = new("review-run", "filomena", "whatsapp", "fixture-account", "120300000000001@g.us");
        internal readonly SessionParticipationReviewTests.Model Model = new() { AllowGeneration = true };
        internal readonly SessionParticipationReviewTests.Memory Memory = new() { AllowEmpty = true };
        internal readonly Transport Sender = new();
        internal SessionFlowRuntime Runtime = null!;
        internal ShadowOutboundHost Host = null!;
        internal JsonElement Project;
        internal static OutboundBindings Roles => new(["group-response-boundaries"], ["group-compiled-boundaries"],
            new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
            ["group-validator-1", "group-validator-2"], "participation-engaged-v1");

        internal static async Task<Harness> Start()
        {
            var h = new Harness();
            h.Project = JsonSerializer.SerializeToElement(JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root,
                "docs/manual-group-live-project-v3-natural-v1.json")))!);
            h.Host = new(Path.Combine(h.directory.FullName, "outbound.jsonl"), h.Project, Roles, [h.Session], new(true, true, true, true, true, true, true));
            h.Host.BindLiveRoute(new(h.Session, "fixture-route", "whatsapp", "fixture-account", h.Session.NativeConversationId,
                new("Filomena", "fixture:self"), 1000)
            { AllowExactGroupDelivery = true }, h.Sender);
            h.Host.Rearm(OutboundMode.Enabled);
            h.Runtime = new(h.Clock, h.Model, h.Memory, outboundHost: h.Host);
            h.Runtime.UpdateProject(new("review-project", 1, "review-v3", h.Project));
            await h.Admit("original", "Sensacional esse plano.");
            await h.Admit("clarification", "Era ironia sobre apagar os backups.");
            return h;
        }

        internal async Task Admit(string id, string text, bool wait = true)
        {
            await Runtime.ReceiveAsync(Session, new(id, Session.ChannelId, Session.AccountId, Session.NativeConversationId,
                Clock.GetUtcNow(), SessionMessageDirection.Inbound, SessionEntryKind.Group, JsonSerializer.SerializeToElement(new
                {
                    texto = text,
                    remetente = new { nome = "Pessoa fixture", stableId = "whatsapp:5511000000000@s.whatsapp.net" },
                    canal = new { nome = "Fixture", nativeId = Session.NativeConversationId },
                    reply = (object?)null
                })), Token);
            Clock.Advance(TimeSpan.FromSeconds(3));
            if (wait) await Runtime.WaitForIdleAsync(Session, Token);
        }

        public async ValueTask DisposeAsync()
        {
            if (Runtime is not null) await Runtime.DisposeAsync();
            Host?.Dispose();
            directory.Delete(true);
        }
    }

    internal sealed class Transport : IBoundOutboundTransport
    {
        internal int Calls;
        internal bool Fail;
        internal Func<Task>? BeforeDescribe;
        public async Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken token)
        {
            if (BeforeDescribe is not null) await BeforeDescribe();
            return new("whatsapp", "fixture-account", "120300000000001@g.us");
        }
        public Task<SendReceipt> SendTextAsync(string route, string text, CancellationToken token) => throw new InvalidOperationException("Unbound send forbidden.");
        public Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken token)
        {
            Calls++;
            if (Fail) throw new IOException("Fixture receipt unavailable.");
            return Task.FromResult(new SendReceipt("fixture-receipt-" + Calls)
            { Channel = "whatsapp", AccountId = "fixture-account", Recipient = "120300000000001@g.us", ReplyToMessageId = request.ReplyToMessageId });
        }
    }
}
