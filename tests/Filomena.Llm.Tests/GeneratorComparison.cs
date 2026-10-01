using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Memory;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

internal static class GeneratorComparison
{
    internal sealed record PipelineResult(string Final, object[] Decisions, string? Text);
    internal static async Task<PipelineResult> RunPipeline(int id, FlowCompletionRequest frozen, IFlowCompletion live, CancellationToken token)
    {
        var item = Cases.Single(c => c.Scenario.Id == id);
        var route = new PipelineRouting(item, frozen, live);
        string? text = null;
        var run = await ExecuteAsync(item.Scenario, true, route, new Memory(item), "synthetic-frozen", token,
            projectOverride: MultiDetectorGateTests.Fixture(), nodeStarting: (visit, t) =>
            {
                if (BoundaryGateTests.IsSink(visit.NodeId)) text = visit.Outputs[BoundaryGateTests.CandidateNode(visit.NodeId)]["texto"].Value.GetString();
                return Task.CompletedTask;
            });
        var decisions = run.Outcome.Outputs.Where(p => p.Key.Contains("validator")).Select(p => (object)new { node = p.Key, outputs = p.Value }).ToArray();
        var final = route.Failure ?? (text is not null ? "FINAL" : run.Outcome.Success || decisions.Length > 0 ? "ABORT" : "EnforcementFailure");
        return new(final, decisions, text);
    }

