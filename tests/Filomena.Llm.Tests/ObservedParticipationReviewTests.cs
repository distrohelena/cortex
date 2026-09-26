using System.Text.Json;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Llm.Context;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ObservedParticipationReviewTests
{
    [Theory]
    [InlineData("responder", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("esperar", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Wait)]
    [InlineData("calar", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Silence)]
    [InlineData("responder", ReassessmentOpportunity.Obsolete, "obsolete", null)]
    [InlineData("shutdown", ReassessmentOpportunity.Relevant, "cancelled", null)]
    public async Task Observed_host_processes_pending_original_after_next_normal_batch_without_second_execution(
        string action, ReassessmentOpportunity opportunity, string expectedStatus, ParticipationReviewDecision? expectedDecision)
    {
        var directory = Directory.CreateTempSubdirectory("participation-observation-");
        try
        {
            var project = DirectParticipationRuleTests.Project();
            // Controlled clock window only; real graph, prompt bodies and routing remain unchanged.
            foreach (var state in project["project"]!["states"]!.AsArray())
                foreach (var node in state!["graph"]!["nodes"]!.AsArray().Where(n => n!["kind"]!.ToString() == "accumulate"))
                    node!["settings"]!["quietSeconds"] = 0.001;
            var pin = new PinnedShadowProject(new("review-project", 1, "review-v3", JsonSerializer.SerializeToElement(project)));
            var session = new SessionKey("replay-run", "filomena", "whatsapp", "fixture-account", "fixture-group@g.us");
            const string peer = "whatsapp:5511000000000@s.whatsapp.net";
            var binding = new ShadowSessionBinding(session,
                FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId), ["fixture:self"], [peer], SessionEntryKind.Group);
            var roles = new OutboundBindings(["group-response-boundaries"], ["group-compiled-boundaries"],
                new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
                ["group-validator-1", "group-validator-2"], "participation-engaged-v1");
            var model = new SessionParticipationReviewTests.Model { Action = action };
            var memory = new SessionParticipationReviewTests.Memory();
            var interpretationModel = new ContextInterpretationAnalyzerTests.Completion("provocacao", "high",
                opportunity == ReassessmentOpportunity.Obsolete ? "obsolete" : "still_relevant");
            var analyzer = new ContextInterpretationAnalyzer(interpretationModel, ContextInterpretationAnalyzerTests.Settings);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken analysisToken = default;
            await using var run = new ObservedShadowRun(pin, binding, directory.FullName, roles, model, memory, "replay-epoch", 0);
            run.ConfigureParticipationObservation(SessionParticipationReviewTests.Definition(), async (context, token) =>
            {
                Assert.Equal("original", context.Original.ProviderMessageId);
                Assert.Contains(context.History, item => item.ProviderMessageId == "clarification");
                if (action == "shutdown")
                {
                    analysisToken = token;
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                }
                return await analyzer.ReassessAsync(context, token);
            }, 150, 2, 10);
            CanonicalShadowInbound Input(long sequence, string id, string text) => new("replay-epoch", sequence,
                session, SessionEntryKind.Group, binding.ConversationNamespace, peer,
                new(id, session.ChannelId, session.AccountId, session.NativeConversationId, DateTimeOffset.UtcNow,
                    SessionMessageDirection.Inbound, SessionEntryKind.Group, JsonSerializer.SerializeToElement(new
                    {
                        texto = text,
                        remetente = new { nome = "Participante" },
                        canal = new { nome = "Grupo fixture", nativeId = session.NativeConversationId },
                        reply = (object?)null
                    })), false);
            var first = await run.AcceptAsync([Input(1, "original", "Agora com 1000.")], TestContext.Current.CancellationToken);
            Assert.True(Assert.Single(first.Outcomes).Success);
            Assert.Empty(first.ParticipationReviews);
            Assert.True(run.TrackParticipationReview("original"));
            var next = Input(2, "clarification", "Era brincadeira sobre a lista anterior.");
            var nextTask = run.AcceptAsync([next], TestContext.Current.CancellationToken);
            if (action == "shutdown")
            {
                await entered.Task;
                try { await run.DisposeAsync(); }
                finally { release.TrySetResult(); }
                var cancelled = analysisToken.IsCancellationRequested;
                var error = await Record.ExceptionAsync(() => nextTask);
                Assert.True(cancelled, "Host disposal must cancel its own observer before closing resources.");
                Assert.IsAssignableFrom<OperationCanceledException>(error);
                Assert.Empty(model.Reviews);
                return;
            }
            var result = await nextTask;
            Assert.True(Assert.Single(result.Outcomes).Success);
            var review = Assert.Single(result.ParticipationReviews);
            Assert.Equal(expectedStatus, review.Status);
            Assert.Equal(expectedDecision, review.Review?.Decision);
            Assert.Equal("provocacao", review.Assessment!.Interpretation!.Reading);
            Assert.Equal(["clarification"], review.Assessment.EvidenceMessageIds);
            Assert.Single(interpretationModel.Requests);
            if (opportunity == ReassessmentOpportunity.Obsolete) Assert.Empty(model.Reviews);
            Assert.Equal(2, run.Outcomes.Count);
            Assert.Equal(2, run.History.Count);
            Assert.DoesNotContain(run.Trace, t => t.EventName is "node-outbound" or "speech-committed");
            Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
            Assert.Equal(0, memory.Calls);
            Assert.False(run.TrackParticipationReview("original"));
            Assert.Empty((await run.AcceptAsync([next], TestContext.Current.CancellationToken)).ParticipationReviews);
        }
        finally { directory.Delete(true); }
    }
}
