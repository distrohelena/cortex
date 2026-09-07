using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class MemoryRuntimeTests
{
    [Theory]
    [InlineData("exact", "conversation")]
    [InlineData("semantic", "person")]
    [InlineData("unknown", "person")]
    public void InvalidModeScopeIsExecutableError(string mode, string scope)
    {
        var project = Project();
        var document = JsonNode.Parse(project.Document.GetRawText())!.AsObject();
        foreach (var state in SessionSliceAcceptanceSupport.States(document))
        {
            var settings = SessionSliceAcceptanceSupport.Node(SessionSliceAcceptanceSupport.Graph(state), "memory")["settings"]!;
            settings["mode"] = mode; settings["scope"] = scope;
        }
        var graph = SessionSliceAcceptanceSupport.Graph(SessionSliceAcceptanceSupport.States(document)[0]);
        var error = Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "group-in"));
        Assert.Contains(error.Issues, i => i.NodeId == "memory");
    }

    [Fact]
    public void MemoryDataWireDoesNotExecuteAnUnreachedNode()
    {
        var document = JsonNode.Parse(Project().Document.GetRawText())!.AsObject();
        var graph = SessionSliceAcceptanceSupport.Graph(SessionSliceAcceptanceSupport.States(document)[0]);
        var edges = graph["controlEdges"]!.AsArray();
        var history = edges.Single(e => e!["source"]!.GetValue<string>() == "history")!;
        history["target"] = "ai";
        edges.Remove(edges.Single(e => e!["source"]!.GetValue<string>() == "memory"));
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "group-in"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptySucceedsButUnavailableFailsBeforeInstruction(bool fail)
    {
        var completion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion, new ResultReader(fail));
        runtime.UpdateProject(Project());
        var key = new SessionKey("r", "c", "whatsapp", "default", "5511944558218@s.whatsapp.net");
        await Receive(runtime, key, "m", "whatsapp:5511944558218@s.whatsapp.net");
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(!fail, Assert.Single(runtime.ReadOutcomes(key)).Success);
        Assert.Equal(fail ? 0 : 1, completion.Requests.Count);
        if (!fail) Assert.Equal(0, completion.Requests.Single().Inputs.GetProperty("memorias").GetArrayLength());
    }

    [Fact]
    public async Task InvalidPersonFailsWithoutCallingReader()
    {
        var reader = new FakeReader();
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new RecordingCompletion([]), reader);
        runtime.UpdateProject(Project());
        var key = new SessionKey("r", "c", "whatsapp", "default", "5511944558218@s.whatsapp.net");
        await Receive(runtime, key, "m", "Helena");
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.False(Assert.Single(runtime.ReadOutcomes(key)).Success);
        Assert.Empty(reader.Queries);
    }

    [Fact]
    public async Task GraphWithoutMemoryNodeNeverCallsReader()
    {
        var reader = new FakeReader();
        await using var runtime = new SessionFlowRuntime(TimeProvider.System,
            new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]), reader);
        runtime.UpdateProject(SessionSliceAcceptanceSupport.Project("without-memory", r => SessionSliceAcceptanceSupport.Configure(r, null, 1, null)));
        var key = new SessionKey("r", "c", "whatsapp", "default", "5511944558218@s.whatsapp.net");
        await Receive(runtime, key, "m", "whatsapp:5511944558218@s.whatsapp.net");
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Empty(reader.Queries);
        Assert.True(Assert.Single(runtime.ReadOutcomes(key)).Success);
    }

    [Fact]
    public async Task BatchPersonsRemoveNullsAndDeduplicateWithoutLosingAttribution()
    {
        var project = Project();
        var document = JsonNode.Parse(project.Document.GetRawText())!.AsObject();
        SessionSliceAcceptanceSupport.Configure(document, null, 4, null);
        var reader = new FakeReader();
        var completion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion, reader);
        runtime.UpdateProject(project with { Document = JsonSerializer.SerializeToElement(document) });
        var key = new SessionKey("r", "c", "whatsapp", "default", "5511981042361-1589823314@g.us");
        await Receive(runtime, key, "1", "whatsapp:5511944558218@s.whatsapp.net");
        await Receive(runtime, key, "2", null);
        await Receive(runtime, key, "3", "whatsapp:5511944558218:3@c.us");
        await Receive(runtime, key, "4", "whatsapp:5511999999999@s.whatsapp.net");
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(2, reader.Queries.Single().Persons.Count);
        var about = completion.Requests.Single().Inputs.GetProperty("memorias")[0].GetProperty("sobre");
        Assert.Equal(2, about.GetArrayLength());
        Assert.NotEqual(about[0].GetProperty("ref").GetString(), about[1].GetProperty("ref").GetString());
    }

    private static ValueTask<SessionAdmission> Receive(SessionFlowRuntime runtime, SessionKey key, string id, string? actor) =>
        runtime.ReceiveAsync(key, new(id, key.ChannelId, key.AccountId, key.NativeConversationId, DateTimeOffset.UtcNow,
            SessionMessageDirection.Inbound, key.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal) ? SessionEntryKind.Group : SessionEntryKind.Dm,
            JsonSerializer.SerializeToElement(new
            {
                texto = "oi",
                remetente = actor is null ? null : new { nome = "Helena", stableId = actor },
                canal = new { nome = "chat", nativeId = key.NativeConversationId },
                reply = (object?)null
            })), TestContext.Current.CancellationToken);

    private sealed class ResultReader(bool fail) : IMemoryReader
    {
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken) => fail
            ? throw new TimeoutException("private service detail") : Task.FromResult<IReadOnlyList<MemoryItem>>([]);
    }

    [Theory]
    [InlineData("5511981042361-1589823314@g.us", "whatsapp-group-5511981042361-1589823314-g-us")]
    [InlineData("120363318973251043@g.us", "whatsapp-group-120363318973251043-g-us")]
    [InlineData("5511944558218@s.whatsapp.net", "whatsapp-dm-5511944558218-s-whatsapp-net")]
    public async Task ExplicitMemoryUsesOneSessionAndSafeNamedInput(string native, string expected)
    {
        var reader = new FakeReader();
        var completion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("inativa", "calar")]);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion, reader);
        runtime.UpdateProject(Project());
        var key = new SessionKey("r", "c", "whatsapp", "default", native);
        var kind = native.EndsWith("@g.us", StringComparison.Ordinal) ? SessionEntryKind.Group : SessionEntryKind.Dm;
        await runtime.ReceiveAsync(key, new("m1", "whatsapp", "default", native, DateTimeOffset.UtcNow,
            SessionMessageDirection.Inbound, kind, JsonSerializer.SerializeToElement(new
            {
                texto = "oi",
                remetente = new { nome = "Helena", stableId = "whatsapp:5511944558218@s.whatsapp.net" },
                canal = new { nome = "apresentação", nativeId = native },
                reply = (object?)null
            })), TestContext.Current.CancellationToken);
        await runtime.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(runtime.ReadOutcomes(key)).Success);
        Assert.Equal(expected, Assert.Single(reader.Queries).ConversationNamespace);
        var inputs = Assert.Single(completion.Requests).Inputs;
        Assert.Equal(3, inputs.EnumerateObject().Count());
        Assert.Equal("limite", inputs.GetProperty("memorias")[0].GetProperty("categoria").GetString());
        Assert.Contains("Helena", inputs.GetProperty("memorias").GetRawText());
        Assert.DoesNotContain(reader.Queries[0].Persons[0].ParticipantUuid.ToString(), inputs.GetRawText());
        Assert.DoesNotContain("whatsapp:", inputs.GetRawText());
        Assert.Contains(runtime.ReadTrace(key), t => t.EventName == "MemoryQueryCompleted");
    }

    internal static SessionFlowProject Project() => SessionSliceAcceptanceSupport.Project("memory", root =>
    {
        SessionSliceAcceptanceSupport.Configure(root, null, 1, null);
        foreach (var state in SessionSliceAcceptanceSupport.States(root))
        {
            var graph = SessionSliceAcceptanceSupport.Graph(state);
            graph["nodes"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"persons","kind":"property","settings":{"path":["remetente"]},"x":400,"y":400}
                """));
            graph["nodes"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"memory","kind":"memory","settings":{"mode":"hybrid","scope":"person","categories":["limite","fato"],"maxItems":8,"maxCharacters":1800,"activeOnly":true,"queryText":"Persona"},"x":600,"y":400}
                """));
            SessionSliceAcceptanceSupport.Node(graph, "ai")["inputs"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"memories","name":"memorias","type":"memory[]","required":true}
                """));
            foreach (var json in new[] {
                """{"source":"accumulate","sourcePort":"messages","target":"persons","targetPort":"object"}""",
                """{"source":"persons","sourcePort":"value","target":"memory","targetPort":"persons"}""",
                """{"source":"memory","sourcePort":"memories","target":"ai","targetPort":"memories"}""" })
                graph["edges"]!.AsArray().Add(JsonNode.Parse(json));
            var wire = graph["controlEdges"]!.AsArray().Single(e => e!["source"]!.GetValue<string>() == "history")!;
            wire["target"] = "memory";
            graph["controlEdges"]!.AsArray().Add(JsonNode.Parse("""
                {"source":"memory","sourcePort":"then","target":"ai","targetPort":"exec"}
                """));
        }
    });

    private sealed class FakeReader : IMemoryReader
    {
        public List<MemoryQuery> Queries { get; } = [];
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<MemoryItem>>([
                new("fact", query.ConversationNamespace, "gosta de Persona 4 Golden", MemoryCategory.Fact, query.Persons, "exact"),
                new("boundary", query.ConversationNamespace, "não brincar com X", MemoryCategory.Boundary, query.Persons, "exact")]);
        }
    }
}
