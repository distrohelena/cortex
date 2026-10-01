using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Inference;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;
using Cortex.Core.Tests.Flows;
using Xunit;

namespace Cortex.Core.Tests.Inference;

public sealed class HostBoundBrainCompatibilityTests
{
    private const string Inactive = "participation-inactive-v1";
    private const string Engaged = "participation-engaged-v1";
    private static readonly SessionKey Session = new("offline-bound-test", "filomena", "whatsapp", "fixture", "5511944558218@s.whatsapp.net");

    [Theory]
    [InlineData(Inactive)]
    [InlineData("participation-attentive-v1")]
    [InlineData(Engaged)]
    public async Task DirectRoutePreservedInAllThreeStates(string state)
    {
        var baseline = await Run(state, "direto", "allow", false);
        var bound = await Run(state, "direto", "allow", true);
        Assert.Equal(baseline.Nodes, bound.Nodes);
        Assert.Equal(baseline.State, bound.State);
        Assert.Equal(state, bound.State);
        Assert.DoesNotContain(bound.Nodes, n => n.EndsWith("-presence", StringComparison.Ordinal));
        Assert.Contains(bound.Trace, t => t.Exit == "direct_involvement_rule");
        Assert.Equal(2, bound.Nodes.Count(n => n.Contains("validator", StringComparison.Ordinal)));
        Assert.Contains(bound.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "shadowed");
        Assert.Contains(bound.Trace, t => t.EventName == "node-hypothetical-transition");
        Assert.All(bound.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        Assert.False(bound.Outcome.TransitionApplied);
        Assert.Contains(bound.Trace, t => t.NodeId == "dm-compiled-boundaries" && t.EventName == "node-completed");
        Assert.Contains(bound.Memory, q => q.Categories.Contains(MemoryCategory.Boundary));
    }

    [Theory]
    [InlineData("indireto")]
    [InlineData("nenhum")]
    public async Task NonDirectStillUsesPresence(string involvement)
    {
        var baseline = await Run(Inactive, involvement, "allow", false);
        var bound = await Run(Inactive, involvement, "allow", true);
        Assert.Equal(baseline.Nodes, bound.Nodes);
        Assert.Contains("dm-presence", bound.Nodes);
        Assert.DoesNotContain(bound.Nodes, n => n.EndsWith("-generation", StringComparison.Ordinal));
        Assert.DoesNotContain(bound.Trace, t => t.EventName == "node-outbound");
    }

    [Theory]
    [InlineData("first", true)]
    [InlineData("all", false)]
    public async Task RegenerationIsRecheckedAndFinalBlockNeverSends(string block, bool sends)
    {
        var baseline = await Run(Inactive, "direto", block, false);
        var bound = await Run(Inactive, "direto", block, true);
        Assert.Equal(baseline.Nodes, bound.Nodes);
        Assert.Contains("dm-regeneration", bound.Nodes);
        Assert.Equal(4, bound.Nodes.Count(n => n.Contains("validator", StringComparison.Ordinal)));
        Assert.Equal(sends, bound.Trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "shadowed"));
        Assert.False(bound.Outcome.TransitionApplied);
        Assert.All(bound.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        Assert.Equal(bound.Observations.Count, bound.Observations.Select(o => o.AttemptId).Distinct().Count());
        Assert.All(bound.Observations, o => Assert.Equal(bound.Outcome.ExecutionId, o.RunId));
    }

    [Fact]
    public async Task ReviewAndPreviewUseFreshBoundExecutionContexts()
    {
        var result = await Run(Inactive, "direto", "allow", true, reviewAndPreview: true);
        Assert.Equal(3, result.Observations.Select(o => o.RunId).Distinct().Count());
        Assert.All(result.Observations, o => Assert.Equal("v3-controlled", o.ConfigurationRevision));
        Assert.Single(result.History);
    }

    private static async Task<Result> Run(string state, string involvement, string block, bool useBound, bool reviewAndPreview = false)
    {
        var directory = Directory.CreateTempSubdirectory("host-bound-offline-");
        try
        {
            var root = Root();
            var bytes = File.ReadAllBytes(Path.Combine(root, "docs", "manual-shadow-project-v3.json"));
            Assert.Equal("47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E", Convert.ToHexString(SHA256.HashData(bytes)));
            var doc = JsonNode.Parse(bytes)!;
            doc["project"]!["initialStateId"] = state; // Explicit local setup, not an artifact edit.
            var project = new SessionFlowProject("frozen-brain-test", 3, "v3-controlled", JsonSerializer.SerializeToElement(doc));
            var roles = new OutboundBindings(["dm-response-boundaries"], ["dm-compiled-boundaries"],
                new Dictionary<string, string> { ["dm-generation"] = "texto", ["dm-regeneration"] = "texto" },
                ["dm-validator-1", "dm-validator-2"], Engaged);
            using var host = new ShadowOutboundHost(Path.Combine(directory.FullName, "journal.jsonl"), project.Document,
                roles, [Session], TransportSafetyCapabilities.Unproven);
            host.Rearm(OutboundMode.Shadow); // No transport implementation is supplied.
            var model = new Signals(involvement, block);
            var memory = new Memory();
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-27T12:00:00Z"));
            var observations = new ConcurrentQueue<InferenceAttemptObservation>();
            var (factory, _, _) = HostBoundSessionCompletionTests.BoundFactory(project, Session, model, observations.Enqueue);
            await using var runtime = useBound
                ? new SessionFlowRuntime(clock, factory, memory, outboundHost: host)
                : new SessionFlowRuntime(clock, model, memory, outboundHost: host);
            runtime.UpdateProject(project);
            await runtime.ReceiveAsync(Session, new("controlled-inbound", Session.ChannelId, Session.AccountId, Session.NativeConversationId,
                clock.GetUtcNow(), SessionMessageDirection.Inbound, SessionEntryKind.Dm,
                JsonSerializer.SerializeToElement(new { texto = "Filomena, me ajuda com o relatório do trabalho?", remetente = new { nome = "Helena", stableId = "whatsapp:5511944558218@s.whatsapp.net" }, canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null })), TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(3));
            await runtime.WaitForIdleAsync(Session, TestContext.Current.CancellationToken);
            var outcome = Assert.Single(runtime.ReadOutcomes(Session));
            Assert.True(outcome.Success, outcome.Error + " " + JsonSerializer.Serialize(runtime.ReadTrace(Session).Where(t => t.EventName == "node-failed")));
            Assert.Equal(OutboundMode.Shadow, host.EffectiveMode);
            if (reviewAndPreview)
            {
                var review = await runtime.ReviewParticipationAsync(Session, "controlled-inbound", new("v3-controlled",
                    ["dm-situation", "dm-presence"], [new("dm-direct-involvement", "direct_involvement_rule", ParticipationReviewDecision.Respond),
                        new("dm-action", "case-responder", ParticipationReviewDecision.Respond), new("dm-action", "case-esperar", ParticipationReviewDecision.Wait),
                        new("dm-action", "case-calar", ParticipationReviewDecision.Silence)]), TestContext.Current.CancellationToken);
                Assert.Equal(ParticipationReviewStatus.Completed, review.Status);
                var preview = await runtime.PreviewResponseAsync(Session, review, null, TestContext.Current.CancellationToken);
                Assert.Equal(ParticipationReviewStatus.Completed, preview.Status);
                Assert.Single(runtime.ReadOutcomes(Session));
            }
            // Multi-detectors execute concurrently: compare identity multisets, not scheduler ordering.
            return new(model.Calls.Select(r => r.NodeId).Order(StringComparer.Ordinal).ToArray(), runtime.ReadSession(Session)!.CurrentStateId,
                outcome, runtime.ReadTrace(Session), runtime.ReadHistory(Session), memory.Queries.ToArray(), observations.ToArray());
        }
        finally { directory.Delete(true); }
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "docs", "manual-shadow-project-v3.json"))) return dir.FullName;
        throw new FileNotFoundException("Frozen v3 artifact not found.");
    }

    private sealed record Result(string[] Nodes, string State, SessionFlowOutcome Outcome,
        IReadOnlyList<SessionTraceEvent> Trace, IReadOnlyList<SessionHistoryEntry> History,
        MemoryQuery[] Memory, IReadOnlyList<InferenceAttemptObservation> Observations);

    private sealed class Memory : IMemoryReader
    {
        internal ConcurrentQueue<MemoryQuery> Queries { get; } = new();
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken)
        {
            Queries.Enqueue(query);
            return Task.FromResult<IReadOnlyList<MemoryItem>>(query.Categories.Contains(MemoryCategory.Boundary)
                ? [new("controlled-source", query.ConversationNamespace, "Helena pediu para Filomena não brincar nem provocar sobre trabalho.",
                    MemoryCategory.Boundary, query.Persons, "controlled_fixture")] : []);
        }
    }

    private sealed class Signals(string involvement, string block) : IFlowCompletion
    {
        internal ConcurrentQueue<FlowCompletionRequest> Calls { get; } = new();
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Calls.Enqueue(request);
            if (request.NodeId.EndsWith("-situation", StringComparison.Ordinal))
                return Task.FromResult(JsonSerializer.Serialize(new { ambiente = "casual", envolvimento = involvement, ritmo = "aberto", necessita_cuidado = false, oportunidade_social = "fraca", resumo = "Helena conversa com Filomena." }));
            if (request.NodeId.EndsWith("-presence", StringComparison.Ordinal))
                return Task.FromResult(JsonSerializer.Serialize(new { acao = "calar", continuar_acompanhando = false, motivo = "sem_contribuicao" }));
            if (request.NodeId.Contains("validator", StringComparison.Ordinal))
            {
                var fields = request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao");
                if (fields.ContainsKey("relevante_ao_topico")) fields["relevante_ao_topico"] = "sim";
                if (fields.ContainsKey("deprecia_experiencia_protegida") && (block == "all" || block == "first" && request.NodeId.Contains("validator-1", StringComparison.Ordinal)))
                    fields["deprecia_experiencia_protegida"] = "sim";
                return Task.FromResult(JsonSerializer.Serialize(fields));
            }
            return Task.FromResult("Vamos corrigir a versão do relatório.");
        }
    }
}
