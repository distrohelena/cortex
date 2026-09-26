using System.Collections.Concurrent;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Tests.Flows;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class SessionParticipationReviewTests
{
    internal static ParticipationReviewDefinition Definition(string revision = "review-v3") => new(revision,
        ["group-situation", "group-presence"],
        [new("group-direct-involvement", "direct_involvement_rule", ParticipationReviewDecision.Respond),
         new("group-action", "case-responder", ParticipationReviewDecision.Respond),
         new("group-action", "case-esperar", ParticipationReviewDecision.Wait),
         new("group-action", "case-calar", ParticipationReviewDecision.Silence)]);

    [Fact]
    public async Task Review_uses_original_and_current_context_without_readmission_or_session_mutation()
    {
        await using var h = await Harness.Start();
        var before = h.Runtime.ReadSession(h.Session);
        var history = JsonSerializer.Serialize(h.Runtime.ReadHistory(h.Session));
        var outcomes = JsonSerializer.Serialize(h.Runtime.ReadOutcomes(h.Session));
        var trace = JsonSerializer.Serialize(h.Runtime.ReadTrace(h.Session));
        var result = await h.Review();
        Assert.Equal(ParticipationReviewStatus.Completed, result.Status);
        Assert.Equal(ParticipationReviewDecision.Respond, result.Decision);
        var situation = Assert.Single(h.Model.Reviews, r => r.NodeId == "group-situation");
        Assert.Equal("Agora com 1000.", Assert.Single(situation.Inputs.GetProperty("mensagens").EnumerateArray()).GetProperty("texto").GetString());
        var original = situation.Inputs.GetProperty("mensagens")[0];
        Assert.Equal("Helena", original.GetProperty("remetente").GetProperty("nome").GetString());
        Assert.Equal("Aqui estão 100 casas de pi.", original.GetProperty("reply").GetProperty("texto").GetString());
        var texts = situation.Inputs.GetProperty("historico").EnumerateArray().Select(m => m.GetProperty("texto").GetString()).ToArray();
        Assert.Contains("Era brincadeira, não precisa listar os números.", texts);
        Assert.DoesNotContain("Agora com 1000.", texts);
        Assert.True(situation.Inputs.GetProperty("reavaliacao").GetProperty("contexto_pode_ser_posterior").GetBoolean());
        Assert.Equal(before, result.Snapshot);
        Assert.Equal(before, h.Runtime.ReadSession(h.Session));
        Assert.Equal(history, JsonSerializer.Serialize(h.Runtime.ReadHistory(h.Session)));
        Assert.Equal(outcomes, JsonSerializer.Serialize(h.Runtime.ReadOutcomes(h.Session)));
        Assert.Equal(trace, JsonSerializer.Serialize(h.Runtime.ReadTrace(h.Session)));
        Assert.Equal(SessionAdmissionStatus.Duplicate, (await h.Runtime.ReceiveAsync(h.Session, h.Message("original", "Agora com 1000."), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, h.Runtime.ReadOutcomes(h.Session).Count);
        Assert.Equal(0, h.Memory.Calls);
        Assert.Equal(0, h.ReviewCallbacks);
        Assert.DoesNotContain(result.Steps, s => s.NodeId.Contains("generation") || s.NodeId.Contains("send") || s.NodeId.Contains("transition"));
    }

    [Theory]
    [InlineData(Inactive)]
    [InlineData(Attentive)]
    [InlineData(Engaged)]
    public async Task Direct_rule_is_preserved_without_presence_generation_or_transition(string state)
    {
        await using var h = await Harness.Start(state);
        Assert.Equal(state, h.Runtime.ReadSession(h.Session)!.CurrentStateId);
        h.Model.Involvement = "direto";
        var result = await h.Review();
        Assert.Equal(ParticipationReviewStatus.Completed, result.Status);
        Assert.Equal(ParticipationReviewDecision.Respond, result.Decision);
        Assert.Single(h.Model.Reviews);
        Assert.Contains(result.Steps, s => s.Exit == "direct_involvement_rule");
        Assert.Equal(result.Snapshot!.StateVersion, h.Runtime.ReadSession(h.Session)!.StateVersion);
    }

    [Theory]
    [InlineData("responder", ParticipationReviewDecision.Respond)]
    [InlineData("esperar", ParticipationReviewDecision.Wait)]
    [InlineData("calar", ParticipationReviewDecision.Silence)]
    public async Task Non_direct_uses_existing_presence_and_stops_at_each_decision(string action, ParticipationReviewDecision expected)
    {
        await using var h = await Harness.Start();
        h.Model.Action = action;
        var result = await h.Review();
        Assert.Equal(ParticipationReviewStatus.Completed, result.Status);
        Assert.Equal(expected, result.Decision);
        Assert.Equal(2, h.Model.Reviews.Count);
        Assert.Equal("group-action", result.Steps.Last().NodeId);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("project")]
    public async Task Context_or_project_change_while_model_runs_discards_decision(string change)
    {
        await using var h = await Harness.Start();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Model.BeforeReview = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var task = h.Review();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var concurrent = await h.Review();
        Assert.Equal(ParticipationReviewStatus.Rejected, concurrent.Status);
        if (change == "context") await h.Runtime.ReceiveAsync(h.Session, h.Message("newer", "Mudamos de assunto."), TestContext.Current.CancellationToken);
        else h.Runtime.UpdateProject(new("review-project", 2, "new-revision", h.Project));
        release.SetResult();
        var result = await task;
        Assert.Equal(ParticipationReviewStatus.Stale, result.Status);
        Assert.Null(result.Decision);
        Assert.Empty(result.Outputs);
        Assert.Single(h.Model.Reviews); // No second model call after staleness.
    }

    [Theory]
    [InlineData("missing-target")]
    [InlineData("wrong-session")]
    [InlineData("revision")]
    [InlineData("busy")]
    public async Task Missing_identity_stale_definition_or_unsettled_session_never_calls_model(string fault)
    {
        await using var h = await Harness.Start();
        if (fault == "busy") await h.Runtime.ReceiveAsync(h.Session, h.Message("buffered", "Ainda digitando."), TestContext.Current.CancellationToken);
        var result = await h.Runtime.ReviewParticipationAsync(
            fault == "wrong-session" ? h.Session with { AccountId = "other-account" } : h.Session,
            fault == "missing-target" ? "absent" : "original", Definition(fault == "revision" ? "old" : "review-v3"), TestContext.Current.CancellationToken);
        Assert.Equal(ParticipationReviewStatus.Rejected, result.Status);
        Assert.Null(result.Decision);
        Assert.Empty(h.Model.Reviews);
    }

    [Theory]
    [InlineData("missing-boundary")]
    [InlineData("unknown-prompt")]
    [InlineData("text-generation")]
    public async Task Unsafe_or_incomplete_cut_rejected_before_any_model_call(string fault)
    {
        await using var h = await Harness.Start();
        var definition = Definition();
        definition = fault switch
        {
            "missing-boundary" => definition with { Boundaries = definition.Boundaries.Where(b => b.Exit != "case-responder").ToArray() },
            "unknown-prompt" => definition with { PromptNodeIds = ["group-situation", "missing"] },
            _ => definition with { PromptNodeIds = ["group-situation", "group-presence", "group-generation"] }
        };
        var result = await h.Runtime.ReviewParticipationAsync(h.Session, "original", definition, TestContext.Current.CancellationToken);
        Assert.Equal(ParticipationReviewStatus.Rejected, result.Status);
        Assert.Empty(h.Model.Reviews);
        Assert.Equal(0, h.Memory.Calls);
    }

    [Fact]
    public async Task Malformed_model_output_is_not_a_decision()
    {
        await using var h = await Harness.Start();
        h.Model.Invalid = true;
        var result = await h.Review();
        Assert.Equal(ParticipationReviewStatus.Failed, result.Status);
        Assert.Null(result.Decision);
        Assert.Empty(result.Outputs);
        Assert.DoesNotContain(h.Model.Reviews, r => r.NodeId == "group-presence");
    }

    [Fact]
    public async Task Disposal_cancels_review_and_waits_without_mutating_session()
    {
        await using var h = await Harness.Start();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Model.BeforeReview = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
        var task = h.Review();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await h.Runtime.DisposeAsync();
        Assert.Equal(ParticipationReviewStatus.Cancelled, (await task).Status);
        Assert.Equal(2, h.Runtime.ReadHistory(h.Session).Count);
    }

    [Fact]
    public async Task Outbound_history_and_unknown_new_run_cannot_be_reviewed_as_inbound()
    {
        await using var h = await Harness.Start();
        var outbound = h.Message("self", "Resposta enviada anteriormente.") with { Direction = SessionMessageDirection.Outbound };
        await h.Runtime.ReceiveAsync(h.Session, outbound, TestContext.Current.CancellationToken);
        var self = await h.Runtime.ReviewParticipationAsync(h.Session, "self", Definition(), TestContext.Current.CancellationToken);
        Assert.Equal(ParticipationReviewStatus.Rejected, self.Status);
        Assert.Empty(h.Model.Reviews);
        await using var restart = new SessionFlowRuntime(h.Clock, h.Model);
        restart.UpdateProject(new("review-project", 1, "review-v3", h.Project));
        var result = await restart.ReviewParticipationAsync(h.Session, "original", Definition(), TestContext.Current.CancellationToken);
        Assert.Equal(ParticipationReviewStatus.Rejected, result.Status);
    }

    [Fact]
    public async Task Provider_failure_cannot_be_reported_as_silence_or_response()
    {
        await using var h = await Harness.Start();
        h.Model.BeforeReview = _ => throw new HttpRequestException("Synthetic provider unavailable.");
        var result = await h.Review();
        Assert.Equal(ParticipationReviewStatus.Failed, result.Status);
        Assert.Null(result.Decision);
        Assert.Empty(result.Outputs);
        Assert.Equal(2, h.Runtime.ReadHistory(h.Session).Count);
    }

    [Theory]
    [InlineData("still_relevant", false, true)]
    [InlineData("obsolete", false, false)]
    [InlineData("still_relevant", true, false)]
    public async Task Pending_replay_only_opens_participation_review_for_unhandled_current_opportunity(
        string opportunity, bool handled, bool expectReview)
    {
        await using var h = await Harness.Start();
        // This controller is a replay harness, not an installed scheduler. IDs come from runtime history.
        JsonElement Input(bool later) => IronyReplayExperiment.Input(JsonSerializer.SerializeToElement(new
        {
            id = "runtime-replay",
            groupRef = h.Session.NativeConversationId,
            gaps = new[] { "controlled_fixture" },
            before = Array.Empty<object>(),
            target = new { id = "original", text = h.Runtime.ReadHistory(h.Session)[0].Message.GetProperty("texto").GetString() },
            after = later ? new[] { new { id = "clarification", text = h.Runtime.ReadHistory(h.Session)[1].Message.GetProperty("texto").GetString() } } : []
        }), later);
        var pending = new PendingIronyReplay(Input(false), IronyPendingReplayTests.Reading(evidence: "original"));
        pending.Advance(Input(true), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        if (handled) pending.MarkHandled();
        var interpretation = pending.Complete(job, IronyPendingReplayTests.Reading("provocacao", "high", opportunity, "clarification"));
        SessionParticipationReviewResult? result = null;
        if (interpretation == "needs_participation_review")
        {
            h.Model.Action = "calar"; // Understanding irony still permits the CURRENT flow to decline participation.
            result = await h.Runtime.ReviewParticipationAsync(h.Session, job.Input.GetProperty("target").GetProperty("id").GetString()!,
                Definition(), TestContext.Current.CancellationToken);
        }
        Assert.Equal(expectReview, result is not null);
        if (expectReview) Assert.Equal(ParticipationReviewDecision.Silence, result!.Decision);
        else Assert.Empty(h.Model.Reviews);
        Assert.Equal(2, h.Runtime.ReadHistory(h.Session).Count);
        Assert.Equal(2, h.Runtime.ReadOutcomes(h.Session).Count);
        Assert.Null(pending.Take());
    }

    internal sealed class Model : IFlowCompletion
    {
        internal ConcurrentQueue<FlowCompletionRequest> Requests { get; } = new();
        internal ConcurrentQueue<FlowCompletionRequest> Reviews { get; } = new();
        internal string Involvement = "indireto";
        internal string Action = "responder";
        internal bool Invalid;
        internal bool AllowGeneration;
        internal bool RespondInitially;
        internal bool BlockFirst;
        internal bool BlockAll;
        internal Func<CancellationToken, Task>? BeforeGeneration;
        internal string SeedState = Inactive;
        internal Func<CancellationToken, Task>? BeforeReview;
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Enqueue(request);
            var review = request.Inputs.TryGetProperty("reavaliacao", out _);
            if (review)
            {
                Reviews.Enqueue(request);
                if (BeforeReview is not null) await BeforeReview(token);
                if (Invalid) return "{}";
            }
            if (!review && request.NodeId.EndsWith("-presence"))
                return JsonSerializer.Serialize(new { acao = RespondInitially ? "responder" : "calar", continuar_acompanhando = SeedState != Inactive, motivo = "sem_contribuicao" });
            if ((review || RespondInitially) && AllowGeneration && request.NodeId.EndsWith("-generation"))
            {
                if (BeforeGeneration is not null) await BeforeGeneration(token);
                return Candidate("Agora entendi a brincadeira da lista.", request);
            }
            if (review && AllowGeneration && request.NodeId.EndsWith("-regeneration")) return Candidate("Agora ficou claro.", request);
            if (review && AllowGeneration && request.NodeId.Contains("validator"))
            {
                var fields = request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao");
                if (fields.ContainsKey("relevante_ao_topico")) fields["relevante_ao_topico"] = "sim";
                if ((BlockAll || BlockFirst && request.NodeId.Contains("validator-1")) && fields.ContainsKey("deprecia_experiencia_protegida"))
                    fields["deprecia_experiencia_protegida"] = "sim";
                return JsonSerializer.Serialize(fields);
            }
            return await new DirectParticipationRuleTests.Signals(review ? Involvement : "indireto", review ? Action : "calar")
                .CompleteAsync(request, token);
        }

        private static string Candidate(string text, FlowCompletionRequest request) =>
            request.ResponseSchema is { } schema && schema.GetProperty("properties").TryGetProperty("replyTo", out _)
                ? JsonSerializer.Serialize(new { texto = text, replyTo = (string?)null })
                : text;
    }

    internal sealed class Memory : IMemoryReader
    {
        internal int Calls;
        internal bool AllowEmpty;
        internal bool WithBoundary;
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (WithBoundary && query.Categories.Contains(MemoryCategory.Boundary))
                return Task.FromResult<IReadOnlyList<MemoryItem>>([new("fixture-boundary", query.ConversationNamespace,
                    "Helena pediu para Filomena não brincar nem provocar sobre trabalho.", MemoryCategory.Boundary,
                    query.Persons, "controlled_fixture")]);
            if (AllowEmpty) return Task.FromResult<IReadOnlyList<MemoryItem>>([]);
            throw new InvalidOperationException("Review cannot call memory.");
        }
    }

    internal sealed class Harness : IAsyncDisposable
    {
        internal readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        internal readonly SessionKey Session = new("review-run", "filomena", "whatsapp", "fixture-account", "synthetic-group@g.us");
        internal readonly Model Model = new();
        internal readonly Memory Memory = new();
        internal SessionFlowRuntime Runtime = null!;
        internal JsonElement Project;
        internal int ReviewCallbacks;
        internal bool Reviewing;

        internal static async Task<Harness> Start(string state = Inactive)
        {
            var h = new Harness();
            h.Model.SeedState = state;
            var p = DirectParticipationRuleTests.Project();
            p["project"]!["initialStateId"] = state;
            h.Project = JsonSerializer.SerializeToElement(p);
            h.Runtime = new(h.Clock, h.Model, h.Memory,
                (_, _) => { if (h.Reviewing) h.ReviewCallbacks++; return Task.CompletedTask; },
                (_, _) => { if (h.Reviewing) h.ReviewCallbacks++; return Task.CompletedTask; });
            h.Runtime.UpdateProject(new("review-project", 1, "review-v3", h.Project));
            await h.Admit("original", "Agora com 1000.");
            await h.Admit("clarification", "Era brincadeira, não precisa listar os números.");
            h.Reviewing = true;
            return h;
        }

        internal SessionMessageEnvelope Message(string id, string text) => new(id, Session.ChannelId, Session.AccountId,
            Session.NativeConversationId, Clock.GetUtcNow(), SessionMessageDirection.Inbound, SessionEntryKind.Group,
            JsonSerializer.SerializeToElement(new
            {
                texto = text,
                remetente = new { nome = "Helena", stableId = "fixture:helena" },
                canal = new { nome = "Synthetic", nativeId = Session.NativeConversationId },
                reply = id == "original" ? new { id = "quoted-pi", texto = "Aqui estão 100 casas de pi.", origem = "observed_provider_quote" } : null
            }));

        private async Task Admit(string id, string text)
        {
            Assert.Equal(SessionAdmissionStatus.Accepted, (await Runtime.ReceiveAsync(Session, Message(id, text), TestContext.Current.CancellationToken)).Status);
            Clock.Advance(TimeSpan.FromSeconds(3));
            await Runtime.WaitForIdleAsync(Session, TestContext.Current.CancellationToken);
            Assert.True(Runtime.ReadOutcomes(Session).Last().Success);
        }

        internal Task<SessionParticipationReviewResult> Review() => Runtime.ReviewParticipationAsync(Session, "original", Definition(), TestContext.Current.CancellationToken);
        public ValueTask DisposeAsync() => Runtime.DisposeAsync();
    }
}
