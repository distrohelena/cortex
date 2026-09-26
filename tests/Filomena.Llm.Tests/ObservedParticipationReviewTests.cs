using System.Text.Json;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Llm.Context;
using Xunit;
using Filomena.App;

namespace Filomena.Llm.Tests;

public sealed class ObservedParticipationReviewTests
{
    [Theory]
    [InlineData("responder", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("esperar", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Wait)]
    [InlineData("calar", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Silence)]
    [InlineData("responder", ReassessmentOpportunity.Obsolete, "obsolete", null)]
    [InlineData("shutdown", ReassessmentOpportunity.Relevant, "cancelled", null)]
    [InlineData("automatic", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("preview", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("preview-outage", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("preview-boundary", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("preview-regenerate", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("preview-abort", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("preview-cancel", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("preview-automatic", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Respond)]
    [InlineData("preview-wait", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Wait)]
    [InlineData("preview-silence", ReassessmentOpportunity.Relevant, "reviewed", ParticipationReviewDecision.Silence)]
    [InlineData("preview", ReassessmentOpportunity.Obsolete, "obsolete", null)]
    [InlineData("initial-response", ReassessmentOpportunity.Relevant, "clear", null)]
    public async Task Observed_host_processes_pending_original_after_next_normal_batch_without_second_execution(
        string action, ReassessmentOpportunity opportunity, string expectedStatus, ParticipationReviewDecision? expectedDecision)
    {
        var directory = Directory.CreateTempSubdirectory("participation-observation-");
        try
        {
            var project = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root,
                "docs/manual-group-shadow-project-v3-gpt6-luna.json")))!;
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
            var previewEnabled = action.StartsWith("preview", StringComparison.Ordinal);
            var automatic = action is "automatic" or "preview-automatic" or "initial-response";
            var model = new SessionParticipationReviewTests.Model
            {
                Action = action == "preview-wait" ? "esperar" : action == "preview-silence" ? "calar"
                    : automatic || previewEnabled ? "responder" : action,
                AllowGeneration = previewEnabled || action == "initial-response",
                RespondInitially = action == "initial-response",
                BlockFirst = action == "preview-regenerate",
                BlockAll = action == "preview-abort"
            };
            var memory = new SessionParticipationReviewTests.Memory
            {
                AllowEmpty = (previewEnabled || action == "initial-response") && action != "preview-outage",
                WithBoundary = action is "preview-boundary" or "preview-regenerate" or "preview-abort"
            };
            var interpretationModel = new ContextInterpretationAnalyzerTests.Completion("provocacao", "high",
                opportunity == ReassessmentOpportunity.Obsolete ? "obsolete" : "still_relevant");
            if (automatic && action != "initial-response") interpretationModel.Scenario = "initial-policy";
            var analyzer = new ContextInterpretationAnalyzer(interpretationModel, ContextInterpretationAnalyzerTests.Settings);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (action == "preview-cancel") model.BeforeGeneration = async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            };
            CancellationToken analysisToken = default;
            await using var run = new ObservedShadowRun(pin, binding, directory.FullName, roles, model, memory, "replay-epoch", 0);
            if (automatic) ContextInterpretationHost.Configure(new("contextual-interpretation:v1", SessionParticipationReviewTests.Definition(),
                ContextInterpretationAnalyzerTests.Settings, 150, 2, 10, previewEnabled), pin, binding, run, interpretationModel);
            else run.ConfigureParticipationObservation(SessionParticipationReviewTests.Definition(), async (context, token) =>
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
            }, 150, 2, 10, automatic ? analyzer.AnalyzeInitialAsync : null, previewResponses: previewEnabled);
            CanonicalShadowInbound Input(long sequence, string id, string text) => new("replay-epoch", sequence,
                session, SessionEntryKind.Group, binding.ConversationNamespace, peer,
                new(id, session.ChannelId, session.AccountId, session.NativeConversationId, DateTimeOffset.UtcNow,
                    SessionMessageDirection.Inbound, SessionEntryKind.Group, JsonSerializer.SerializeToElement(new
                    {
                        texto = text,
                        remetente = new { nome = "Helena" },
                        canal = new { nome = "Grupo fixture", nativeId = session.NativeConversationId },
                        reply = (object?)null
                    })), false);
            var first = await run.AcceptAsync([Input(1, "original", "Agora com 1000.")], TestContext.Current.CancellationToken);
            Assert.True(Assert.Single(first.Outcomes).Success);
            Assert.Empty(first.ParticipationReviews);
            if (action == "initial-response")
            {
                Assert.Equal("clear", Assert.Single(first.InitialInterpretations).Status);
                var request = Assert.Single(model.Requests, r => r.NodeId == "group-generation");
                Assert.Equal("provocacao", request.Inputs.GetProperty("interpretacoes_contextuais")[0].GetProperty("interpretacao").GetProperty("Reading").GetString());
                Assert.Contains(first.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "shadowed");
                Assert.Single(interpretationModel.Requests);
                Assert.Single(run.History);
                return;
            }
            if (automatic)
            {
                Assert.Equal("tracked", Assert.Single(first.InitialInterpretations).Status);
                var initialSituation = Assert.Single(model.Requests, r => r.NodeId == "group-situation");
                var interpretation = Assert.Single(initialSituation.Inputs.GetProperty("interpretacoes_contextuais").EnumerateArray());
                Assert.Equal("ambigua", interpretation.GetProperty("interpretacao").GetProperty("Reading").GetString());
                Assert.Equal(0, interpretation.GetProperty("indice_no_batch").GetInt32());
                Assert.False(interpretation.GetProperty("certeza").GetBoolean());
            }
            else Assert.True(run.TrackParticipationReview("original"));
            var next = Input(2, "clarification", "Era brincadeira sobre a lista anterior.");
            var nextTask = run.AcceptAsync([next], TestContext.Current.CancellationToken);
            if (action == "preview-cancel")
            {
                await entered.Task;
                try { await run.DisposeAsync(); }
                finally { release.TrySetResult(); }
                var cancelled = Assert.Single((await nextTask).ParticipationReviews).Preview!;
                Assert.Equal(ParticipationReviewStatus.Cancelled, cancelled.Status);
                Assert.Empty(cancelled.Outputs);
                Assert.Empty(cancelled.Trace);
                return;
            }
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
            Assert.Equal(automatic ? 3 : 1, interpretationModel.Requests.Count);
            if (automatic) Assert.Equal("clear", Assert.Single(result.InitialInterpretations).Status);
            if (opportunity == ReassessmentOpportunity.Obsolete) Assert.Empty(model.Reviews);
            Assert.Equal(2, run.Outcomes.Count);
            Assert.Equal(2, run.History.Count);
            Assert.DoesNotContain(run.Trace, t => t.EventName is "node-outbound" or "speech-committed");
            Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
            if (previewEnabled && expectedDecision == ParticipationReviewDecision.Respond)
            {
                var preview = Assert.IsType<SessionResponsePreviewResult>(review.Preview);
                if (action == "preview-outage")
                {
                    Assert.Equal(ParticipationReviewStatus.Failed, preview.Status);
                    Assert.Empty(preview.Outputs);
                    Assert.DoesNotContain(model.Reviews, r => r.NodeId.EndsWith("-generation"));
                    return;
                }
                Assert.Equal(ParticipationReviewStatus.Completed, preview.Status);
                var generation = Assert.Single(model.Reviews, r => r.NodeId == "group-generation");
                Assert.Equal("provocacao", generation.Inputs.GetProperty("reavaliacao").GetProperty("interpretacao_contextual").GetProperty("Reading").GetString());
                Assert.Equal("Agora entendi a brincadeira da lista.", preview.Outputs["group-generation"]["texto"].GetString());
                if (action == "preview-abort")
                {
                    Assert.DoesNotContain(preview.Trace, t => t.EventName == "node-outbound");
                    Assert.Contains(preview.Steps, s => s.NodeId == "group-validation-2" && s.Exit == "block");
                    return;
                }
                var send = Assert.Single(preview.Trace, t => t.EventName == "node-outbound");
                Assert.True(send.MemoryMetadata!["result"] == "shadowed", JsonSerializer.Serialize(send.MemoryMetadata));
                Assert.Equal("participation-engaged-v1", send.MemoryMetadata["wouldTransitionTo"]);
                Assert.DoesNotContain(preview.Trace, t => t.EventName is "speech-committed" or "transition-applied");
                Assert.True(memory.Calls > 0);
                if (memory.WithBoundary) Assert.True(model.Reviews.Count(r => r.NodeId.Contains("validator")) >= 2);
                if (action == "preview-regenerate") Assert.Equal("Agora ficou claro.", preview.Outputs["group-regeneration"]["texto"].GetString());
            }
            else
            {
                Assert.Equal(0, memory.Calls);
                Assert.Null(review.Preview);
            }
            Assert.False(run.TrackParticipationReview("original"));
            Assert.Empty((await run.AcceptAsync([next], TestContext.Current.CancellationToken)).ParticipationReviews);
        }
        finally { directory.Delete(true); }
    }
}
