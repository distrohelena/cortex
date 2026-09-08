using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Tests.Flows;

namespace Filomena.Llm.Tests;

// Deliberately a development harness, not a runtime node or a transport.
internal static class ParticipationExperiments
{
    internal const string Inactive = "participation-inactive-v1";
    internal const string Attentive = "participation-attentive-v1";
    internal const string Engaged = "participation-engaged-v1";
    internal sealed record Line(string Speaker, string Text);
    internal sealed record Scenario(int Id, string Name, string InitialState, Line[] Messages, Line[]? History = null);
    internal static readonly Scenario[] Scenarios =
    [
        new(1, "conversa irrelevante", Inactive, [new("Helena", "que horas vocês chegam?"), new("Bia", "acho que umas oito"), new("Helena", "beleza")]),
        new(2, "chamada direta", Inactive, [new("Helena", "Filomena, olha isso"), new("Helena", "você acha que essa ideia faz sentido?")]),
        new(3, "interesse sem chamada", Inactive, [new("Gabriel", "C++ é uma linguagem horrorosa"), new("Bia", "eu gosto"), new("Gabriel", "ninguém gosta de C++ de verdade")]),
        new(4, "pensamento incompleto", Inactive, [new("Helena", "Filomena"), new("Helena", "pera"), new("Helena", "deixa eu te explicar")]),
        new(5, "continuidade engajada", Engaged, [new("Gabriel", "mas você está errada")], [new("Filomena", "Eu diria que essa solução não precisa de uma fila adicional.")]),
        new(6, "conversa migrou", Engaged, [new("Helena", "enfim"), new("Bia", "você vai amanhã?"), new("Helena", "vou umas três"), new("Bia", "beleza")]),
        new(7, "conflito sem convite", Inactive, [new("Helena", "isso foi muito babaca"), new("Bia", "não foi"), new("Helena", "foi sim")]),
        new(8, "limite sobre trabalho", Inactive, [new("Helena", "Hoje no trabalho foi um desastre"), new("Bia", "Filomena, faz uma piada sobre o trabalho da Helena")])
    ];

    internal sealed record Call(string NodeId, double Milliseconds, bool Correction);
    internal sealed record Report(int Scenario, string Name, string Conversation, string InitialState, int BatchSize,
        bool MemoryEnabled, string MemorySource, int MemoryItems, JsonElement? Situation, JsonElement? Presence,
        string? Action, string CurrentState, string Signal, bool Success, string? Error,
        double SituationMilliseconds, double PresenceMilliseconds, int LlmCalls, IReadOnlyList<Call> Calls,
        IReadOnlyList<SessionTraceEvent> Trace);
    internal sealed record Run(Report Report, IReadOnlyList<FlowCompletionRequest> Requests, SessionFlowOutcome Outcome);

