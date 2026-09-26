using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Llm.LmStudio;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ContextInterpretationPipelineLiveTests
{
    [Fact]
    public async Task Synthetic_natural_project_runs_real_models_with_shadow_only_transport_and_explicit_empty_memory()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_IRONY_PIPELINE_LIVE") != "1", "Explicit bounded synthetic pipeline experiment.");
        var scenario = Environment.GetEnvironmentVariable("FILOMENA_IRONY_PIPELINE_SCENARIO") ?? "initial";
        Assert.Contains(scenario, new[] { "initial", "late", "late-invitation" });
        var directory = Environment.GetEnvironmentVariable("FILOMENA_IRONY_SYNTHETIC_OUTPUT") ?? throw new InvalidOperationException("Output required.");
        Assert.True(Path.IsPathFullyQualified(directory) && Directory.Exists(directory));
        var replayPath = Environment.GetEnvironmentVariable("FILOMENA_IRONY_PIPELINE_REPLAY");
        var replayRows = replayPath is null ? null : File.ReadLines(replayPath)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key));
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root,
            "docs/manual-group-live-project-v3-natural-v1.json")))!;
        foreach (var state in project["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray().Where(n => n!["kind"]!.ToString() == "accumulate"))
                node!["settings"]!["quietSeconds"] = 0.001;
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(project)));
        var pin = new PinnedShadowProject(new("synthetic-irony-natural", 1, revision, JsonSerializer.SerializeToElement(project)));
        // Replay the synthetic identity too: analyzer evidence refs are intentionally run-bound.
        var runId = replayRows is null ? Guid.NewGuid().ToString("N") : replayRows
            .First(row => row.GetProperty("kind").GetString() == "interaction")
            .GetProperty("result").GetProperty("Trace")[0].GetProperty("Session").GetProperty("RuntimeId").GetString()!;
        var session = new SessionKey(runId, "filomena", "whatsapp", "synthetic-account", "synthetic-group@g.us");
        const string peer = "whatsapp:5511000000000@s.whatsapp.net";
        var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId),
            ["synthetic:self"], [peer], SessionEntryKind.Group);
        var roles = new OutboundBindings(["group-response-boundaries"], ["group-compiled-boundaries"],
            new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
            ["group-validator-1", "group-validator-2"], "participation-engaged-v1");
        using var usage = new ContextInterpretationSyntheticLiveTests.Usage();
        using var remoteHttp = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        using var localHttp = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        var options = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        var provider = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["openai"] = new OpenAiFlowCompletion(remoteHttp, () => key),
            ["lmstudio"] = new FlowModelCompletion(new LmStudioClient(localHttp, options), options.Model)
        });
        var path = Path.Combine(directory, $"pipeline-{runId}.jsonl");
        await using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        Queue<JsonElement>? replay = null;
        if (replayRows is not null)
        {
            Assert.Equal("late-invitation", scenario);
            Assert.All(replayRows.Where(row => row.GetProperty("kind").GetString() == "interaction"), row =>
            {
                Assert.Equal("late-invitation", row.GetProperty("scenario").GetString());
                Assert.False(row.GetProperty("transportAvailable").GetBoolean());
                Assert.Equal("controlled_empty", row.GetProperty("memoryCoverage").GetString());
            });
            replay = new(replayRows.Where(row => row.GetProperty("kind").GetString() == "completion"));
        }
        var completion = new Recording(provider, usage, writer, replay);
        var memory = new SessionParticipationReviewTests.Memory { AllowEmpty = true };
        var journal = Directory.CreateDirectory(Path.Combine(directory, "journal"));
        await using var run = new ObservedShadowRun(pin, binding, journal.FullName, roles, completion, memory, "synthetic-epoch", 0);
        var config = ContextInterpretationHostTests.ReadOperationalConfig(true);
        ContextInterpretationHost.Configure(config with { Participation = config.Participation with { ProjectRevision = revision } }, pin, binding, run, completion);
        string[] inputs = scenario == "late-invitation" ?
        [
            "Sensacional esse plano.",
            "Filomena, aquele sensacional foi ironia sobre a ideia de apagar todos os backups. Agora que expliquei, responde àquela provocação: você também achou essa ideia ótima?",
            "Isso, era deboche da ideia, não uma instrução para apagar nada. Já descartamos o plano, assunto encerrado."
        ] : scenario == "late" ?
        [
            "Sensacional esse plano.",
            "Aquele sensacional foi ironia sobre apagar todos os backups. Ainda estou discutindo essa ideia, não elogiei o plano.",
            "Já descartamos a ideia de apagar os backups. Assunto encerrado, agora vamos escolher um filme."
        ] :
        [
            "Filomena, que maravilha: o aplicativo apagou meu rascunho de novo. Estou sendo irônica, não quero um tutorial.",
            "Já recuperei o rascunho. Resolvido, vamos mudar de assunto.",
            "Filomena, agora é sério: ele recuperou tudo automaticamente e fiquei feliz. Você também comemoraria?"
        ];
        for (var index = 0; index < inputs.Length; index++)
        {
            var id = $"synthetic:{index + 1}";
            var message = new SessionMessageEnvelope(id, session.ChannelId, session.AccountId, session.NativeConversationId,
                DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Group, JsonSerializer.SerializeToElement(new
                {
                    texto = inputs[index],
                    remetente = new { nome = "Pessoa fictícia", stableId = peer },
                    canal = new { nome = "Grupo sintético", nativeId = session.NativeConversationId },
                    reply = (object?)null
                }));
            var started = Stopwatch.GetTimestamp();
            var result = await run.AcceptAsync([new("synthetic-epoch", index + 1, session, SessionEntryKind.Group,
                binding.ConversationNamespace, peer, message, false)], TestContext.Current.CancellationToken);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                kind = "interaction",
                scenario,
                index,
                revision,
                input = inputs[index],
                milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                result,
                memoryCoverage = "controlled_empty",
                transportAvailable = false,
                graphitiCalls = 0
            }));
            Assert.True(Assert.Single(result.Outcomes).Success, JsonSerializer.Serialize(result.Trace));
            Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
            Assert.DoesNotContain(result.Trace, t => t.EventName == "speech-committed" || t.MemoryMetadata?.GetValueOrDefault("result") == "sent");
            Assert.Equal(index + 1, run.History.Count);
            foreach (var preview in result.ParticipationReviews.Select(r => r.Preview).OfType<SessionResponsePreviewResult>())
                Assert.DoesNotContain(preview.Trace, t => t.EventName == "speech-committed" || t.MemoryMetadata?.GetValueOrDefault("result") == "sent");
        }
        if (replay is not null) Assert.Empty(replay);
        TestContext.Current.TestOutputHelper!.WriteLine($"Synthetic pipeline report: {path}");
    }

    private sealed class Recording(IFlowCompletion inner, ContextInterpretationSyntheticLiveTests.Usage usage, StreamWriter writer,
        Queue<JsonElement>? replay = null) : IFlowCompletion
    {
        private int calls;
        private int diagnosticCalls;
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (Interlocked.Increment(ref calls) > 18) throw new InvalidOperationException("Synthetic experiment call budget exceeded.");
            if (request.Inputs.TryGetProperty("reavaliacao", out var review))
                Assert.Equal(JsonValueKind.Object, review.GetProperty("interpretacao_contextual").ValueKind);
            if (replay is not null)
            {
                var recorded = replay.Dequeue();
                Assert.Equal(request.NodeId, recorded.GetProperty("NodeId").GetString());
                Assert.Equal(request.Inputs.TryGetProperty("reavaliacao", out _), recorded.GetProperty("reassessment").GetBoolean());
                if (request.NodeId == "group-generation" && request.Inputs.TryGetProperty("reavaliacao", out _))
                {
                    // Diagnostic only: same runtime-built request, one metadata difference, two calls.
                    // Neither diagnostic output enters the flow or supplies sending authority.
                    foreach (var omitFlag in new[] { false, true })
                    {
                        if (++diagnosticCalls > 2) throw new InvalidOperationException("Metadata comparison budget exceeded.");
                        var inputs = JsonNode.Parse(request.Inputs.GetRawText())!;
                        if (omitFlag) inputs["reavaliacao"]!.AsObject().Remove("autoriza_envio");
                        else inputs["reavaliacao"]!["autoriza_envio"] = false;
                        usage.Value = null;
                        var watch = Stopwatch.GetTimestamp();
                        var diagnostic = await inner.CompleteAsync(request with { Inputs = JsonSerializer.SerializeToElement(inputs) }, token);
                        await writer.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            kind = "generation_metadata_comparison",
                            omitFlag,
                            request.NodeId,
                            request.Model,
                            request.Provider,
                            inputs,
                            output = diagnostic,
                            milliseconds = Stopwatch.GetElapsedTime(watch).TotalMilliseconds,
                            usage = usage.Value
                        }));
                    }
                }
                return recorded.GetProperty("output").GetString()!;
            }
            usage.Value = null;
            var start = Stopwatch.GetTimestamp();
            var output = await inner.CompleteAsync(request, token);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                kind = "completion",
                request.NodeId,
                request.Model,
                request.Provider,
                reassessment = request.Inputs.TryGetProperty("reavaliacao", out _),
                interpretationPhase = request.Inputs.TryGetProperty("phase", out var phase) ? phase.GetString() : null,
                output,
                milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                usage = request.Provider == "openai" ? usage.Value : null
            }));
            return output;
        }
    }
}
