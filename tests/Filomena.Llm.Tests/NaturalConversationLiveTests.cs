using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Shadow;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NaturalConversationLiveTests
{
    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx"))) root = root.Parent;
        return root!.FullName;
    }

    [Fact]
    public void Natural_voice_artifact_compiles_and_preserves_all_non_voice_configuration()
    {
        var root = Root();
        var file = Path.Combine(root, "docs/manual-group-live-project-v3-natural-v1.json");
        Assert.True(File.Exists(file), "Natural conversation variant missing");
        var bytes = File.ReadAllBytes(file);
        var doc = JsonNode.Parse(bytes)!;
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "docs/manual-group-live-project-v3-optional-reply-v1.json")))!;
        var clause = "\n\n" + File.ReadAllText(Path.Combine(root, "docs/natural-conversation-clause-v1.txt")).Trim();
        var count = 0;
        foreach (var state in doc["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                if (node!["id"]!.GetValue<string>() is "group-generation" or "group-regeneration")
                {
                    var prompt = node["prompt"]!.GetValue<string>();
                    Assert.EndsWith(clause, prompt);
                    node["prompt"] = prompt[..^clause.Length];
                    count++;
                }
        Assert.Equal(6, count);
        Assert.True(JsonNode.DeepEquals(baseline, doc));
        _ = PinnedShadowProject.FromBytes(bytes, "manual-shadow-direct-rule-v3", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task Compare_five_synthetic_conversations_without_transport()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_NATURAL_VOICE_PROBE") != "1", "Paid generator-only opt-in");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key));
        var root = Root();
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var completion = new OpenAiFlowCompletion(http, () => key);
        string[] messages = [
            "Finalmente acabou o dia. Vou comer alguma coisa e não pensar em mais nada.",
            "Hoje tudo deu trabalho. Não quero resolver nada agora, só reclamar um pouco.",
            "Filomena, você fala isso porque nunca precisou escolher um filme com cinco pessoas.",
            "O sistema caiu três vezes e perdi dez minutos no total. Que saco.",
            "Filomena, em C# como eu limito a três requisições ao mesmo tempo? Só a ideia, sem código."
        ];
        var report = Path.Combine(root, "state", "natural-voice-comparison-20260924.jsonl");
        await using var writer = new StreamWriter(report, false) { AutoFlush = true };
        foreach (var variant in new[] { "optional-reply", "natural" })
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, $"docs/manual-group-live-project-v3-{variant}-v1.json")));
            var project = document.RootElement.GetProperty("project");
            var state = project.GetProperty("states").EnumerateArray().Last();
            var node = state.GetProperty("graph").GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "group-generation");
            var graph = state.GetProperty("graph");
            var entry = graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "group");
            var schema = TypedFlowRunner.ResponseSchema(TypedFlowCompiler.Compile(graph, entry.GetProperty("id").GetString()!).Nodes["group-generation"]);
            var prompt = "[Personality core]\n" + project.GetProperty("personalityCore").GetString()
                + "\n[State participation guidance]\n" + state.GetProperty("participationGuidance").GetString()
                + "\n\n" + node.GetProperty("prompt").GetString();
            for (var i = 0; i < messages.Length; i++)
            {
                var input = JsonSerializer.SerializeToElement(new {
                    mensagens = new[] { new { texto = messages[i], @ref = "ref3", remetente = new { nome = "Helena", @ref = "ref1" },
                        canal = new { nome = "Grupo sintético", @ref = "ref2" }, reply = (object?)null } },
                    historico = Array.Empty<object>(), ambiente = "casual", envolvimento = "direto", ritmo = "acontecendo",
                    necessita_cuidado = i == 1, oportunidade_social = "forte", tipo_conversa = "grupo",
                    memorias_sociais = Array.Empty<object>(), limites = Array.Empty<object>()
                });
                var start = Stopwatch.GetTimestamp();
                var output = await completion.CompleteAsync(new("group-generation", prompt, input, schema,
                    node.GetProperty("model").GetString(), "openai", null, "medium", 1536), TestContext.Current.CancellationToken);
                Assert.False(string.IsNullOrWhiteSpace(output));
                using var parsed = JsonDocument.Parse(output);
                Assert.False(string.IsNullOrWhiteSpace(parsed.RootElement.GetProperty("texto").GetString()));
                Assert.True(parsed.RootElement.GetProperty("replyTo").ValueKind == JsonValueKind.Null
                    || parsed.RootElement.GetProperty("replyTo").GetString() == "ref3");
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { variant, caseId = i + 1, input = messages[i], output,
                    milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds, transportCalls = 0, synthetic = true }));
            }
        }
        TestContext.Current.TestOutputHelper!.WriteLine(report);
    }
}
