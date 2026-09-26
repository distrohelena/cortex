using Filomena.Core.Flows.Sessions;
using Xunit;
using static Filomena.Llm.Tests.SessionParticipationReviewTests;

namespace Filomena.Llm.Tests;

public sealed class PendingParticipationReviewsTests
{
    private static PendingParticipationReviews Controller(Harness h, int horizon = 150, int budget = 2, int capacity = 10)
        => new(h.Runtime, h.Session, Definition(), horizon, budget, capacity);

    private static Task<ParticipationReassessment> Relevant(ParticipationReassessmentContext context, CancellationToken token)
        => Task.FromResult(new ParticipationReassessment(true, ReassessmentOpportunity.Relevant,
            [context.Original.ProviderMessageId]));

    private static async Task Advance(Harness h, string id = "later")
    {
        await h.Runtime.ReceiveAsync(h.Session, h.Message(id, "Era uma brincadeira sobre a mensagem anterior."), TestContext.Current.CancellationToken);
        h.Clock.Advance(TimeSpan.FromSeconds(3));
        await h.Runtime.WaitForIdleAsync(h.Session, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task New_context_drives_real_review_once_without_readmission_or_outbound()
    {
        await using var h = await Harness.Start();
        var controller = Controller(h);
        Assert.True(controller.Track("original"));
        Assert.False(controller.Track("original"));
        Assert.Empty(await controller.ReviewPendingAsync(Relevant, TestContext.Current.CancellationToken));
        await Advance(h);
        var result = Assert.Single(await controller.ReviewPendingAsync((context, token) =>
        {
            Assert.Equal("original", context.Original.ProviderMessageId);
            Assert.Contains(context.History, m => m.ProviderMessageId == "later");
            return Relevant(context, token);
        }, TestContext.Current.CancellationToken));
        Assert.Equal("reviewed", result.Status);
        Assert.Equal(ParticipationReviewDecision.Respond, result.Review!.Decision);
        Assert.Equal(3, h.Runtime.ReadHistory(h.Session).Count);
        Assert.Equal(3, h.Runtime.ReadOutcomes(h.Session).Count);
        Assert.Equal(0, h.Memory.Calls);
        Assert.DoesNotContain(h.Runtime.ReadTrace(h.Session), t => t.EventName == "speech-committed");
        Assert.Empty(await controller.ReviewPendingAsync(Relevant, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ReassessmentOpportunity.Obsolete, true, "obsolete")]
    [InlineData(ReassessmentOpportunity.Uncertain, true, "pending")]
    [InlineData(ReassessmentOpportunity.Relevant, false, "pending")]
    public async Task Understanding_does_not_force_participation(ReassessmentOpportunity opportunity, bool confident, string status)
    {
        await using var h = await Harness.Start();
        var controller = Controller(h);
        controller.Track("original");
        await Advance(h);
        var result = Assert.Single(await controller.ReviewPendingAsync((_, _) => Task.FromResult(
            new ParticipationReassessment(confident, opportunity, ["later"])), TestContext.Current.CancellationToken));
        Assert.Equal(status, result.Status);
        Assert.Null(result.Review);
        Assert.Empty(h.Model.Reviews);
        Assert.Empty(await controller.ReviewPendingAsync(Relevant, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("context", "stale")]
    [InlineData("project", "stale")]
    [InlineData("closed", "closed")]
    public async Task Inflight_analysis_cannot_reopen_changed_context_or_closed_opportunity(string mutation, string status)
    {
        await using var h = await Harness.Start();
        var controller = Controller(h);
        controller.Track("original");
        await Advance(h);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = controller.ReviewPendingAsync(async (context, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return await Relevant(context, token);
        }, TestContext.Current.CancellationToken);
        await entered.Task;
        Assert.Empty(await controller.ReviewPendingAsync(Relevant, TestContext.Current.CancellationToken));
        if (mutation == "context") await Advance(h, "newest");
        else if (mutation == "project") h.Runtime.UpdateProject(new("review-project", 2, "changed", h.Project));
        else controller.Close("original");
        release.SetResult();
        Assert.Equal(status, Assert.Single(await task).Status);
        Assert.Empty(h.Model.Reviews);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("invented-evidence")]
    [InlineData("unknown-opportunity")]
    public async Task Failed_or_invalid_analysis_is_not_permission_and_does_not_retry_same_context(string scenario)
    {
        await using var h = await Harness.Start();
        var controller = Controller(h);
        controller.Track("original");
        await Advance(h);
        var result = Assert.Single(await controller.ReviewPendingAsync((_, _) => scenario == "failure"
            ? throw new HttpRequestException("fixture")
            : Task.FromResult(new ParticipationReassessment(true,
                scenario == "unknown-opportunity" ? (ReassessmentOpportunity)999 : ReassessmentOpportunity.Relevant,
                ["invented"])), TestContext.Current.CancellationToken));
        Assert.Equal("analysis_failed", result.Status);
        Assert.Empty(h.Model.Reviews);
        Assert.Empty(await controller.ReviewPendingAsync(Relevant, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Horizon_capacity_and_missing_identity_bound_work_without_automatic_recovery()
    {
        await using var h = await Harness.Start();
        var controller = Controller(h, horizon: 2, capacity: 1);
        Assert.False(controller.Track("unknown"));
        Assert.True(controller.Track("original"));
        Assert.False(controller.Track("clarification"));
        await Advance(h);
        var result = Assert.Single(await controller.ReviewPendingAsync((_, _) => throw new Exception("Must not analyze obsolete work"),
            TestContext.Current.CancellationToken));
        Assert.Equal("distance_exceeded", result.Status);
        Assert.Empty(await Controller(h).ReviewPendingAsync(Relevant, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Budget_stops_inconclusive_reanalysis_on_later_context()
    {
        await using var h = await Harness.Start();
        var controller = Controller(h, budget: 1);
        controller.Track("original");
        await Advance(h);
        await controller.ReviewPendingAsync((_, _) => Task.FromResult(new ParticipationReassessment(false,
            ReassessmentOpportunity.Uncertain, [])), TestContext.Current.CancellationToken);
        await Advance(h, "later2");
        Assert.Equal("budget_exhausted", Assert.Single(await controller.ReviewPendingAsync(Relevant,
            TestContext.Current.CancellationToken)).Status);
        Assert.Empty(h.Model.Reviews);
    }

    [Fact]
    public async Task Cancellation_even_when_analyzer_ignores_it_does_not_enter_review_or_repeat_same_context()
    {
        await using var h = await Harness.Start();
        var controller = Controller(h);
        controller.Track("original");
        await Advance(h);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.ReviewPendingAsync((context, token) =>
        {
            cancellation.Cancel();
            return Relevant(context, token);
        }, cancellation.Token));
        Assert.Empty(h.Model.Reviews);
        Assert.Empty(await controller.ReviewPendingAsync(Relevant, TestContext.Current.CancellationToken));
    }
}
