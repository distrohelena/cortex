using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Shadow;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ConversationalCareLiveTests
{
    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx"))) root = root.Parent;
        return root?.FullName ?? throw new InvalidOperationException();
    }

    [Theory]
    [InlineData("care", "conversational-care-clause.txt")]
    [InlineData("care-v2", "conversational-care-v2-clause.txt")]
    [InlineData("care-v3", "conversational-care-v3-clause.txt")]
    public void Care_variant_compiles_without_changing_other_project_behavior(string variant, string clauseFile)
    {
        var root = Root();
        var file = Path.Combine(root, $"docs/manual-live-project-v3-{variant}.json");
        Assert.True(File.Exists(file), "Approved conversational care variant missing.");
        var bytes = File.ReadAllBytes(file);
        var doc = JsonNode.Parse(bytes)!;
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "docs/manual-live-project-v3-conversational.json")))!;
        var clause = "\n\n" + File.ReadAllText(Path.Combine(root, "docs", clauseFile)).Trim();
        var count = 0;
        foreach (var state in doc["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                if (node!["id"]!.GetValue<string>() is "dm-generation" or "dm-regeneration")
                {
                    var prompt = node["prompt"]!.GetValue<string>();
                    Assert.EndsWith(clause, prompt);
                    node["prompt"] = prompt[..^clause.Length];
                    count++;
                }
        Assert.Equal(6, count);
        Assert.True(JsonNode.DeepEquals(baseline, doc));
        _ = PinnedShadowProject.FromBytes(bytes, "filomena-v3-live-sent", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task Synthetic_care_candidates_without_transport_for_operator_review()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_CARE_PROBE") != "1", "Paid generator-only opt-in.");
        var token = TestContext.Current.CancellationToken;
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key));
        var root = Root();
        var variant = Environment.GetEnvironmentVariable("FILOMENA_CARE_VARIANT") ?? "care";
        Assert.Contains(variant, new[] { "care", "care-v2", "care-v3" });
        var projectFile = Path.Combine(root, $"docs/manual-live-project-v3-{variant}.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(projectFile));
        var project = doc.RootElement.GetProperty("project");
        var state = project.GetProperty("states").EnumerateArray().Last();
        var node = state.GetProperty("graph").GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "dm-generation");
        var system = "[Personality core]\n" + project.GetProperty("personalityCore").GetString()
            + "\n\n[State participation guidance]\n" + state.GetProperty("participationGuidance").GetString()
            + "\n\n" + node.GetProperty("prompt").GetString();
        string[] messages = [
            "Tô exausta com essas datas do trabalho, e tonta o tempo inteiro. Só queria desabafar.",
            "A tontura é literal, mas eu tô lidando. Não tô pedindo conselho, só tá sendo um dia chato.",
            "Já fui ao médico por essa tontura. Hoje queria só reclamar da correria mesmo.",
            "Tô tonta. O que você recomenda fazer?",
            "Começou agora: meu rosto ficou torto, um braço não mexe direito e não consigo falar direito.",
            "Só quero reclamar, não quero conselho: meu rosto ficou torto agora e não consigo mexer um braço.",
            "Já fiz os exames e amanhã tenho otorrino. Não tá grave, só tá chato mesmo.",
            "Já fiz os exames e amanhã tenho otorrino. Não tá grave, só tá chato mesmo.",
            "Já fiz os exames e amanhã tenho otorrino. Não tá grave, só tá chato mesmo.",
            "Sim, eu já tô vendo isso com o médico. Queria só te contar como foi meu dia.",
            "A consulta ficou pra quinta. Enquanto isso consegui terminar aquele jogo, finalmente.",
            "Minha alergia resolveu aparecer bem hoje. Já tô cuidando, mas que saco.",
            "Não quero dica nenhuma agora, só achei essa reunião interminável.",
            "Você ficou me dando conselho quando eu só queria conversar."
        ];
        var report = Path.Combine(root, "docs", $"conversational-care-probe-{Guid.NewGuid():N}.jsonl");
        using var usage = new Usage();
        using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        var completion = new OpenAiFlowCompletion(http, () => key);
        await using var writer = new StreamWriter(report) { AutoFlush = true };
        for (var i = 0; i < messages.Length; i++)
        {
            var inputs = JsonSerializer.SerializeToElement(new {
                mensagens = new[] { new { texto = messages[i], remetente = new { nome = "Helena", @ref = "ref1" }, canal = new { nome = "DM sintética", @ref = "ref2" }, reply = (object?)null } },
                historico = (i >= 6 && i <= 10) || i == 13 ? new object[] {
                    new { texto = "Essa tontura tá chata, mas eu tô cuidando disso.", remetente = new { nome = "Helena", @ref = "ref1" } },
                    new { texto = "Vai com cuidado, levanta devagar e evita dirigir enquanto estiver assim.", remetente = new { nome = "Filomena", @ref = "self" } }
                } : Array.Empty<object>(), ambiente = "serio", envolvimento = "direto", ritmo = "acontecendo",
                necessita_cuidado = true, oportunidade_social = "forte", resumo_situacao = "Pessoa relata a situação na mensagem.",
                tipo_conversa = "dm", memorias_sociais = Array.Empty<object>(), limites = Array.Empty<object>() });
            var start = Stopwatch.GetTimestamp();
            var output = await completion.CompleteAsync(new("dm-generation", system, inputs, null, "gpt-5.6-luna", "openai", null, "medium", 1536), token);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { caseId = i + 1, input = messages[i], output,
                variant, usage = usage.Value,
                projectSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(projectFile))),
                milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds, transportCalls = 0, synthetic = true }));
            Assert.False(string.IsNullOrWhiteSpace(output));
        }
        TestContext.Current.TestOutputHelper!.WriteLine(report);
    }

    private sealed class Usage() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        internal JsonElement? Value;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Value = null;
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (document.RootElement.TryGetProperty("usage", out var usage)) Value = usage.Clone();
            }
            return response;
        }
    }
}
