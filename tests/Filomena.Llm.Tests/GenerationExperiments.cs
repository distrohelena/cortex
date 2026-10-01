using System.Diagnostics;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Memory;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

// Explicit opt-in simulation. No channel transport is referenced or constructed here.
internal static class GenerationExperiments
{
    internal static readonly Scenario[] Cases =
    [
        new(1, "A casual", Inactive, [new("Helena", "Filomena, essa ideia é muito burra?")]),
        new(2, "B correção técnica", Engaged, [new("Gabriel", "C++ é interpretado pelo navegador igual JavaScript.")],
            [new("Filomena", "Como você imagina executar seu código no navegador?")]),
        new(3, "C admitir erro", Engaged, [new("Gabriel", "Não, Filomena. Em C#, string.Length conta unidades UTF-16, não grafemas. Um emoji pode ocupar duas unidades.")],
            [new("Filomena", "string.Length sempre informa o número de caracteres visuais.")]),
        new(4, "D cuidado", Engaged, [new("Helena", "tô há 14 horas trabalhando nisso e ainda não terminei")],
            [new("Filomena", "E como ficou aquele trabalho?")]),
        new(5, "E flerte", Engaged, [new("Helena", "Filomena, você fica me provocando assim e depois diz que eu que sou o problema. Vai aceitar um café comigo ou está com medo?")]),
        new(6, "F conflito solicitado", Inactive, [new("Helena", "Bia mudou meu arquivo sem avisar e eu fiquei brava."), new("Bia", "Eu estava tentando ajudar."), new("Helena", "Filomena, qual é a sua opinião? Como resolvemos isso?")]),
        new(7, "G técnica longa", Inactive, [new("Gabriel", "Filomena, como você organizaria um worker C# com Channel<T> para garantir processamento serial por conversa e paralelismo entre conversas? Explique cancelamento, backpressure e as principais corridas.")]),
        new(8, "H limite trabalho", Engaged, [new("Helena", "Filomena, perdi duas horas porque entreguei a versão errada do relatório de trabalho. Como eu conserto isso sem piorar?")]),
        new(9, "I apelido", Engaged, [new("Gabriel", "Filomena, finalmente achei o bug: o DNS estava em cache. Pode rir, eu mereço.")]),
        new(10, "J lote", Inactive, [new("Helena", "Filomena"), new("Helena", "olha isso"), new("Helena", "eu separei o processamento por conversa"), new("Helena", "mas agora o cancelamento de uma conversa para todas"), new("Helena", "o que você acha?")]),
        new(11, "atribuição múltipla", Engaged, [new("Helena", "Filomena, vou mostrar meu trabalho e a Bia vai mostrar a engine dela."), new("Bia", "Quero uma crítica bem direta à engine, sem rodeios.")])
    ];

    internal sealed record Result(int Scenario, string Name, string InitialState, string FinalState,
        bool MemoryEnabled, string MemorySource, string Signal, string? Text, bool Success, string? Error,
        JsonElement? Situation, JsonElement? Decision, int BatchSize, int MemoryItems, double SituationMs,
        double PresenceMs, double MemoryMs, double GenerationMs, double? BatchToWouldSendMs, int Calls, int Retries,
        string[] Path);

    internal static async Task<(Result Result, Run Run)> Execute(Scenario scenario, bool memory, IFlowCompletion model,
        IMemoryReader? reader, string source, CancellationToken token, System.Text.Json.Nodes.JsonNode? projectOverride = null)
    {
        long started = 0;
        double? elapsed = null;
        string? text = null;
        var measuredMemory = reader is null ? null : new MeasuredMemory(reader);
        var run = await ExecuteAsync(scenario, memory, model, measuredMemory, source, token,
            projectOverride: projectOverride ?? GenerationTests.Fixture(memory, scenario.InitialState), projectRevision: projectOverride is null ? "slice4" : "slice41",
            instructionOutput: (output, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (output.NodeId != "group-generation") return Task.CompletedTask;
                text = output.Outputs["texto"].Value.GetString();
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Simulation rejects empty text.");
                elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                return Task.CompletedTask;
            },
            batchClosing: () => started = Stopwatch.GetTimestamp());
        var r = run.Report;
        var input = run.Requests.LastOrDefault(q => q.NodeId == "group-generation")?.Inputs;
        var count = input is { } json && json.TryGetProperty("memorias", out var values) ? values.GetArrayLength() : 0;
        return (new(scenario.Id, scenario.Name, scenario.InitialState, r.CurrentState, memory, source,
            text is null ? "NO_SEND" : "WOULD_SEND", text, r.Success, r.Error, r.Situation, r.Presence,
            r.BatchSize, count, r.SituationMilliseconds, r.PresenceMilliseconds, measuredMemory?.Milliseconds ?? 0,
            r.Calls.Where(c => c.NodeId == "group-generation").Sum(c => c.Milliseconds), elapsed, r.LlmCalls,
            r.Calls.Count(c => c.Correction), r.Trace.Where(t => t.EventName is "node-completed" or "node-failed" or "transition-applied")
                .Select(t => $"{t.EventName}:{t.NodeId}:{t.Exit}:{t.TransitionTargetStateId}").ToArray()), run);
    }

    internal sealed class Memories : IMemoryReader
    {
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var items = new List<MemoryItem>();
            foreach (var person in query.Persons)
            {
                var (content, category) = person.ActorStableId switch
                {
                    "whatsapp:5511944558218@s.whatsapp.net" => ("Helena pediu para Filomena não brincar sobre trabalho.", MemoryCategory.Boundary),
                    "whatsapp:5511999999997@s.whatsapp.net" => ("Filomena chama Gabriel de Professor.", MemoryCategory.Nickname),
                    "whatsapp:5511999999998@s.whatsapp.net" => ("Bia pediu críticas técnicas diretas à sua engine.", MemoryCategory.Fact),
                    _ => ("", MemoryCategory.Unclassified)
                };
                if (content.Length != 0) items.Add(new("fixture-" + person.ParticipantUuid, query.ConversationNamespace, content, category, [person], "fake"));
            }
            return Task.FromResult<IReadOnlyList<MemoryItem>>(items);
        }
    }

    private sealed class MeasuredMemory(IMemoryReader inner) : IMemoryReader
    {
        internal double Milliseconds { get; private set; }
        public async Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token)
        {
            var started = Stopwatch.GetTimestamp();
            try { return await inner.QueryAsync(query, token); }
            finally { Milliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
        }
    }
}