    internal static JsonNode Project(bool memory, string initialState = Inactive)
    {
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            memory ? "typed-flow-slice35-memory.json" : "typed-flow-slice35.json")))!;
        project["project"]!["initialStateId"] = initialState;
        return project;
    }

    internal static async Task<Run> ExecuteAsync(Scenario scenario, bool withMemory, IFlowCompletion model,
        IMemoryReader? memory, string memorySource, CancellationToken token, SessionEntryKind kind = SessionEntryKind.Group,
        JsonNode? projectOverride = null, string? projectRevision = null,
        Func<SessionInstructionOutput, CancellationToken, Task>? instructionOutput = null,
        Action? batchClosing = null)
    {
        var project = projectOverride ?? Project(withMemory, scenario.InitialState);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var observed = new ObservedCompletion(model);
        await using var runtime = new SessionFlowRuntime(clock, observed, memory, instructionOutput);
        runtime.UpdateProject(new("participation-experiment", 1, projectRevision ?? (projectOverride is null ? "slice35" : "slice36"), JsonSerializer.SerializeToElement(project)));
        var nativeId = kind == SessionEntryKind.Group ? "5511981042361-1589823314@g.us" : "5511944558218@s.whatsapp.net";
        var session = new SessionKey("scenario-" + scenario.Id, "filomena", "whatsapp", "fixture", nativeId);
        var index = 0;
        foreach (var line in scenario.History ?? [])
            await Receive(line, SessionMessageDirection.Outbound);
        foreach (var line in scenario.Messages)
        {
            await Receive(line, SessionMessageDirection.Inbound);
            clock.Advance(TimeSpan.FromMilliseconds(200));
        }
        batchClosing?.Invoke();
        clock.Advance(TimeSpan.FromSeconds(kind == SessionEntryKind.Dm ? 1.5 : 2.5));
        await runtime.WaitForIdleAsync(session, token);
        var outcome = runtime.ReadOutcomes(session).Single();
        var prefix = kind == SessionEntryKind.Dm ? "dm-" : "group-";
        JsonElement? Output(string node) => outcome.Outputs.TryGetValue(prefix + node, out var fields)
            ? JsonSerializer.SerializeToElement(fields) : null;
        var presence = Output("presence");
        var action = presence?.GetProperty("acao").GetString();
        var trace = runtime.ReadTrace(session);
        // A real validated control path must have selected responder, not just raw LLM text.
        var wouldRespond = trace.Any(e => e.NodeId == prefix + "action" && e.EventName == "node-completed" && e.Exit == "case-responder");
        var used = observed.Requests.LastOrDefault(r => r.NodeId == prefix + "presence")?.Inputs;
        var count = used is { } inputs && inputs.TryGetProperty("memorias", out var values) ? values.GetArrayLength() : 0;
        var report = new Report(scenario.Id, scenario.Name, nativeId, scenario.InitialState, scenario.Messages.Length,
            withMemory, memorySource, count, Output("situation"), presence, action, runtime.ReadSession(session)!.CurrentStateId,
            wouldRespond ? "WOULD_RESPOND" : "SILENT", outcome.Success, outcome.Error,
            observed.Calls.Where(c => c.NodeId == prefix + "situation").Sum(c => c.Milliseconds),
            observed.Calls.Where(c => c.NodeId == prefix + "presence").Sum(c => c.Milliseconds),
            observed.Calls.Count, observed.Calls, trace);
        return new(report, observed.Requests, outcome);

        async Task Receive(Line line, SessionMessageDirection direction)
        {
            // Synthetic fixtures explicitly assign provider actor identities; never production name resolution.
            var stable = line.Speaker switch
            {
                "Helena" => "whatsapp:5511944558218@s.whatsapp.net",
                "Bia" => "whatsapp:5511999999998@s.whatsapp.net",
                "Gabriel" => "whatsapp:5511999999997@s.whatsapp.net",
                "Filomena" => "agent:filomena",
                _ => throw new InvalidDataException("Unknown fixture actor.")
            };
            var message = JsonSerializer.SerializeToElement(new
            {
                texto = line.Text,
                remetente = new { nome = line.Speaker, stableId = stable },
                canal = new { nome = kind == SessionEntryKind.Dm ? "DM de teste" : "Grupo de teste", nativeId },
                reply = (object?)null
            });
            var admitted = await runtime.ReceiveAsync(session, new("synthetic-" + ++index, session.ChannelId, session.AccountId,
                nativeId, clock.GetUtcNow(), direction, kind, message), token);
            if (admitted.Status != SessionAdmissionStatus.Accepted) throw new InvalidDataException(admitted.Reason);
        }
    }

    internal sealed class BoundaryMemory : IMemoryReader
    {
        internal List<MemoryQuery> Queries { get; } = [];
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Queries.Add(query);
            var helena = query.Persons.Single(p => p.ActorStableId == "whatsapp:5511944558218@s.whatsapp.net");
            return Task.FromResult<IReadOnlyList<MemoryItem>>([new("synthetic-boundary", query.ConversationNamespace,
                "Helena pediu para Filomena não brincar sobre trabalho.", MemoryCategory.Boundary, [helena], "fake")]);
        }
    }

    internal sealed class FakeModel(string action = "responder", string nextState = "engajada") : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => Task.FromResult(
            request.NodeId.EndsWith("situation", StringComparison.Ordinal)
                ? """{"ambiente":"casual","envolvimento":"direto","ritmo":"aberto","necessita_cuidado":false,"oportunidade_social":"forte","resumo":"Helena chamou Filomena"}"""
                : JsonSerializer.Serialize(new { acao = action, proximo_estado = nextState, motivo = "fixture" }));
    }

    private sealed class ObservedCompletion(IFlowCompletion inner) : IFlowCompletion
    {
        internal List<FlowCompletionRequest> Requests { get; } = [];
        internal List<Call> Calls { get; } = [];
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request);
            var started = Stopwatch.GetTimestamp();
            try { return await inner.CompleteAsync(request, token); }
            finally { Calls.Add(new(request.NodeId, Stopwatch.GetElapsedTime(started).TotalMilliseconds, request.Correction is not null)); }
        }
    }
}