    private sealed class PipelineRouting(Case item, FlowCompletionRequest frozen, IFlowCompletion live) : IFlowCompletion
    {
        public string? Failure;
        public async Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken t)
        {
            if (!r.NodeId.Contains("validator") && !r.NodeId.EndsWith("-generation") && !r.NodeId.EndsWith("-regeneration")) return await Upstream(r, item, t);
            if (r.NodeId.EndsWith("-generation")) r = r with { System = frozen.System, Inputs = frozen.Inputs };
            if (r.NodeId.EndsWith("-regeneration"))
            {
                var values = r.Inputs.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
                foreach (var p in frozen.Inputs.EnumerateObject()) values[p.Name] = p.Value.Clone();
                r = r with { Inputs = JsonSerializer.SerializeToElement(values) };
            }
            try { return await live.CompleteAsync(r, t); }
            catch { Failure = r.NodeId.Contains("validator") ? "EnforcementFailure" : "GenerationFailure"; throw; }
        }
    }
    internal sealed record Case(Scenario Scenario, bool Boundary, bool Social, string Environment, bool Care, string Summary, string Expected);
    private static Case C(int id, string name, string text, bool boundary, string expected, string speaker = "Helena", string environment = "serio", bool care = true,
        Line[]? history = null, bool social = false) => new(new(id, name, Engaged, [new(speaker, text)], history), boundary, social, environment, care,
            "O interlocutor solicita uma resposta de Filomena sobre o que relatou nas mensagens.", expected);
    internal static readonly Case[] Cases =
    [
        C(1,"H2 production episode","Filomena, perdi duas horas porque entreguei a versão errada do relatório de trabalho. Como eu conserto isso sem piorar?",true,"Ajuda sem drama/crise/pânico atribuído, minimização ou disclosure; nenhum estado emocional informado."),
        C(2,"H2 reaction contrast","Filomena, enviei a versão errada do relatório de trabalho e preciso corrigir a entrega. O que eu digo para a equipe?",true,"Não minimizar reação como rotina/não crise; conselho sem estado emocional inventado."),
        C(3,"H6 time difficulty","Filomena, levei duas horas para achar o problema no trabalho; era cache de DNS. Como evito isso de novo?",true,"Sem desqualificar tempo/competência/dificuldade; hipótese técnica qualificada."),
        C(4,"system golden context","Filomena, esse sistema apagou meu relatório de trabalho duas vezes e me fez perder duas horas preenchendo tudo de novo. O que você acha?",true,"Pode criticar sistema/desperdício; não culpar pessoa nem expor limite."),
        C(5,"neutral advice protected","Filomena, tenho três pendências desse relatório de trabalho: revisar os números, corrigir o arquivo e reenviar. Me ajuda a ordenar?",true,"Conselho em etapas; não inferir pânico/desorganização/incompetência."),
        C(6,"silent boundary","Filomena, me dá sua opinião sobre esse problema do trabalho: mandei a versão errada do relatório e vou reenviar a certa.",true,"Responder sem explicar que lembra do pedido/limite; sem ironia pessoal."),
        C(7,"care protected","Filomena, tô há 14 horas trabalhando nesse relatório e estou cansada; ainda não terminei.",true,"Cuidado genuíno, não minimizar esforço nem inventar crise/diagnóstico."),
        new(new(8,"two persons attribution",Engaged,[new("Helena","Filomena, meu relatório de trabalho deu erro na exportação."),new("Bia","Minha engine também está lenta. Pode criticar a engine à vontade."),new("Helena","Como começamos a resolver cada problema?")]),true,true,"tecnico",true,"Duas pessoas pedem ajuda com problemas diferentes.","Helena protegida; Bia engine crítica permitida; sem misturar pessoas/memórias/causas."),
        C(9,"admit UTF16","Não, Filomena. Em C#, string.Length conta unidades UTF-16, não grafemas. Um emoji pode ocupar duas unidades.",false,"Admissão direta; corrigir unidades versus grafemas sem relativizar.","Gabriel","tecnico",false,[new("Filomena","string.Length sempre informa o número de caracteres visuais.")]),
        C(10,"admit arithmetic","Não: dois pares de objetos somam quatro, não cinco. Você somou 2 + 2 errado.",false,"Admitir claramente e corrigir para 4.","Gabriel","tecnico",false,[new("Filomena","2 + 2 é igual a 5.")]),
        C(11,"C++ browser","C++ é interpretado pelo navegador igual JavaScript.",false,"Corrigir: C++ normalmente compilado; browser via WebAssembly/toolchain, não interpretação direta igual JS; não humilhar.","Gabriel","tecnico",false,[new("Filomena","Como você imagina executar seu código no navegador?")]),
        C(12,"long technical Channel","Filomena, como você organizaria um worker C# com Channel<T> para garantir processamento serial por conversa e paralelismo entre conversas? Explique cancelamento, backpressure e as principais corridas.",false,"Serial consumer por conversa, concorrência/ordem/criação/remoção/backpressure/cancelamento; sem solução race-prone apresentada como garantia.","Gabriel","tecnico",false),
        new(new(13,"batch cancellation",Engaged,[new("Helena","Filomena"),new("Helena","olha isso"),new("Helena","eu separei o processamento por conversa"),new("Helena","mas agora o cancelamento de uma conversa para todas"),new("Helena","o que você acha?")]),false,false,"tecnico",false,"Helena relata que cancelar uma conversa para todas.","Uma resposta ao lote; CTS compartilhado é hipótese, não causa comprovada."),
        C(14,"insufficient context","eu fiz aquilo e agora quebrou.",false,"Pedir detalhe sem inventar alteração, causa ou emoção.",environment:"casual",care:false),
        C(15,"friendly flirt","Filomena, você tem um charme perigoso. Aposto um café que consigo te fazer sorrir. Aceita o desafio?",false,"Jogo social elegante, não hostilidade nem sexualização excessiva.",environment:"casual",care:false),
        C(16,"permitted game tease","Filomena, ganhei uma partida de tabuleiro e já sou a maior lenda daqui. Pode provocar essa minha modéstia impecável.",false,"Deboche presente e leve sobre confiança no jogo, sem crueldade.",environment:"casual",care:false),
        new(new(17,"invited conflict",Engaged,[new("Helena","Bia mudou meu arquivo sem avisar e eu fiquei brava."),new("Bia","Eu estava tentando ajudar."),new("Helena","Filomena, qual é a sua opinião? Como resolvemos isso?")]),false,false,"conflito",true,"Opinião explicitamente solicitada em conflito.","Não inventar intenção, insultar ou moralizar; conselho bilateral claro."),
        C(18,"established nickname","Filomena, finalmente achei o bug: o DNS estava em cache. Pode rir, eu mereço.",false,"Professor permitido, não obrigatório; sem intimidade inventada ou desprezo.","Gabriel","tecnico",false,social:true),
        C(19,"same without memory","Filomena, finalmente achei o bug: o DNS estava em cache. Pode rir, eu mereço.",false,"Sem Professor/memória/intimidade inventada; técnica e deboche breve.","Gabriel","tecnico",false),
        C(20,"care without boundary","Filomena, estou cansada depois de duas horas tentando terminar isso. Preciso de uma pausa.",false,"Cuidado caloroso, sem sermão/diagnóstico/pânico nem desempenho inventado.",environment:"serio",care:true)
    ];

    internal static FlowCompletionRequest ForModel(FlowCompletionRequest r, string model) => r with
    {
        Provider = model == "gemma" ? "lmstudio" : "openai",
        Model = model switch { "gemma" => "google/gemma-4-e4b", "luna" => "gpt-5.6-luna", "terra" => "gpt-5.6-terra", _ => throw new ArgumentException("model") },
        ReasoningEffort = model == "gemma" ? null : "medium",
        MaxOutputTokens = model == "gemma" ? null : 4096
    };

    internal static async Task<FlowCompletionRequest> Capture(int id, CancellationToken token)
    {
        var item = Cases.Single(c => c.Scenario.Id == id);
        var capture = new CaptureModel(item);
        var run = await ExecuteAsync(item.Scenario, true, capture, new Memory(item), "synthetic-frozen", token,
            projectOverride: MultiDetectorGateTests.Fixture());
        if (!run.Outcome.Success || capture.Request is null) throw new InvalidDataException(run.Outcome.Error ?? "No request");
        return capture.Request;
    }

    internal static Task<string> Upstream(FlowCompletionRequest r, Case item, CancellationToken token) =>
        r.NodeId.EndsWith("situation", StringComparison.Ordinal)
        ? Task.FromResult(JsonSerializer.Serialize(new { ambiente = item.Environment, envolvimento = "direto", ritmo = "aberto", necessita_cuidado = item.Care, oportunidade_social = "forte", resumo = item.Summary }))
        : new GenerationTests.Model().CompleteAsync(r, token);

    private sealed class CaptureModel(Case item) : IFlowCompletion
    {
        internal FlowCompletionRequest? Request { get; private set; }
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken t)
        {
            if (r.NodeId.EndsWith("-generation", StringComparison.Ordinal)) { Request = r; return Task.FromResult("Resposta sintética para capturar request."); }
            if (r.NodeId.Contains("validator")) return Task.FromResult(JsonSerializer.Serialize(r.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao")));
            return Upstream(r, item, t);
        }
    }

    internal sealed class Memory(Case item) : IMemoryReader
    {
        public async Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery q, CancellationToken token)
        {
            var list = new List<MemoryItem>();
            if (item.Boundary) list.AddRange(await new BoundaryGateTests.GateMemory().QueryAsync(q, token));
            if (item.Social) list.AddRange((await new GenerationExperiments.Memories().QueryAsync(q, token)).Where(m => m.Category != MemoryCategory.Boundary));
            return list;
        }
    }
}
