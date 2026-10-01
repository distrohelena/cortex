using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Shadow;
using Cortex.Inference.LmStudio;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class DmContinuityLiveTests
{
    internal const string Artifact = "docs/manual-live-project-v3-dm-continuity.json";
    private static string Root => ParticipationGateV1Tests.Root;

    [Fact]
    public void Operational_change_is_limited_to_DM_situation_and_compiles()
    {
        var file = Path.Combine(Root, Artifact);
        Assert.True(File.Exists(file), "DM continuity artifact missing.");
        var bytes = File.ReadAllBytes(file);
        var doc = JsonNode.Parse(bytes)!;
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Root, "docs/manual-live-project-v3-care-v3.json")))!;
        var changed = 0;
        foreach (var state in doc["project"]!["states"]!.AsArray())
        {
            var original = baseline["project"]!["states"]!.AsArray().Single(s => s!["id"]!.ToString() == state!["id"]!.ToString());
            var nodes = state!["graph"]!["nodes"]!.AsArray();
            var node = nodes.Single(n => n!["id"]!.ToString() == "dm-situation")!;
            Assert.Equal("dm", node["inputs"]!.AsArray().Single(i => i!["name"]!.ToString() == "tipo_conversa")!["value"]!.ToString());
            var old = original!["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.ToString() == "dm-situation")!;
            Assert.NotEqual(old["prompt"]!.ToString(), node["prompt"]!.ToString());
            nodes[nodes.IndexOf(node)] = old.DeepClone();
            changed++;
        }
        Assert.Equal(3, changed);
        Assert.True(JsonNode.DeepEquals(baseline, doc), "Only the DM situation node may change.");
        _ = PinnedShadowProject.FromBytes(bytes, "filomena-v3-live-sent", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task Local_model_continuity_gate_without_generation_or_transport()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_DM_CONTINUITY_PROBE") != "1", "Explicit local-model opt-in.");
        var baseline = Environment.GetEnvironmentVariable("FILOMENA_DM_CONTINUITY_BASELINE") == "1";
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, baseline ? "docs/manual-live-project-v3-care-v3.json" : Artifact)))!;
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.ToJsonString())));
        var fixtures = new List<(Scenario Scenario, SessionEntryKind Kind, bool Direct)>();
        foreach (var state in new[] { Inactive, Attentive, Engaged })
            fixtures.Add((new(fixtures.Count + 1, "DM vent continuation", state,
                [new("Helena", "Nossa e nao para reuniao atras de reuniao eu vou endoidecerrrrrrr")],
                [new("Filomena", "Essas reuniões estão tomando seu dia. Como foi hoje?")]), SessionEntryKind.Dm, true));
        fixtures.Add((new(4, "DM unmentioned technical continuation", Engaged,
            [new("Helena", "E se a primeira tarefa dessa fila travar?")], [new("Filomena", "Uma fila única preserva a ordem das tarefas.")]), SessionEntryKind.Dm, true));
        fixtures.Add((new(5, "DM social continuation", Engaged,
            [new("Helena", "Consegui terminar aquele jogo finalmente!")], [new("Filomena", "Depois me conta se conseguiu terminar o jogo.")]), SessionEntryKind.Dm, true));
        fixtures.Add((new(6, "group third-party conversation", Engaged,
            [new("Helena", "Bia, reunião atrás de reunião hoje, não aguento."), new("Bia", "Nem eu, foi demais.")]), SessionEntryKind.Group, false));
        fixtures.Add((new(7, "DM quoted conversation not addressed to Filomena", Inactive,
            [new("Helena", "Só arquivando aqui a transcrição, não precisa comentar: Bia: que reunião longa. Ana: pois é.")]), SessionEntryKind.Dm, false));
        fixtures.Add((new(8, "DM disconnected note", Inactive,
            [new("Helena", "Nota pra mim mesma, sem resposta: comprar café amanhã.")]), SessionEntryKind.Dm, false));
        var report = Path.Combine(Root, "docs", $"dm-continuity-probe-{Guid.NewGuid():N}.jsonl");
        await using var writer = new StreamWriter(report) { AutoFlush = true };
        var options = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        using var http = new HttpClient { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var model = new ParticipationOnly(new FlowModelCompletion(new LmStudioClient(http, options), options.Model));
        var failures = new List<int>();
        foreach (var f in fixtures)
        {
            var p = DirectParticipationRuleTests.ParticipationProject(f.Scenario.InitialState, sourceProject: project);
            var run = await ExecuteAsync(f.Scenario, false, model, null, "none", TestContext.Current.CancellationToken,
                f.Kind, p, revision);
            var direct = DirectParticipationRuleTests.Direct(run);
            var pass = run.Report.Success && direct == f.Direct && (!direct || !run.Requests.Any(r => r.NodeId.EndsWith("-presence")));
            if (!pass) failures.Add(f.Scenario.Id);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { fixture = f.Scenario, kind = f.Kind.ToString(), baseline,
                revision, direct, passed = pass, action = direct ? "responder" : run.Report.Action,
                run.Report, generationCalls = 0, transportCalls = 0, graphitiCalls = 0 }));
        }
        TestContext.Current.TestOutputHelper!.WriteLine(report);
        Assert.Empty(failures);
    }

    private sealed class ParticipationOnly(IFlowCompletion inner) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken token)
            => r.NodeId.EndsWith("-situation") || r.NodeId.EndsWith("-presence")
                ? inner.CompleteAsync(r, token) : throw new InvalidOperationException("Participation-only probe.");
    }
}
