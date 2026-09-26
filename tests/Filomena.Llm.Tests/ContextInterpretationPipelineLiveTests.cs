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
        var directory = Environment.GetEnvironmentVariable("FILOMENA_IRONY_SYNTHETIC_OUTPUT") ?? throw new InvalidOperationException("Output required.");
        Assert.True(Path.IsPathFullyQualified(directory) && Directory.Exists(directory));
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key));
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root,
            "docs/manual-group-live-project-v3-natural-v1.json")))!;
        foreach (var state in project["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray().Where(n => n!["kind"]!.ToString() == "accumulate"))
                node!["settings"]!["quietSeconds"] = 0.001;
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(project)));
        var pin = new PinnedShadowProject(new("synthetic-irony-natural", 1, revision, JsonSerializer.SerializeToElement(project)));
        var runId = Guid.NewGuid().ToString("N");
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
        var completion = new Recording(provider, usage, writer);
        var memory = new SessionParticipationReviewTests.Memory { AllowEmpty = true };
        var journal = Directory.CreateDirectory(Path.Combine(directory, "journal"));
        await using var run = new ObservedShadowRun(pin, binding, journal.FullName, roles, completion, memory, "synthetic-epoch", 0);
        var config = ContextInterpretationHostTests.ReadOperationalConfig(true);
        ContextInterpretationHost.Configure(config with { Participation = config.Participation with { ProjectRevision = revision } }, pin, binding, run, completion);
        string[] inputs =
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
        }
        TestContext.Current.TestOutputHelper!.WriteLine($"Synthetic pipeline report: {path}");
    }

    private sealed class Recording(IFlowCompletion inner, ContextInterpretationSyntheticLiveTests.Usage usage, StreamWriter writer) : IFlowCompletion
    {
        private int calls;
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (Interlocked.Increment(ref calls) > 18) throw new InvalidOperationException("Synthetic experiment call budget exceeded.");
            usage.Value = null;
            var start = Stopwatch.GetTimestamp();
            var output = await inner.CompleteAsync(request, token);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                kind = "completion",
                request.NodeId,
                request.Model,
                request.Provider,
                output,
                milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                usage = request.Provider == "openai" ? usage.Value : null
            }));
            return output;
        }
    }
}
