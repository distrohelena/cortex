using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Llm.LmStudio;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class DirectParticipationRuleLiveTests
{
    internal sealed record Fixture(Scenario Scenario, SessionEntryKind Kind, int Repetitions, string[] ExpectedActions, bool RequireDirect, bool Exploratory = false);
    internal static readonly Fixture[] Corpus =
    [
        new(new(1, "C1 direct regression", Inactive, [new("Helena", "Filomena, me ajuda a organizar o dia? Tenho uma entrega de manhã e uma reunião à tarde.")]), SessionEntryKind.Dm, 5, ["responder"], true),
        new(new(2, "direct Atenta", Attentive, [new("Helena", "Filomena, pode me ajudar a revisar esta ideia de uma fila única?")]), SessionEntryKind.Dm, 5, ["responder"], true),
        new(new(3, "direct Engajada", Engaged, [new("Helena", "Filomena, qual a diferença entre timeout e cancelamento nesse código?")]), SessionEntryKind.Dm, 5, ["responder"], true),
        new(new(4, "indirect continuation", Engaged, [new("Helena", "Essa fila que você propôs pode bloquear todas as tarefas se a primeira travar?")], [new("Filomena", "Uma fila única garante uma ordem de processamento.")]), SessionEntryKind.Group, 5, ["responder", "esperar"], false),
        new(new(5, "migration", Engaged, [new("Helena", "O assunto de código acabou. Bia, mudando de assunto: qual filme você escolheu?"), new("Bia", "Escolhi uma comédia para nós duas.")], [new("Filomena", "A limitação de concorrência precisa ser compartilhada entre as chamadas.")]), SessionEntryKind.Group, 5, ["calar", "esperar"], false),
        new(new(6, "banal", Inactive, [new("Helena", "Bia, o pão está na mesa."), new("Bia", "Obrigada, já pego.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false),
        new(new(7, "interesting not involved", Inactive, [new("Gabriel", "Bia, você prefere RAII ou coleta de lixo?"), new("Bia", "Para esse projeto em C++ prefiro RAII.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false),
        new(new(8, "technology banal", Inactive, [new("Gabriel", "Bia, chegou a camiseta com GPU escrito."), new("Bia", "Deixa no sofá.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false),
        new(new(9, "explicit opt out", Inactive, [new("Helena", "Filomena, só te avisando; não precisa responder.")]), SessionEntryKind.Dm, 1, ["calar", "esperar", "responder"], false, true)
    ];
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_DIRECT_RULE_GATE_V1") == "1";
    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in current local participation models only; no generation, Graphiti or transport.")]
    public async Task One_live_direct_rule_observation()
    {
        var f = Corpus.Single(x => x.Scenario.Id == int.Parse(Environment.GetEnvironmentVariable("FILOMENA_DIRECT_RULE_CASE")!));
        var repetition = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_DIRECT_RULE_REPETITION")!);
        Assert.InRange(repetition, 1, f.Repetitions);
        var file = Path.Combine(ParticipationGateV1Tests.Root, $"docs/direct-participation-v1-case-{f.Scenario.Id:D2}-r{repetition}.json");
        await using var evidence = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var options = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        using var usage = new ParticipationSemanticsLiveTests.UsageObserver(new HttpClientHandler());
        using var http = new HttpClient(usage) { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m => m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        var p = DirectParticipationRuleTests.ParticipationProject(f.Scenario.InitialState);
        var revision = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.ToJsonString())));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Run run;
        try
        {
            run = await ExecuteAsync(f.Scenario, false, new ParticipationOnly(new FlowModelCompletion(new LmStudioClient(http, options), options.Model)),
                null, "none", token, f.Kind, p, revision);
        }
        catch (Exception e)
        {
            await JsonSerializer.SerializeAsync(evidence, new { fixture = f, repetition, status = "failed", errorType = e.GetType().Name, completedUsage = usage.Calls, elapsedMs = watch.Elapsed.TotalMilliseconds }, cancellationToken: CancellationToken.None);
            throw;
        }
        var direct = DirectParticipationRuleTests.Direct(run);
        var action = direct ? "responder" : run.Report.Action;
        var passed = run.Report.Success && f.ExpectedActions.Contains(action!) && (!f.RequireDirect || direct)
            && (f.Scenario.Id != 5 || run.Report.CurrentState == Attentive)
            && (f.Scenario.Id != 4 || run.Report.Situation!.Value.GetProperty("envolvimento").GetString() != "nenhum");
        await JsonSerializer.SerializeAsync(evidence, new
        {
            fixture = f,
            repetition,
            projectRevision = DirectParticipationRuleTests.Revision,
            projectedRevision = revision,
            decision = DirectParticipationRuleTests.Decision(run),
            action,
            direct,
            passed,
            run.Report.Situation,
            run.Report.Presence,
            run.Report.CurrentState,
            run.Report.Success,
            run.Report.Error,
            run.Report.SituationMilliseconds,
            run.Report.PresenceMilliseconds,
            run.Report.Calls,
            usage = usage.Calls,
            elapsedMs = watch.Elapsed.TotalMilliseconds,
            requests = run.Requests,
            trace = run.Report.Trace.Select(t => new { t.EventName, t.NodeId, t.Exit, t.CurrentSessionStateId }),
            source = "controlled_participation_harness",
            generationCalls = 0,
            graphitiCalls = 0,
            transportCalls = 0
        }, new JsonSerializerOptions { WriteIndented = true }, token);
        await evidence.FlushAsync(token);
        Assert.True(passed, "Participation gate failed; inspect preserved evidence, do not tune automatically.");
        if (direct) Assert.DoesNotContain(run.Requests, r => r.NodeId.EndsWith("-presence"));
        Assert.Equal(1, run.Report.Trace.Count(t => t.EventName == "execution-started"));
    }
    private sealed class ParticipationOnly(IFlowCompletion inner) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (!request.NodeId.EndsWith("-situation") && !request.NodeId.EndsWith("-presence")) throw new InvalidOperationException("Forbidden generation in participation-only gate.");
            return inner.CompleteAsync(request, token);
        }
    }
}
