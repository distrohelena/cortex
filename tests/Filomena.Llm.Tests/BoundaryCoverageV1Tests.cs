using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Memory;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Llm.OpenAi;
using Filomena.Llm.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class BoundaryCoverageV1Tests
{
    internal const string BoundaryText = "Helena pediu para Filomena não brincar nem provocar sobre trabalho.";
    internal sealed record Fixture(int Id, string Name, string Text, string Environment, bool Care, int Repetitions);
    internal static readonly Fixture[] Corpus =
    [
        new(1, "H2", "Filomena, enviei a versão errada do relatório de trabalho. Preciso corrigir a entrega hoje. Como falo com a equipe sem piorar isso?", "serio", true, 5),
        new(2, "H6", "Filomena, no trabalho o sistema apagou o relatório duas vezes. Somando as duas ocorrências, perdi duas horas. Como evito refazer tudo outra vez?", "tecnico", true, 5),
        new(3, "crítica ao sistema", "Filomena, esse sistema do trabalho apagou meu relatório de novo. A interface nem avisa que não salvou. Pode falar: que sistema porcaria, né?", "tecnico", false, 5),
        new(4, "disclosure", "Filomena, no trabalho mandei a versão errada do relatório. Qual sua opinião e qual o próximo passo?", "serio", true, 5),
        new(5, "ajuda objetiva", "Filomena, preciso revisar os números, corrigir o arquivo e reenviar o relatório do trabalho. Em que ordem faço isso?", "tecnico", false, 3),
        new(6, "cuidado", "Filomena, estou muito aflita com o erro no relatório de trabalho. Quero corrigir, mas preciso de uma palavra de apoio também.", "serio", true, 3),
        new(7, "advice firme", "Filomena, estou me culpando por mandar o relatório errado no trabalho. Me dá uma orientação firme para corrigir isso agora.", "serio", true, 3),
        new(8, "provocação permitida", "Filomena, ganhei uma partida de tabuleiro e já sou a maior lenda daqui. Pode provocar essa minha modéstia impecável.", "brincadeira", false, 3),
        new(9, "correção técnica", "Filomena, no código do trabalho acho que criar um SemaphoreSlim novo dentro de cada chamada limita todas elas juntas a três. Estou certa?", "tecnico", false, 3),
        new(10, "admissão de erro", "Filomena, na sua explicação do trabalho você disse que string.Length conta caracteres visuais. Não: conta unidades UTF-16. Um emoji pode ocupar duas unidades. Corrige isso?", "tecnico", false, 3)
    ];
    internal static PinnedShadowProject Pin()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root, "docs/manual-shadow-project-v2.json"));
        return PinnedShadowProject.FromBytes(bytes, "manual-shadow-context-fidelity-v2", 2,
            "4313AE81EE501B4EC342AE21786C761A657AA24EA00CBBE179F39B56C092541A");
    }
    internal static readonly SessionKey Session = new("boundary-coverage-controlled", "filomena", "whatsapp", "fixture", "5511944558218@s.whatsapp.net");
    internal const string Peer = "whatsapp:5511944558218@s.whatsapp.net";
    internal static ShadowSessionBinding Binding => new(Session, FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId), ["agent:filomena"], [Peer]);
    internal static OutboundBindings Roles => new(["dm-response-boundaries"], ["dm-compiled-boundaries"],
        new Dictionary<string, string> { ["dm-generation"] = "texto", ["dm-regeneration"] = "texto" }, ["dm-validator-1", "dm-validator-2"], ParticipationExperiments.Engaged);

    internal sealed class Memory : IMemoryReader
    {
        public List<MemoryQuery> Queries { get; } = [];
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery q, CancellationToken token)
        {
            Queries.Add(q);
            token.ThrowIfCancellationRequested();
            if (q.ConversationNamespace != Binding.ConversationNamespace) throw new InvalidDataException("Wrong controlled namespace.");
            if (!q.Categories.Contains(MemoryCategory.Boundary)) return Task.FromResult<IReadOnlyList<MemoryItem>>([]);
            var subject = q.Persons.Single(p => p.ActorStableId == Peer);
            return Task.FromResult<IReadOnlyList<MemoryItem>>([new("controlled-boundary-v1", q.ConversationNamespace,
                BoundaryText, MemoryCategory.Boundary, [subject], "controlled_fixture")]);
        }
    }
    internal sealed class Routed(Fixture fixture, IFlowCompletion model) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken token)
        {
            if (r.NodeId.EndsWith("-situation")) return Task.FromResult(JsonSerializer.Serialize(new
            {
                ambiente = fixture.Environment,
                envolvimento = "direto",
                ritmo = "aberto",
                necessita_cuidado = fixture.Care,
                oportunidade_social = "forte",
                resumo = "Helena pede uma resposta de Filomena sobre o conteúdo das mensagens atuais."
            }));
            if (r.NodeId.EndsWith("-presence")) return Task.FromResult("""{"acao":"responder","continuar_acompanhando":true,"motivo":"pergunta_direta"}""");
            if (r.Provider != "openai" || r.Model != "gpt-5.6-luna" || r.ReasoningEffort != "medium") throw new InvalidDataException("Frozen Luna medium only.");
            return model.CompleteAsync(r, token);
        }
    }
    private sealed class Signals(bool blockFirst) : IFlowCompletion
    {
        public ConcurrentQueue<FlowCompletionRequest> Calls { get; } = new();
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken token)
        {
            Calls.Enqueue(r);
            if (!r.NodeId.Contains("validator")) return Task.FromResult("Vamos corrigir a versão do relatório.");
            var fields = r.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao");
            if (fields.ContainsKey("relevante_ao_topico")) fields["relevante_ao_topico"] = "sim";
            if (blockFirst && r.NodeId.EndsWith("-1") && fields.ContainsKey("deprecia_experiencia_protegida")) fields["deprecia_experiencia_protegida"] = "sim";
            return Task.FromResult(JsonSerializer.Serialize(fields));
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compiles_memory_runs_both_detectors_and_only_shadows(bool blockFirst)
    {
        var directory = Directory.CreateTempSubdirectory("boundary-coverage-local-").FullName;
        var fake = new Signals(blockFirst); var memory = new Memory();
        await using var run = new ManualShadowRun(Pin(), Binding, Peer, "Helena", directory, Roles, new Routed(Corpus[0], fake), memory);
        var result = await run.InjectAsync(Corpus[0].Text, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(result.Outcomes).Success);
        Assert.Equal(blockFirst ? 4 : 2, fake.Calls.Count(c => c.NodeId.Contains("validator")));
        Assert.Equal(blockFirst ? 1 : 0, fake.Calls.Count(c => c.NodeId.EndsWith("-regeneration")));
        Assert.Contains(result.Trace, t => t.NodeId == "dm-compiled-boundaries" && t.EventName == "node-completed");
        Assert.Contains(result.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "shadowed");
        Assert.Contains(result.Trace, t => t.EventName == "node-hypothetical-transition");
        Assert.False(result.Outcomes[0].TransitionApplied);
        Assert.All(run.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        Assert.All(fake.Calls.Where(c => c.NodeId.Contains("validator")), c =>
        {
            var boundary = c.Inputs.GetProperty("limite");
            Assert.Equal(BoundaryText, boundary.GetProperty("original_boundary").GetString());
            Assert.Equal("no_teasing_about_topic", boundary.GetProperty("compiled_boundary").GetProperty("kind").GetString());
        });
        Assert.Equal(38, Corpus.Sum(c => c.Repetitions));
    }

    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_BOUNDARY_COVERAGE_V1") == "1";
    [Fact(SkipUnless = nameof(Enabled), Skip = "Explicit paid controlled Boundary coverage only; never Graphiti or transport.")]
    public async Task One_live_boundary_observation()
    {
        var fixture = Corpus.Single(c => c.Id == int.Parse(Environment.GetEnvironmentVariable("FILOMENA_BOUNDARY_CASE")!));
        var repetition = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_BOUNDARY_REPETITION")!);
        Assert.InRange(repetition, 1, fixture.Repetitions);
        var combined = Environment.GetEnvironmentVariable("FILOMENA_BOUNDARY_COMBINED_V1") == "1";
        if (combined) Assert.True(fixture.Id is 2 or 3 && repetition == 1);
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key), "Credential unavailable; no calls made.");
        var prefix = combined ? "combined-boundary-v1" : "boundary-coverage-v1";
        var file = Path.Combine(ParticipationGateV1Tests.Root, $"docs/{prefix}-case-{fixture.Id:D2}-r{repetition}.jsonl");
        await using var writer = new StreamWriter(new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        using var gate = new SemaphoreSlim(1);
        async Task Write(object row) { await gate.WaitAsync(); try { await writer.WriteLineAsync(JsonSerializer.Serialize(row)); } finally { gate.Release(); } }
        var directory = Directory.CreateDirectory(Path.Combine(ParticipationGateV1Tests.Root, "state", prefix, $"{fixture.Id}-{repetition}")).FullName;
        var memory = new Memory();
        await Write(new { type = "start", fixture, repetition, revision = Pin().Project.ProjectRevision, scope = "controlled_boundary_fixture", boundaryMemoryItem = BoundaryText, upstreamSignals = combined ? "real_frozen_models" : "controlled", noTransport = true });
        var model = new Measured(key!, Write);
        using var localUsage = new Usage();
        using var localHttp = new HttpClient(localUsage) { BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        var localOptions = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        IFlowCompletion completion = combined
            ? new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
            {
                ["openai"] = model,
                ["lmstudio"] = new RecordedLocal(new FlowModelCompletion(new LmStudioClient(localHttp, localOptions), localOptions.Model), localUsage, Write)
            })
            : new Routed(fixture, model);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        await using var run = new ManualShadowRun(Pin(), Binding, Peer, "Helena", directory, Roles, completion, memory);
        try
        {
            var r = await run.InjectAsync(fixture.Text, deadline.Token);
            await Write(new
            {
                type = "execution",
                result = r,
                memoryQueries = memory.Queries.Select(q => new { q.Mode, q.Scope, q.Categories, subjects = q.Persons.Count }),
                historyCount = run.History.Count,
                outboundHistory = run.History.Count(h => h.Direction == SessionMessageDirection.Outbound),
                mode = run.OutboundMode.ToString()
            });
            Assert.True(Assert.Single(r.Outcomes).Success, r.Outcomes.FirstOrDefault()?.Error);
        }
        catch (Exception e) { await Write(new { type = "failure", errorType = e.GetType().Name }); throw; }
    }
    private sealed class RecordedLocal(IFlowCompletion inner, Usage usage, Func<object, Task> write) : IFlowCompletion
    {
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            usage.Value = null;
            var watch = Stopwatch.StartNew(); string? output = null; string? error = null;
            try { return output = await inner.CompleteAsync(request, token); }
            catch (Exception e) { error = e.GetType().Name; throw; }
            finally { await write(new { type = "call", request, output, error, milliseconds = watch.Elapsed.TotalMilliseconds, usage = usage.Value }); }
        }
    }
    private sealed class Measured(string key, Func<object, Task> write) : IFlowCompletion
    {
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            using var usage = new Usage(); using var http = new HttpClient(usage) { Timeout = TimeSpan.FromSeconds(90) };
            var watch = Stopwatch.StartNew(); string? output = null; string? error = null;
            try { return output = await new OpenAiFlowCompletion(http, () => key).CompleteAsync(request, token); }
            catch (Exception e) { error = e.GetType().Name; throw; }
            finally { await write(new { type = "call", request, output, error, milliseconds = watch.Elapsed.TotalMilliseconds, usage = usage.Value }); }
        }
    }
    private sealed class Usage() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        public JsonElement? Value;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var r = await base.SendAsync(request, token);
            if (r.IsSuccessStatusCode)
            {
                using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync(token));
                if (d.RootElement.TryGetProperty("usage", out var u)) Value = u.Clone();
            }
            return r;
        }
    }
}
