using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Tests.Flows;
using Xunit;

namespace Filomena.Core.Tests.Boundaries;

public sealed class BoundaryRuntimeTests
{
    [Theory]
    [InlineData("5511981042361-1589823314@g.us", false)]
    [InlineData("120363318973251043@g.us", false)]
    [InlineData("5511944558218@s.whatsapp.net", false)]
    [InlineData("5511944558218@s.whatsapp.net", true)]
    public async Task Trusted_boundary_travels_from_reader_to_instruction_in_its_own_execution(string native, bool abstain)
    {
        var project = MemoryRuntimeTests.Project();
        var root = JsonNode.Parse(project.Document.GetRawText())!.AsObject();
        foreach (var state in SessionSliceAcceptanceSupport.States(root))
        {
            var graph = SessionSliceAcceptanceSupport.Graph(state);
            graph["nodes"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"interpret","kind":"interpretBoundaries","settings":{"taxonomyVersion":"v1","grammarVersion":"v1","templatesVersion":"v1"},"x":700,"y":400}
                """));
            SessionSliceAcceptanceSupport.Node(graph, "ai")["inputs"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "memories")!["type"] = "boundary[]";
            var data = graph["edges"]!.AsArray().Single(e => e!["source"]!.GetValue<string>() == "memory")!;
            data["source"] = "interpret"; data["sourcePort"] = "boundaries";
            graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"memory","sourcePort":"memories","target":"interpret","targetPort":"memories"}"""));
            graph["controlEdges"]!.AsArray().Single(e => e!["source"]!.GetValue<string>() == "memory")!["target"] = "interpret";
            graph["controlEdges"]!.AsArray().Add(JsonNode.Parse("""{"source":"interpret","sourcePort":"then","target":"ai","targetPort":"exec"}"""));
        }
        var completion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion, new Reader(abstain));
        runtime.UpdateProject(project with { Document = JsonSerializer.SerializeToElement(root) });
        var key = new SessionKey("runtime", "character", "whatsapp", "default", native);
        await runtime.ReceiveAsync(key, new("m", "whatsapp", "default", native, DateTimeOffset.UtcNow, SessionMessageDirection.Inbound,
            native.EndsWith("@g.us", StringComparison.Ordinal) ? SessionEntryKind.Group : SessionEntryKind.Dm,
            JsonSerializer.SerializeToElement(new { texto = "oi", remetente = new { nome = "Helena", stableId = "whatsapp:5511944558218@s.whatsapp.net" }, canal = new { nome = "chat", nativeId = native }, reply = (object?)null })), TestContext.Current.CancellationToken);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        if (abstain)
        {
            Assert.False(Assert.Single(runtime.ReadOutcomes(key)).Success);
            Assert.Empty(completion.Requests);
            var failure = Assert.Single(runtime.ReadTrace(key), trace => trace.NodeId == "interpret" && trace.EventName == "node-failed");
            Assert.Equal("private-source", failure.MemoryMetadata!["sourceMemoryId"]);
            Assert.Equal("ambiguous", failure.MemoryMetadata["status"]);
            Assert.DoesNotContain("Não brinque", JsonSerializer.Serialize(failure), StringComparison.Ordinal);
            return;
        }
        Assert.True(Assert.Single(runtime.ReadOutcomes(key)).Success);
        var policy = Assert.Single(completion.Requests).Inputs.GetProperty("memorias")[0];
        Assert.Equal("no_teasing_about_topic:v1", policy.GetProperty("compiled_boundary").GetProperty("policy_template").GetString());
        Assert.DoesNotContain("private-source", policy.GetRawText());
        Assert.DoesNotContain("\"_boundary\"", policy.GetRawText());
        Assert.DoesNotContain("whatsapp:", policy.GetRawText());
    }

    private sealed class Reader(bool ambiguous) : IMemoryReader
    {
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemoryItem>>([new("private-source", query.ConversationNamespace, ambiguous ? "Não brinque sobre isso." : "Não brinque sobre meu trabalho.", MemoryCategory.Boundary, query.Persons, "exact")]);
    }
}
