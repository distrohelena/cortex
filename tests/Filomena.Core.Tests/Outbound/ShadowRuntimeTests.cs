using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Outbound;
using Xunit;

namespace Filomena.Core.Tests.Outbound;

public sealed class ShadowRuntimeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shadow-runtime-" + Guid.NewGuid().ToString("N"));
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "default", "5511944558218@s.whatsapp.net");
    public ShadowRuntimeTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("empty", "shadowed")]
    [InlineData("boundary", "shadowed")]
    [InlineData("block", "blocked")]
    [InlineData("copy", "blocked")]
    [InlineData("kill", "disabled")]
    [InlineData("failure", "no_sink")]
    [InlineData("outage", "no_sink")]
    [InlineData("duplicate", "shadowed")]
    public async Task RealRuntimeRecallCompilerEnforcementAndShadow(string scenario, string expected)
    {
        var project = Project(scenario == "copy", scenario == "duplicate");
        using var host = new ShadowOutboundHost(Path.Combine(directory, "journal.jsonl"), project.Document,
            new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged"), [Session], TransportSafetyCapabilities.Unproven);
        host.Rearm(OutboundMode.Shadow);
        var completion = new Completion(scenario);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion, new Reader(scenario),
            nodeStarting: (visit, _) => { if (visit.NodeId == "send" && scenario == "kill") host.Rearm(OutboundMode.Disabled); return Task.CompletedTask; }, outboundHost: host);
        try { runtime.UpdateProject(project); }
        catch (SessionFlowProjectException error) { Assert.Fail(JsonSerializer.Serialize(error.Issues)); }
        await runtime.ReceiveAsync(Session, new("inbound-1", Session.ChannelId, Session.AccountId, Session.NativeConversationId,
            DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
            JsonSerializer.SerializeToElement(new { texto = "O trabalho demorou.", remetente = new { nome = "Helena", stableId = "whatsapp:5511944558218@s.whatsapp.net" }, canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null })), TestContext.Current.CancellationToken);
        await runtime.WaitForIdleAsync(Session, TestContext.Current.CancellationToken);
        var outcome = Assert.Single(runtime.ReadOutcomes(Session));
        var trace = runtime.ReadTrace(Session);
        if (expected == "no_sink")
        {
            Assert.False(outcome.Success);
            Assert.DoesNotContain(trace, e => e.EventName == "node-outbound");
        }
        else
        {
            Assert.True(outcome.Success, outcome.Error);
            var send = Assert.Single(trace, e => e.EventName == "node-outbound" && e.NodeId == "send");
            Assert.Equal(expected, send.MemoryMetadata!["result"]);
            if (expected == "shadowed") Assert.Equal("engaged", send.MemoryMetadata["wouldTransitionTo"]);
            if (expected == "shadowed" && scenario != "duplicate") Assert.Contains(trace, e => e.EventName == "node-hypothetical-transition" && e.MemoryMetadata!["applied"] == "false");
            if (scenario == "duplicate")
            {
                var duplicate = Assert.Single(trace, e => e.EventName == "node-outbound" && e.NodeId == "send2");
                Assert.Equal("duplicate_attempt", duplicate.MemoryMetadata!["diagnosticCode"]);
                Assert.Equal("2", duplicate.MemoryMetadata["attemptOrdinal"]);
            }
        }
        Assert.Equal("initial", runtime.ReadSession(Session)!.CurrentStateId);
        Assert.Single(runtime.ReadHistory(Session));
        if (scenario == "empty") Assert.Equal(1, completion.Calls);
        if (scenario == "boundary") Assert.Equal(3, completion.Calls);
        if (scenario == "outage") Assert.Equal(0, completion.Calls);
    }

    internal static SessionFlowProject Project(bool copy, bool duplicate = false, string sendExit = "shadowed", bool history = false, long version = 1, bool engagedWithoutDm = false)
    {
        var graph = JsonNode.Parse("""
            {"version":3,"nodes":[
            {"id":"dm","kind":"dm","x":0,"y":0},
            {"id":"batch","kind":"accumulate","settings":{"quietSeconds":null,"maxMessages":1,"maxWaitSeconds":null},"x":0,"y":0},
            {"id":"persons","kind":"property","settings":{"path":["remetente"]},"x":0,"y":0},
            {"id":"recall","kind":"memory","settings":{"mode":"hybrid","scope":"person","categories":["limite"],"maxItems":8,"maxCharacters":1800,"activeOnly":true},"x":0,"y":0},
            {"id":"compile","kind":"interpretBoundaries","settings":{"taxonomyVersion":"v1","grammarVersion":"v1","templatesVersion":"v1"},"x":0,"y":0},
            {"id":"generate","kind":"prompt","prompt":"fixture","inputs":[],"outputMode":"text","responseFields":[{"id":"text","name":"text","type":"text"}],"x":0,"y":0},
            {"id":"enforce","kind":"prompt","prompt":"Enforcement","executionProfile":"boundary_enforcement_v1","provider":"openai","model":"gpt-5.6-luna","reasoningEffort":"medium","maxOutputTokens":1024,
             "boundaryTemplateVersion":"no_teasing_about_topic:v1","semanticContractVersion":"no_teasing_about_topic:semantic-v2","boundaryEvaluatorVersion":"no_teasing_about_topic:evaluator-v2",
             "enforcementStrategy":"multiple_detectors_v1","aggregationPolicyVersion":"any_block_all_allow:v1","complementaryContractVersion":"no_teasing_about_topic:implicit-depreciation-v1",
             "inputs":[{"id":"response","name":"resposta","type":"text","required":true},{"id":"limits","name":"limites","type":"boundary[]","required":true},{"id":"messages","name":"mensagens","type":"message[]","required":true}],"x":0,"y":0},
            {"id":"send","kind":"send","x":0,"y":0},
            {"id":"exit","kind":"exit","settings":{"mode":"switch","targetStateId":"engaged"},"x":0,"y":0}],
            "edges":[
            {"source":"dm","sourcePort":"message","target":"batch","targetPort":"message"},
            {"source":"batch","sourcePort":"messages","target":"persons","targetPort":"object"},
            {"source":"persons","sourcePort":"value","target":"recall","targetPort":"persons"},
            {"source":"recall","sourcePort":"memories","target":"compile","targetPort":"memories"},
            {"source":"compile","sourcePort":"boundaries","target":"enforce","targetPort":"limits"},
            {"source":"generate","sourcePort":"text","target":"enforce","targetPort":"response"},
            {"source":"batch","sourcePort":"messages","target":"enforce","targetPort":"messages"},
            {"source":"generate","sourcePort":"text","target":"send","targetPort":"message"}],
            "controlEdges":[]}
            """)!.AsObject();
        var sequence = new List<string> { "dm", "batch", "recall", "compile", "generate", "enforce" };
        if (history)
        {
            // Same shape as the frozen brain's history nodes: before-current-batch, character messages included.
            graph["nodes"]!.AsArray().Add(JsonNode.Parse("""{"id":"history","kind":"history","settings":{"maxMessages":20,"maxAgeMinutes":15,"maxCharacters":5000,"includeCharacterMessages":true,"mode":"before-current-batch"},"x":0,"y":0}"""));
            sequence.Insert(2, "history");
        }
        if (copy)
        {
            var copier = graph["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "generate")!.DeepClone();
            copier["id"] = "copy";
            graph["nodes"]!.AsArray().Add(copier);
            graph["edges"]!.AsArray().Single(e => e!["target"]!.GetValue<string>() == "send")!["source"] = "copy";
            sequence.Add("copy");
        }
        sequence.Add("send");
        if (duplicate)
        {
            graph["nodes"]!.AsArray().Add(JsonNode.Parse("""{"id":"send2","kind":"send","x":0,"y":0}"""));
            graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"generate","sourcePort":"text","target":"send2","targetPort":"message"}"""));
            sequence.Add("send2");
        }
        sequence.Add("exit");
        for (var i = 1; i < sequence.Count; i++) graph["controlEdges"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { source = sequence[i - 1], sourcePort = sequence[i - 1].StartsWith("send", StringComparison.Ordinal) ? sendExit : "then", target = sequence[i], targetPort = "exec" }));
        // A group-only graph: the engaged state then has no DM entry at all.
        JsonNode engagedGraph = engagedWithoutDm ? JsonNode.Parse("""{"version":3,"nodes":[{"id":"group","kind":"group","x":0,"y":0}],"edges":[],"controlEdges":[]}""")! : graph;
        return new("shadow-tests", version, "v" + version, JsonSerializer.SerializeToElement(new
        {
            version = 1, stateScope = "session", initialStateId = "initial",
            states = new[] { new { id = "initial", name = "Initial", graph = (JsonNode)graph }, new { id = "engaged", name = "Engaged", graph = engagedGraph } }
        }));
    }

    internal sealed class Reader(string scenario) : IMemoryReader
    {
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken)
        {
            if (scenario == "outage") throw new HttpRequestException("offline");
            return Task.FromResult<IReadOnlyList<MemoryItem>>(scenario == "empty" ? [] : [new("source", query.ConversationNamespace, "Não brinque sobre meu trabalho.", MemoryCategory.Boundary, query.Persons, "exact")]);
        }
    }
    internal sealed class Completion(string scenario) : IFlowCompletion
    {
        private int calls;
        public int Calls => calls;
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            if (request.NodeId is "generate" or "copy") return Task.FromResult("Vamos por partes.");
            if (scenario == "failure") throw new HttpRequestException("provider unavailable");
            return Task.FromResult(JsonSerializer.Serialize(request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject()
                .ToDictionary(p => p.Name, p => scenario == "block" && p.Name == "expoe_limite" ? "sim" : "nao")));
        }
    }
    public void Dispose() => Directory.Delete(directory, true);
}
