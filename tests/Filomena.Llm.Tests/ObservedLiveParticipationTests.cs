using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Cortex.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ObservedLiveParticipationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Local_live_host_reassesses_without_reopening_inbox_and_restores_only_confirmed_context(bool respond, bool wait, bool ordinaryReply)
    {
        var directory = Directory.CreateTempSubdirectory("observed-live-participation-");
        try
        {
            var project = JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root,
                "docs/manual-group-live-project-v3-natural-v1.json")))!;
            foreach (var state in project["project"]!["states"]!.AsArray())
                foreach (var node in state!["graph"]!["nodes"]!.AsArray().Where(n => n!["kind"]!.ToString() == "accumulate"))
                    node!["settings"]!["quietSeconds"] = 0.001;
            var pin = new PinnedShadowProject(new("review-project", 1, "review-v3", JsonSerializer.SerializeToElement(project)));
            var session = new SessionKey("fixture", "filomena", "whatsapp", "fixture-account", "120300000000001@g.us");
            const string peer = "whatsapp:5511000000000@s.whatsapp.net";
            var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId),
                ["fixture:self"], [peer], SessionEntryKind.Group);
            var model = new SessionParticipationReviewTests.Model { AllowGeneration = true, Action = wait ? "esperar" : "responder" };
            var analysis = new ContextInterpretationAnalyzerTests.Completion("ironia", "high", "still_relevant") { Scenario = "initial-policy" };
            var memory = new SessionParticipationReviewTests.Memory { AllowEmpty = true };
            var sender = new ReviewedResponseContinuationTests.Transport();
            var options = ContextInterpretationHostTests.ReadOperationalConfig(false) with { RespondToReassessedMessages = respond };
            var finalSequence = wait ? 4 : 3;
            var route = LiveRouteBindings.FromShadow(binding, "fixture-route", session.NativeConversationId, "Filomena", 1000)
                with
            { AllowExactGroupDelivery = true };
            ObservedLiveRun Open(long tail) => new(pin, binding, directory.FullName, ReviewedResponseContinuationTests.Harness.Roles,
                model, memory, "fixture-epoch", tail, route, sender, new(true, true, true, true, true, true, true),
                _ => Task.CompletedTask, 5, participation: ContextInterpretationHost.CreateLiveOptions(options, pin, binding, analysis));
            CanonicalShadowInbound Input(long sequence, string id, string text) => new("fixture-epoch", sequence,
                session, SessionEntryKind.Group, binding.ConversationNamespace, peer,
                new(id, session.ChannelId, session.AccountId, session.NativeConversationId, DateTimeOffset.UtcNow,
                    SessionMessageDirection.Inbound, SessionEntryKind.Group, JsonSerializer.SerializeToElement(new
                    {
                        texto = text,
                        remetente = new { nome = "Fixture" },
                        canal = new { nome = "Fixture", nativeId = session.NativeConversationId },
                        reply = (object?)null
                    })), false);
            await using (var run = Open(0))
            {
                var first = await run.AcceptAsync([Input(1, "original", "Agora com 1000.")], TestContext.Current.CancellationToken);
                Assert.Equal("tracked", Assert.Single(first.InitialInterpretations).Status);
                Assert.Equal(0, sender.Calls);
                model.RespondInitially = ordinaryReply;
                var clarification = Input(2, "clarification", "Era brincadeira sobre a lista.");
                var second = await run.AcceptAsync([clarification], TestContext.Current.CancellationToken);
                if (ordinaryReply)
                {
                    Assert.Equal(1, sender.Calls);
                    Assert.Empty(second.ParticipationReviews);
                    Assert.DoesNotContain(second.Outcomes, outcome => outcome.ReassessmentOfMessageId is not null);
                    Assert.Single(run.History, item => item.Direction == SessionMessageDirection.Outbound);
                    Assert.Empty(model.Reviews);
                    Assert.Equal(OutboundMode.Enabled, run.OutboundMode);
                    Assert.Empty((await run.AcceptAsync([clarification], TestContext.Current.CancellationToken)).ParticipationReviews);
                    Assert.Equal(1, sender.Calls);
                    model.RespondInitially = false;
                    var resumed = await run.AcceptAsync([Input(3, "return-to-original", "Voltando ao primeiro comentário, ainda quero discutir aquela brincadeira.")],
                        TestContext.Current.CancellationToken);
                    Assert.Equal("original", Assert.Single(resumed.ParticipationReviews).OriginalMessageId);
                    Assert.Equal(2, sender.Calls);
                    return;
                }
                Assert.Equal(wait ? ParticipationReviewDecision.Wait : ParticipationReviewDecision.Respond,
                    Assert.Single(second.ParticipationReviews).Review!.Decision);
                if (wait)
                {
                    Assert.Equal(0, sender.Calls);
                    model.Action = "responder";
                    second = await run.AcceptAsync([Input(3, "more", "Ainda estamos discutindo a brincadeira.")], TestContext.Current.CancellationToken);
                }
                Assert.Equal(respond ? 1 : 0, sender.Calls);
                if (respond)
                {
                    Assert.Equal("original", Assert.Single(second.ParticipationReviews).Continuation!.Outcome!.ReassessmentOfMessageId);
                    Assert.Contains(second.Trace, t => t.EventName == "speech-committed");
                }
                else Assert.Null(Assert.Single(second.ParticipationReviews).Continuation);
                Assert.Single(run.History, m => m.ProviderMessageId == "original");
                Assert.Equal(respond ? 1 : 0, run.History.Count(m => m.Direction == SessionMessageDirection.Outbound));
                // A later normal drain must not complete the original inbox entry again.
                await run.AcceptAsync([Input(finalSequence, "after", "Assunto encerrado.")], TestContext.Current.CancellationToken);
                Assert.Equal(OutboundMode.Enabled, run.OutboundMode);
                Assert.Equal(respond ? 1 : 0, sender.Calls);
            }
            var requests = analysis.Requests.Count;
            await using (var restarted = Open(finalSequence))
            {
                Assert.Equal(respond ? 1 : 0, restarted.History.Count(m => m.Direction == SessionMessageDirection.Outbound));
                Assert.Empty(restarted.Outcomes);
                Assert.Equal(requests, analysis.Requests.Count);
                var result = await restarted.AcceptAsync([Input(finalSequence + 1, "restart-new", "Outra conversa.")], TestContext.Current.CancellationToken);
                Assert.Empty(result.ParticipationReviews);
                Assert.Equal(respond ? 1 : 0, sender.Calls);
            }
        }
        finally { directory.Delete(true); }
    }
}
