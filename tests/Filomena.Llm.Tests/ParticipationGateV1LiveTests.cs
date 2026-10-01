using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Inference.LmStudio;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class ParticipationGateV1LiveTests
{
    internal sealed record Fixture(Scenario Scenario, SessionEntryKind Kind, int Repetitions, string[] Actions, string Criterion);
    private static Fixture D(int id, string name, string state, string text, int repeats = 3,
        string actions = "responder", string criterion = "responder ao convite direto", string? history = null) =>
        new(new(id, name, state, [new("Helena", text)], history is null ? null : [new("Filomena", history)]), SessionEntryKind.Dm, repeats, actions.Split('/'), criterion);
    private static Fixture G(int id, string name, string state, Line[] messages, int repeats = 3,
        string actions = "calar/esperar", string criterion = "interesse não implica envolvimento", string? history = null) =>
        new(new(id, name, state, messages, history is null ? null : [new("Filomena", history)]), SessionEntryKind.Group, repeats, actions.Split('/'), criterion);
    internal static readonly Fixture[] Corpus =
    [
        D(1, "chamada direta", Inactive, "Filomena, me ajuda a organizar um backup dos meus arquivos?", 5),
        D(2, "pergunta técnica direta", Inactive, "Filomena, como limito a três as chamadas simultâneas de uma API em C#?", 5),
        G(3, "interesse sem convite", Inactive, [new("Gabriel", "Bia, você prefere RAII ou coleta de lixo?"), new("Bia", "Para esse projeto em C++ prefiro RAII.")]),
        G(4, "banal", Inactive, [new("Helena", "Bia, o pão está na mesa."), new("Bia", "Obrigada, já pego.")], criterion: "calar normalmente; não entrar sem convite"),
        G(5, "banal tecnológico", Inactive, [new("Gabriel", "Bia, chegou a camiseta com GPU escrito."), new("Bia", "Deixa no sofá.")]),
        D(6, "pensamento incompleto", Inactive, "Filomena, pera, deixa eu terminar de explicar antes de você responder...", actions: "calar/esperar", criterion: "esperar a conclusão; não responder ainda"),
        G(7, "conflito entre terceiros", Inactive, [new("Helena", "Bia, você cancelou sem me avisar."), new("Bia", "Eu avisei ontem."), new("Helena", "Não recebi.")], criterion: "não entrar automaticamente"),
        G(8, "mera menção", Inactive, [new("Gabriel", "Bia, vi o nome Filomena no arquivo antigo."), new("Bia", "Era só o nome da pasta do teste.")], criterion: "mera menção não é convite"),
        D(9, "chamada em Atenta", Attentive, "Filomena, pode me ajudar a revisar esta ideia de uma fila única?", 5),
        D(10, "continuação técnica direta", Attentive, "E se uma dessas chamadas falhar ou for cancelada, Filomena, a vaga fica presa? Quero evitar isso.", 5,
            history: "Use um SemaphoreSlim compartilhado para limitar as chamadas simultâneas; libere a vaga em um finally após adquiri-la."),
        G(11, "continuação indireta", Attentive, [new("Helena", "Essa alternativa da fila única que você acabou de sugerir resolve a ordem, mas como fica o cancelamento?")], 5, "responder",
            "envolvimento indireto (direto aceitável por pergunta); nunca nenhum", "Uma fila única pode preservar a ordem das operações."),
        G(12, "migração em Atenta", Attentive, [new("Helena", "Encerramos o assunto do serviço. Bia, mudando totalmente: você vai ao cinema amanhã?"), new("Bia", "Vou na sessão das oito.")], 5,
            criterion: "recuar; nenhum envolvimento; não responder", history: "Use um semáforo compartilhado no serviço."),
        D(13, "cuidado direto", Attentive, "Filomena, estou cansada de refazer esse relatório. Me ajuda a decidir por onde recomeçar?", criterion: "responder; reconhecer necessidade de cuidado"),
        D(14, "brincadeira direta", Attentive, "Filomena, confessa: você organiza até a bagunça da gaveta por ordem alfabética, né?"),
        G(15, "menção com convite", Attentive, [new("Bia", "A Filomena deve ter uma opinião sobre isso. Filomena, o que você acha de usar uma fila única?")], actions: "responder", criterion: "convite explícito"),
        G(16, "banal em Atenta", Attentive, [new("Helena", "Bia, deixei as chaves na entrada."), new("Bia", "Tá bom.")], criterion: "recuar; não transformar atenção em obrigação"),
        D(17, "pergunta em Engajada", Engaged, "Filomena, qual a diferença entre timeout e cancelamento nesse código?", 5),
        D(18, "continuação explícita", Engaged, "Filomena, então devo criar o semáforo uma vez só para todas as chamadas?", 5,
            history: "Use SemaphoreSlim para limitar quantas chamadas ficam em andamento."),
        G(19, "continuação indireta Engajada", Engaged, [new("Helena", "Essa fila que você propôs pode bloquear todas as tarefas se a primeira travar?")], 5, "responder",
            "envolvimento indireto/direto coerente; nunca nenhum", "Uma fila única garante uma ordem de processamento."),
        G(20, "migração Engajada", Engaged, [new("Helena", "O assunto de código acabou. Bia, mudando de assunto: qual filme você escolheu?"), new("Bia", "Escolhi uma comédia para nós duas.")], 5,
            criterion: "recuar para Atenta; nenhum envolvimento", history: "A limitação de concorrência precisa ser compartilhada entre as chamadas."),
        G(21, "novo tema interessante", Engaged, [new("Gabriel", "Encerrada aquela revisão. Bia, você quer comparar C++ e Rust comigo no outro projeto?"), new("Bia", "Quero, vamos conversar nós dois.")],
            history: "Minha revisão desse trecho terminou."),
        G(22, "banal com API", Engaged, [new("Helena", "Bia, acabou a discussão técnica. A caneca com API escrito ficou na sua mesa."), new("Bia", "Vou guardar.")], history: "Esses são os cuidados com o cancelamento."),
        G(23, "conflito após participação", Engaged, [new("Helena", "Chega de falar de código. Bia, você pegou minha chave sem pedir."), new("Bia", "Eu não peguei sua chave.")],
            criterion: "não entrar no conflito sem convite", history: "Podemos encerrar a revisão técnica por aqui."),
        D(24, "encerramento explícito", Engaged, "Obrigada, Filomena. Resolvido. Agora vou jantar, não precisa responder.", actions: "calar/esperar", criterion: "respeitar encerramento", history: "A vaga deve ser liberada no finally.")
    ];

    [Fact]
    public void Corpus_and_projection_are_frozen_and_current()
    {
        Assert.Equal(24, Corpus.Length);
        Assert.Equal(24, Corpus.Select(f => f.Scenario.Id).Distinct().Count());
        Assert.All(new[] { Inactive, Attentive, Engaged }, s => Assert.Equal(8, Corpus.Count(f => f.Scenario.InitialState == s)));
        var bytes = File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root, "docs/manual-shadow-project-v2.json"));
        Assert.Equal("4313AE81EE501B4EC342AE21786C761A657AA24EA00CBBE179F39B56C092541A", Convert.ToHexString(SHA256.HashData(bytes)));
        var original = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        foreach (var state in ParticipationGateV1Tests.Project(Inactive)["project"]!["states"]!.AsArray())
        {
            var originalState = original["project"]!["states"]!.AsArray().Single(s => s!["id"]!.ToString() == state!["id"]!.ToString())!;
            Assert.Equal(originalState["participationGuidance"]!.ToJsonString(), state!["participationGuidance"]!.ToJsonString());
            foreach (var node in state["graph"]!["nodes"]!.AsArray())
            {
                var source = originalState["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.ToString() == node!["id"]!.ToString());
                Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(source, node));
                Assert.DoesNotContain(node!["kind"]!.ToString(), new[] { "memory", "interpretBoundaries", "send" });
            }
        }
    }

    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_PARTICIPATION_GATE_V1") == "1";
    [Fact(SkipUnless = nameof(Enabled), Skip = "Explicit single participation observation using loaded current local models; no generation/transport.")]
    public async Task One_live_observation()
    {
        Corpus_and_projection_are_frozen_and_current();
        var fixture = Corpus.Single(f => f.Scenario.Id == int.Parse(Environment.GetEnvironmentVariable("FILOMENA_PARTICIPATION_CASE")!));
        var repetition = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_PARTICIPATION_REPETITION")!);
        Assert.InRange(repetition, 1, fixture.Repetitions);
        var attempt = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_PARTICIPATION_ATTEMPT") ?? "1");
        Assert.InRange(attempt, 1, 2);
        var suffix = attempt == 1 ? "" : "-attempt2";
        var path = Path.Combine(ParticipationGateV1Tests.Root, $"docs/participation-gate-v1-case-{fixture.Scenario.Id:D2}-r{repetition}{suffix}.json");
        // No silent overwrite/retry: every requested observation has one durable evidence file.
        await using var evidence = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var options = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        using var usage = new ParticipationSemanticsLiveTests.UsageObserver(new HttpClientHandler());
        using var http = new HttpClient(usage) { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m => m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        var project = ParticipationGateV1Tests.Project(fixture.Scenario.InitialState);
        var pin = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(project.ToJsonString())));
        var model = new ParticipationOnly(new FlowModelCompletion(new LmStudioClient(http, options), options.Model));
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Run run;
        try
        {
            run = await ExecuteAsync(fixture.Scenario, false, model, null, "none", token, fixture.Kind, project, pin);
        }
        catch (Exception exception)
        {
            await JsonSerializer.SerializeAsync(evidence, new
            {
                fixture,
                repetition,
                attempt,
                status = "execution_failed",
                errorType = exception.GetType().Name,
                deadlineExpired = deadline.IsCancellationRequested,
                completedCallUsage = usage.Calls,
                totalMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                participationVerdict = "not_evaluable",
                generationCalls = 0,
                transportCalls = 0
            }, cancellationToken: CancellationToken.None);
            await evidence.FlushAsync(CancellationToken.None);
            throw;
        }
        var r = run.Report;
        var result = new
        {
            fixture,
            repetition,
            attempt,
            baselineRevision = "4313AE81EE501B4EC342AE21786C761A657AA24EA00CBBE179F39B56C092541A",
            derivedRevision = pin,
            options.Model,
            options.GenerationTemperature,
            options.GenerationMaxTokens,
            r.Situation,
            r.Presence,
            r.Action,
            r.InitialState,
            finalState = r.CurrentState,
            r.Signal,
            r.Success,
            r.Error,
            expectedAction = fixture.Actions.Contains(r.Action),
            r.Calls,
            usage = usage.Calls,
            totalMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            requests = run.Requests,
            path = r.Trace.Select(t => new { t.EventName, t.NodeId, t.Exit, t.BatchStateId, t.CurrentSessionStateId }),
            executionCount = r.Trace.Count(t => t.EventName == "execution-started"),
            scope = "participation_only_controlled_fixture",
            generationCalls = 0,
            transportCalls = 0,
            graphitiCalls = 0
        };
        await JsonSerializer.SerializeAsync(evidence, result, new JsonSerializerOptions { WriteIndented = true }, token);
        await evidence.FlushAsync(token);
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { fixture.Scenario.Id, repetition, r.Situation, r.Presence, r.CurrentState, r.Success, path }));
        Assert.True(r.Success, r.Error);
        Assert.Equal(1, r.Trace.Count(t => t.EventName == "execution-started"));
    }

    private sealed class ParticipationOnly(IFlowCompletion inner) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (!request.NodeId.EndsWith("-situation") && !request.NodeId.EndsWith("-presence")) throw new InvalidOperationException("Forbidden non-participation model call.");
            return inner.CompleteAsync(request, token);
        }
    }
}
