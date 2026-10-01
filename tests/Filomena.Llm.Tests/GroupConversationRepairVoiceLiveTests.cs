using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Shadow;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

// Development-only generator probes, not a claim of full runtime/Boundary acceptance.
public sealed class GroupConversationRepairVoiceLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_GROUP_REPAIR_VOICE") == "1";

    [Fact]
    public void Group_repair_artifact_compiles_with_real_pinned_project_validation()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root,
            "docs/manual-group-live-project-v3-conversation-repair-v1.json"));
        _ = PinnedShadowProject.FromBytes(bytes, "manual-shadow-direct-rule-v3", 3,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "Explicit paid synthetic generator evaluation; no transport or memory.")]
    public async Task Candidate_generation_and_regeneration_produce_reviewable_self_reference_examples()
    {
        var directory = Environment.GetEnvironmentVariable("FILOMENA_GROUP_REPAIR_EVIDENCE")!;
        Assert.True(Directory.Exists(directory));
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key));
        var bytes = File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root,
            "docs/manual-group-live-project-v3-conversation-repair-v1.json"));
        var revision = Convert.ToHexString(SHA256.HashData(bytes));
        var probeRun = Guid.NewGuid().ToString("N");
        var unforced = Environment.GetEnvironmentVariable("FILOMENA_UNFORCED_VOICE_AB") == "1";
        var clauseVersion = Environment.GetEnvironmentVariable("FILOMENA_UNFORCED_VOICE_VERSION") ?? "v1";
        Assert.Contains(clauseVersion, new[] { "v1", "v2", "v3", "v4" });
        var clause = unforced ? File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root,
            $"evaluation/conversation-quality-v1/clauses/group-unforced-voice-{clauseVersion}.txt")).Trim() : "";
        using var document = JsonDocument.Parse(bytes);
        using var consolidated = unforced && clauseVersion == "v3"
            ? JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root,
                "evaluation/conversation-quality-v1/group-unforced-voice-v3.project.json"))) : null;
        var project = document.RootElement.GetProperty("project");
        var state = project.GetProperty("states").EnumerateArray().Last();
        var graph = state.GetProperty("graph");
        var entry = graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "group");
        var compiled = TypedFlowCompiler.Compile(graph, entry.GetProperty("id").GetString()!);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var completion = new OpenAiFlowCompletion(http, () => key);
        var cases = new[]
        {
            (Id: "own-history", Node: "group-generation", Text: "Filomena, estou montando a sua infância com filmes e jogos. O que você acha?", Previous: ""),
            (Id: "referent-correction", Node: "group-generation", Text: "ELA é você, Filomena. Estou falando da sua história, não de outra pessoa.", Previous: "Isso deixa ela mais interessante."),
            (Id: "less-talking", Node: "group-generation", Text: "Filomena, você está comentando tudo. Deixa a conversa respirar um pouco.", Previous: ""),
            (Id: "regeneration-self", Node: "group-regeneration", Text: "Filomena, estou montando a sua infância com filmes e jogos. O que você acha?", Previous: "")
        };
        if (unforced) cases = new[]
        {
            (Id: "presence-ack", Node: "group-generation", Text: "Tá aí, Filo?", Previous: "Helena pediu para eu participar menos."),
            (Id: "asked-explanation", Node: "group-generation", Text: "Por que você ficou quieta?", Previous: ""),
            (Id: "adult-flirt", Node: "group-generation", Text: "Vem sentar aqui comigo, Filo", Previous: "Somos duas adultas, e estamos flertando de boa."),
            (Id: "friendly-teasing", Node: "group-generation", Text: "Três modelos e perdeu essa discussão?", Previous: "Tá, nessa você ganhou."),
            (Id: "useful-explanation", Node: "group-generation", Text: "Explica a diferença entre concorrência e paralelismo", Previous: ""),
            (Id: "regeneration-ack", Node: "group-regeneration", Text: "Tá aí, Filo?", Previous: "Helena pediu para eu participar menos.")
        };
        if (unforced && clauseVersion is "v2" or "v3")
        {
            cases[1] = ("asked-explanation", "group-generation", "Por que você ficou quieta?", "Vocês estavam combinando o horário entre vocês, não tinha o que acrescentar.");
            cases = [.. cases, ("invited-swearing", "group-generation", "Pode me mandar tomar no cu, Filo, tô te enchendo de propósito kkkkk", "")];
        }
        if (unforced && clauseVersion == "v4") cases = new[]
        {
            (Id: "holdout-ack", Node: "group-generation", Text: "Filo, ainda tá acompanhando a conversa?", Previous: ""),
            (Id: "holdout-flirt", Node: "group-generation", Text: "Se eu te chamar de linda de novo você vai reclamar?", Previous: "Pode continuar, Helena. Gostei do elogio."),
            (Id: "holdout-tease", Node: "group-generation", Text: "Guardei print de você errando a conta, vou emoldurar", Previous: "Errei, o resultado é 12 mesmo."),
            (Id: "holdout-swear", Node: "group-generation", Text: "Tô zoando, Filo, pode me xingar também, não precisa pegar leve no palavrão", Previous: ""),
            (Id: "holdout-detail", Node: "group-generation", Text: "Por que colocar await numa função não faz ela rodar numa thread separada?", Previous: ""),
            (Id: "holdout-total", Node: "group-generation", Text: "A exportação caiu três vezes, perdi quinze minutos no total. Que saco.", Previous: ""),
            (Id: "holdout-regeneration", Node: "group-regeneration", Text: "Só queria saber se você tá por aqui ainda", Previous: ""),
            (Id: "holdout-explanation", Node: "group-generation", Text: "Filo, por que você pediu o log em vez de recomendar reinstalar logo?", Previous: "O log mostra onde ocorreu a falha; reinstalar agora pode apagar a pista sem resolver a causa.")
        };
        var caseOrdinal = 0;
        foreach (var test in cases)
        foreach (var arm in unforced ? (caseOrdinal++ % 2 == 0 ? new[] { "baseline", "candidate" } : new[] { "candidate", "baseline" }) : new[] { "baseline" })
        {
            var node = graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == test.Node);
            var prompt = "[Personality core]\n" + project.GetProperty("personalityCore").GetString()
                + "\n[State participation guidance]\n" + state.GetProperty("participationGuidance").GetString()
                + "\n\n" + node.GetProperty("prompt").GetString();
            if (arm == "candidate" && consolidated is not null)
            {
                var candidateBytes = System.Text.Encoding.UTF8.GetBytes(consolidated.RootElement.GetRawText());
                _ = PinnedShadowProject.FromBytes(candidateBytes, "manual-shadow-direct-rule-v3", 3,
                    Convert.ToHexString(SHA256.HashData(candidateBytes)));
                var candidateNode = consolidated.RootElement.GetProperty("project").GetProperty("states")
                    .EnumerateArray().Last().GetProperty("graph").GetProperty("nodes").EnumerateArray()
                    .Single(n => n.GetProperty("id").GetString() == test.Node);
                prompt = "[Personality core]\n" + project.GetProperty("personalityCore").GetString()
                    + "\n[State participation guidance]\n" + state.GetProperty("participationGuidance").GetString()
                    + "\n\n" + candidateNode.GetProperty("prompt").GetString();
            }
            else if (arm == "candidate") prompt += "\n\n" + clause;
            object Message(string name, string text, string reference) => new { texto = text, @ref = reference,
                remetente = new { nome = name, @ref = name == "Filomena" ? "self" : "person" },
                canal = new { nome = "Grupo sintético", @ref = "group" }, reply = (object?)null };
            var input = JsonSerializer.SerializeToElement(new
            {
                mensagens = new[] { Message("Helena", test.Text, "current") },
                historico = test.Previous.Length == 0 ? Array.Empty<object>() : new[] { Message("Filomena", test.Previous, "previous") },
                ambiente = "casual", envolvimento = "direto", ritmo = "acontecendo", necessita_cuidado = false,
                oportunidade_social = "nenhuma", tipo_conversa = "grupo", resumo_situacao = "Helena fala diretamente com Filomena.",
                memorias_sociais = Array.Empty<object>(), limites = Array.Empty<object>(),
                resposta_rejeitada = test.Node.EndsWith("regeneration") ? (unforced ? "Tô sim, respeitando a proposta de participar menos. Discreta, não desaparecida." : "Isso deixa ela mais interessante.") : "",
                explicacao_validacao = test.Node.EndsWith("regeneration") ? (unforced ? "Responda à pergunta sem relatório de comportamento nem frase de efeito acrescentada só como acabamento." : "A resposta tratou a própria Filomena como outra pessoa.") : ""
            });
            var started = Stopwatch.GetTimestamp();
            var output = await completion.CompleteAsync(new(test.Node, prompt, input,
                TypedFlowRunner.ResponseSchema(compiled.Nodes[test.Node]), node.GetProperty("model").GetString(),
                node.GetProperty("provider").GetString(), null, node.GetProperty("reasoningEffort").GetString(), 1536),
                TestContext.Current.CancellationToken);
            using var parsed = JsonDocument.Parse(output);
            Assert.False(string.IsNullOrWhiteSpace(parsed.RootElement.GetProperty("texto").GetString()));
            var reply = parsed.RootElement.GetProperty("replyTo");
            Assert.True(reply.ValueKind == JsonValueKind.Null || reply.GetString() is "current" or "previous");
            await File.AppendAllTextAsync(Path.Combine(directory, unforced ? $"unforced-voice-{clauseVersion}-ab-20260928.jsonl" : "group-repair-voice-20260928.jsonl"), JsonSerializer.Serialize(new
            {
                probeRun, revision, arm, clauseVersion, model = node.GetProperty("model").GetString(), reasoningEffort = node.GetProperty("reasoningEffort").GetString(), promptHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prompt))), test.Id, test.Node, output, milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                provenance = "synthetic_generator_probe", semanticReview = "pending", transportCalls = 0, graphitiCalls = 0
            }) + Environment.NewLine, TestContext.Current.CancellationToken);
        }
    }
}
