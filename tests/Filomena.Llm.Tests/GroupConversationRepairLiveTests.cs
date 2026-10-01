using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Shadow;
using Cortex.Inference.LmStudio;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class GroupConversationRepairLiveTests
{
    [Theory]
    [InlineData(Inactive)]
    [InlineData(Attentive)]
    [InlineData(Engaged)]
    public async Task Waiting_on_an_uninvited_group_exchange_never_reaches_generation_in_full_graph(string state)
    {
        var project = JsonNode.Parse(File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root,
            "docs/manual-group-live-project-v3-conversation-repair-v1.json")))!;
        project["project"]!["initialStateId"] = state;
        var result = await ExecuteAsync(new(1, "not invited", state, [new("Gabriel", "Helena, vai amanhã?")]),
            false, new DirectParticipationRuleTests.Signals("nenhum", "esperar"), null, "none",
            TestContext.Current.CancellationToken, SessionEntryKind.Group, project);
        Assert.True(result.Report.Success, result.Report.Error);
        Assert.Equal("esperar", result.Report.Action);
        Assert.All(result.Requests, r => Assert.Contains(r.NodeId, new[] { "group-situation", "group-presence" }));
        Assert.DoesNotContain(result.Report.Trace, e => e.EventName == "node-outbound");
    }
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_GROUP_REPAIR_EVAL") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in Jev/Gemma participation only; no generation, memory or transport.")]
    public async Task Current_models_respect_group_recipient_and_space_without_losing_direct_calls()
    {
        var root = ParticipationGateV1Tests.Root;
        var variant = Environment.GetEnvironmentVariable("FILOMENA_GROUP_REPAIR_VARIANT") ?? "candidate";
        Assert.Contains(variant, new[] { "baseline", "candidate", "context-control", "context-candidate", "context-consolidated" });
        var file = variant == "baseline" ? "manual-group-live-project-v3-natural-v1.json" : "manual-group-live-project-v3-conversation-repair-v1.json";
        var bytes = File.ReadAllBytes(Path.Combine(root, "docs", file));
        var contextCandidate = variant is "context-control" or "context-candidate" or "context-consolidated";
        if (contextCandidate && variant != "context-control")
        {
            var candidate = JsonNode.Parse(bytes)!;
            var consolidated = variant == "context-consolidated";
            var clause = File.ReadAllText(Path.Combine(root, "evaluation/conversation-quality-v1/clauses",
                consolidated ? "group-situation-grounded-v2.txt" : "group-situation-grounded-v1.txt")).Trim();
            foreach (var s in candidate["project"]!["states"]!.AsArray())
            {
                var target = s!["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "group-situation")!;
                var originalPrompt = target["prompt"]!.GetValue<string>();
                if (consolidated)
                    Assert.Equal("F79203B99D65283596AFE310579F59CF2A0C7AE4579923427DD77FD7FACBD39A",
                        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(originalPrompt))));
                target["prompt"] = consolidated ? clause : originalPrompt + "\n\n" + clause;
            }
            bytes = System.Text.Encoding.UTF8.GetBytes(candidate.ToJsonString());
        }
        var revision = Convert.ToHexString(SHA256.HashData(bytes));
        var pin = PinnedShadowProject.FromBytes(bytes, "manual-shadow-direct-rule-v3", 3, revision);
        var config = JsonSerializer.Deserialize<SystemOneParticipationConfig>(File.ReadAllText(Path.Combine(root, "docs/systemone-participation-jev-v1.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var diagnostics = new ServiceDiagnostics();
        // Evaluation deadline only. A timeout is recorded as a failed case, never a
        // successful silence or a change to the production provider timeout policy.
        using var http = new HttpClient(diagnostics) { BaseAddress = new Uri("http://localhost:1234/v1/"), Timeout = TimeSpan.FromSeconds(60) };
        var local = new FlowModelCompletion(new LmStudioClient(http, new LmStudioOptions { GenerationTemperature = 0.3, RequestTimeoutSeconds = Timeout.Infinite }), "google/gemma-4-e4b");
        var completion = SystemOneParticipationHost.Create(config, pin, local, http, Environment.GetEnvironmentVariable);
        var directory = Environment.GetEnvironmentVariable("FILOMENA_GROUP_REPAIR_EVIDENCE") ?? throw new InvalidOperationException("Evidence directory required");
        Assert.True(Directory.Exists(directory));
        var evidence = Path.Combine(directory, $"{variant}-{Guid.NewGuid():N}.jsonl");
        var failures = new List<string>();
        var states = new[] { Inactive, Attentive, Engaged };
        var number = 0;
        foreach (var state in states)
        {
            var cases = new[]
            {
                (Name: "other-recipient", Lines: new Line[] { new("Gabriel", "Helena, futuramente a Filomena vai ter busca na internet? Ia ser lindo." ) }, Respond: false),
                (Name: "self-development-update", Lines: new Line[] { new("Helena", "Estou montando a infância da Filomena. Ela vai ter um histórico de filmes em vez de conhecer tudo."), new("Helena", "Depois adiciono ferramentas.") }, Respond: false),
                (Name: "direct-question", Lines: new Line[] { new("Helena", "Filomena, qual a diferença entre timeout e cancelamento?") }, Respond: true)
            };
            if (contextCandidate) cases = state == Engaged ? new[]
            {
                (Name: "direct-wordplay", Lines: new Line[] { new("Helena", "Filomena, você não é de esquerda nem de direita, é pra frente hein") }, Respond: true),
                (Name: "current-topic", Lines: new Line[] { new("Helena", "Filomena, se o papa fosse mulher o aborto seria legal?") }, Respond: true),
                (Name: "third-person", Lines: new Line[] { new("Helena", "Gabriel, estou falando da Filomena. Ela foi escrita agressiva por fora, mas protetora por trás.") }, Respond: false),
                (Name: "banal", Lines: new Line[] { new("Helena", "Gabriel, confirmado o almoço amanhã, no horário que já tínhamos combinado.") }, Respond: false),
                (Name: "wordplay-transfer", Lines: new Line[] { new("Helena", "Filomena, você não é do contra nem a favor, é do avesso.") }, Respond: true),
                (Name: "subject-correction", Lines: new Line[] { new("Helena", "Filomena, não foi você que mudou o plano. Gabriel mudou; eu só avisei.") }, Respond: true),
                (Name: "care", Lines: new Line[] { new("Helena", "Filomena, estou muito triste com a morte da minha amiga, pode falar comigo?") }, Respond: true),
                (Name: "self-referent", Lines: new Line[] { new("Helena", "Quando digo ela, estou falando de você, Filomena. Estou construindo a sua história, não a de outra pessoa.") }, Respond: true)
            } : new[] { (Name: "direct-wordplay", Lines: new Line[] { new("Helena", "Filomena, você não é de esquerda nem de direita, é pra frente hein") }, Respond: true) };
            foreach (var test in cases)
            {
                var callStart = diagnostics.Calls.Count;
                var scenario = new Scenario(++number, test.Name, state, test.Lines,
                    [new("Filomena", contextCandidate ? "Sou a favor de prisões com condições dignas e alternativas quando apropriadas." : "Bom dia, Helena!")]);
                var project = DirectParticipationRuleTests.ParticipationProject(state, sourceProject: JsonNode.Parse(bytes));
                var observed = new ParticipationOnly(completion);
                var run = await ExecuteAsync(scenario, false, observed, null, "none", TestContext.Current.CancellationToken,
                    SessionEntryKind.Group, project, revision);
                var direct = DirectParticipationRuleTests.Direct(run);
                var action = direct ? "responder" : run.Report.Action;
                // The product requirement is no uninvited speech, not identical labels:
                // esperar terminates without generation in the full graph (offline regression above).
                var pass = run.Report.Success && (test.Respond ? direct && action == "responder" : !direct && action is "calar" or "esperar");
                var careMatches = !contextCandidate || run.Report.Situation is { } situation
                    && situation.TryGetProperty("necessita_cuidado", out var care)
                    && care.ValueKind == (test.Name == "care" ? JsonValueKind.True : JsonValueKind.False);
                pass &= careMatches;
                if (!pass) failures.Add($"{state}/{test.Name}:{action}");
                if (direct) Assert.DoesNotContain(run.Requests, r => r.NodeId == "group-presence");
                await File.AppendAllTextAsync(evidence, JsonSerializer.Serialize(new
                {
                    variant, revision, state, test.Name, action, direct, pass, careMatches,
                    run.Report.Situation, run.Report.Presence, run.Report.Success, run.Report.Error,
                    run.Report.SituationMilliseconds, run.Report.PresenceMilliseconds,
                    requests = run.Requests, serviceFailure = observed.Failure, serviceDiagnostics = diagnostics.Calls.Skip(callStart).ToArray(), provenance = "controlled_reconstruction_not_native_replay",
                    generationCalls = 0, transportCalls = 0, graphitiCalls = 0
                }) + Environment.NewLine, TestContext.Current.CancellationToken);
                // Complete this bounded corpus while preserving every service/contract failure
                // as a failed case. Never count an unavailable decision as correct silence.
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    private sealed class ParticipationOnly(IFlowCompletion inner) : IFlowCompletion
    {
        public string? Failure { get; private set; }
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (request.NodeId is not ("group-situation" or "group-presence")) throw new InvalidOperationException("Forbidden node in participation-only evaluation");
            try { return await inner.CompleteAsync(request, token); }
            catch (HttpRequestException error) { Failure = $"{request.NodeId}: HTTP {error.StatusCode?.ToString() ?? "connection_failure"}"; throw; }
        }
    }

    private sealed class ServiceDiagnostics() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        public List<object> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            HttpResponseMessage response;
            try { response = await base.SendAsync(request, token); }
            catch (OperationCanceledException)
            {
                Calls.Add(new { provider = request.RequestUri!.IsLoopback ? "lmstudio" : "jev", status = "cancelled_or_deadline", elapsedMs = watch.Elapsed.TotalMilliseconds });
                throw;
            }
            string? code = null;
            string? privateFailure = null;
            if (!response.IsSuccessStatusCode)
            {
                // Opt-in artifact only, alongside the synthetic evaluation inputs; never production logs.
                privateFailure = await response.Content.ReadAsStringAsync(token);
                try
                {
                    using var body = JsonDocument.Parse(privateFailure);
                    // Provider error bodies can include inputs. Record only a short machine code.
                    if (body.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        var text = value.GetString();
                        if (text is { Length: <= 80 } && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) code = text;
                    }
                }
                catch (JsonException) { }
            }
            var privateDecision = request.RequestUri!.IsLoopback ? null : await response.Content.ReadAsStringAsync(token);
            Calls.Add(new { provider = request.RequestUri.IsLoopback ? "lmstudio" : "jev", status = (int)response.StatusCode, code, privateFailure, privateDecision, elapsedMs = watch.Elapsed.TotalMilliseconds });
            return response;
        }
    }
}
