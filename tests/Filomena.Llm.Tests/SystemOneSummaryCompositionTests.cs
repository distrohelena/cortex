using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Llm.SystemOne;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class SystemOneSummaryCompositionTests
{
    internal const string Summary = "Helena perdeu duas horas no total em duas falhas do sistema e pede ajuda com o relatório.";
    internal static JsonNode Source() => JsonNode.Parse(File.ReadAllText(Path.Combine(ParticipationGateV1Tests.Root, SystemOneParticipationTests.SourceArtifact)))!;

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Summary_reaches_memory_generation_and_presence_without_rewriting_graph(bool direct, bool regenerate)
    {
        var project = Source();
        var bytes = Encoding.UTF8.GetBytes(project.ToJsonString());
        var pin = PinnedShadowProject.FromBytes(bytes, "systemone-summary-isolated", 3, Convert.ToHexString(SHA256.HashData(bytes)));
        using var http = new HttpClient(new SystemOneParticipationTests.Decisions(direct ? "direto" : "indireto", "responder"));
        var baseline = new Baseline(blockFirst: regenerate);
        var memory = new BoundaryCoverageV1Tests.Memory();
        var completion = new SystemOneSummaryComposition(SystemOneParticipationTests.Completion(http), baseline, "laya", "multilingual");
        await using var run = new ManualShadowRun(pin, BoundaryCoverageV1Tests.Binding, BoundaryCoverageV1Tests.Peer, "Helena",
            Directory.CreateTempSubdirectory("systemone-summary-").FullName, BoundaryCoverageV1Tests.Roles, completion, memory);
        var result = await run.InjectAsync("Filomena, perdi duas horas no total em duas falhas do relatório. Me ajuda?", TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(result.Outcomes).Success);
        Assert.Contains(memory.Queries, q => q.Text == Summary);
        Assert.Equal(Summary, Assert.Single(baseline.Calls, c => c.NodeId.EndsWith("-generation")).Inputs.GetProperty("resumo_situacao").GetString());
        if (regenerate) Assert.Equal(Summary, Assert.Single(baseline.Calls, c => c.NodeId.EndsWith("-regeneration")).Inputs.GetProperty("resumo_situacao").GetString());
        var summaryRequest = Assert.Single(baseline.Calls, c => c.NodeId.EndsWith("-situation"));
        var typedRequest = Assert.Single(completion.TypedRequests, c => c.NodeId.EndsWith("-situation"));
        Assert.Equal("lmstudio", summaryRequest.Provider);
        Assert.Equal("google/gemma-4-e4b", summaryRequest.Model);
        Assert.Equal(summaryRequest.System, typedRequest.System);
        Assert.True(JsonElement.DeepEquals(summaryRequest.Inputs, typedRequest.Inputs));
        Assert.Equal("resumo", Assert.Single(summaryRequest.ResponseSchema!.Value.GetProperty("properties").EnumerateObject()).Name);
        Assert.Contains(result.Trace, t => t.EventName == "node-outbound" && t.MemoryMetadata!["result"] == "shadowed");
        Assert.False(result.Outcomes[0].TransitionApplied);
        Assert.Equal(OutboundMode.Shadow, run.OutboundMode);
        Assert.All(run.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        if (direct) Assert.DoesNotContain(completion.TypedRequests, r => r.NodeId.EndsWith("-presence"));
        else Assert.Equal(Summary, Assert.Single(completion.TypedRequests, r => r.NodeId.EndsWith("-presence")).Inputs.GetProperty("resumo_situacao").GetString());
        Assert.True(JsonNode.DeepEquals(Source(), project));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"resumo\":null}")]
    [InlineData("{\"resumo\":\" \"}")]
    [InlineData("{\"resumo\":\"ok\",\"envolvimento\":\"direto\"}")]
    public async Task Missing_or_overreaching_summary_fails_before_dispatch(string response)
    {
        using var http = new HttpClient(new SystemOneParticipationTests.Decisions("direto", "responder"));
        var completion = new SystemOneSummaryComposition(SystemOneParticipationTests.Completion(http), new Baseline(response), "laya", "multilingual");
        var p = DirectParticipationRuleTests.ParticipationProject(Inactive, sourceProject: Source());
        var result = await ExecuteAsync(new(1, "invalid summary", Inactive, [new("Helena", "Filomena, me ajuda?")]), false,
            completion, null, "none", TestContext.Current.CancellationToken, SessionEntryKind.Dm, p);
        Assert.False(result.Report.Success);
        Assert.DoesNotContain(result.Report.Trace, t => t.Exit == DirectParticipationRuleTests.DirectExit || t.EventName == "node-outbound");
    }

    [Fact]
    public async Task Invalid_typed_result_never_falls_back_to_generative_situation()
    {
        using var http = new HttpClient(new SystemOneClientTests.Handler("{}"));
        var baseline = new Baseline();
        var completion = new SystemOneSummaryComposition(SystemOneParticipationTests.Completion(http), baseline, "laya", "multilingual");
        var result = await ExecuteAsync(new(1, "typed outage", Inactive, [new("Helena", "Filomena, me ajuda?")]), false,
            completion, null, "none", TestContext.Current.CancellationToken, SessionEntryKind.Dm,
            DirectParticipationRuleTests.ParticipationProject(Inactive, sourceProject: Source()));
        Assert.False(result.Report.Success);
        Assert.Empty(baseline.Calls);
        Assert.DoesNotContain(result.Report.Trace, t => t.Exit == DirectParticipationRuleTests.DirectExit);
    }

    internal sealed class Baseline(string? summaryResponse = null, bool blockFirst = false) : IFlowCompletion
    {
        internal List<FlowCompletionRequest> Calls { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Calls.Add(request);
            if (request.NodeId.EndsWith("-situation")) return Task.FromResult(summaryResponse ?? JsonSerializer.Serialize(new { resumo = Summary }));
            if (request.NodeId.EndsWith("-presence")) throw new InvalidOperationException("Presence must use typed model.");
            if (!request.NodeId.Contains("validator")) return Task.FromResult("Vamos corrigir o relatório.");
            var fields = request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao");
            if (fields.ContainsKey("relevante_ao_topico")) fields["relevante_ao_topico"] = "sim";
            if (blockFirst && request.NodeId.EndsWith("-1") && fields.ContainsKey("deprecia_experiencia_protegida")) fields["deprecia_experiencia_protegida"] = "sim";
            return Task.FromResult(JsonSerializer.Serialize(fields));
        }
    }
}

// Experiment-only composition. No production registration or graph mutation.
internal sealed class SystemOneSummaryComposition(IFlowCompletion typed, IFlowCompletion baseline, string provider, string model) : IFlowCompletion
{
    internal List<FlowCompletionRequest> TypedRequests { get; } = [];
    internal List<object> SummaryCalls { get; } = [];
    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        if (request.NodeId.EndsWith("-presence")) return await Typed(request, token);
        if (!request.NodeId.EndsWith("-situation")) return await baseline.CompleteAsync(request, token);
        var schema = JsonNode.Parse(request.ResponseSchema!.Value.GetRawText())!;
        var properties = schema["properties"]!.AsObject();
        var summaryDefinition = properties["resumo"]?.DeepClone() ?? throw new InvalidDataException("Summary field missing.");
        properties.Remove("resumo");
        schema["required"] = new JsonArray(properties.Select(p => JsonValue.Create(p.Key)).ToArray());
        var answer = JsonNode.Parse(await Typed(request with { ResponseSchema = JsonSerializer.SerializeToElement(schema) }, token))!.AsObject();
        if (answer.ContainsKey("resumo")) throw new InvalidDataException("Typed provider cannot supply summary.");
        var summarySchema = new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = new JsonObject { ["resumo"] = summaryDefinition },
            ["required"] = new JsonArray("resumo")
        };
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        // Same system prompt, inputs and model as the original node; only output schema is narrowed.
        var text = await baseline.CompleteAsync(request with { ResponseSchema = JsonSerializer.SerializeToElement(summarySchema) }, token);
        using var summary = JsonDocument.Parse(text);
        var root = summary.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty("resumo", out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("Generative summary is invalid; no partial situation may escape.");
        answer["resumo"] = value.GetString();
        SummaryCalls.Add(new { request.NodeId, request.Model, request.Provider, elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, summary = value.GetString() });
        return answer.ToJsonString();
    }

    private Task<string> Typed(FlowCompletionRequest request, CancellationToken token)
    {
        var configured = request with { Provider = provider, Model = model };
        TypedRequests.Add(configured);
        return typed.CompleteAsync(configured, token);
    }
}
