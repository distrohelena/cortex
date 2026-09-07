using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Xunit;

namespace Filomena.Core.Tests.Flows;

internal static class SessionSliceAcceptanceSupport
{
    public static JsonObject Fixture() => JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();

    public static SessionFlowProject Project(string revision, Action<JsonObject>? configure = null, long version = 1)
    {
        var root = Fixture();
        configure?.Invoke(root);
        return new SessionFlowProject("slice2", version, revision, JsonSerializer.SerializeToElement(root));
    }

    public static void Configure(JsonObject root, double? quietSeconds, int? maxMessages, double? maxWaitSeconds, string? revisionForPrompt = null, string? prompt = null)
    {
        foreach (var state in States(root))
        {
            var graph = Graph(state);
            var settings = Node(graph, "accumulate")["settings"]!.AsObject();
            settings["quietSeconds"] = quietSeconds;
            settings["maxMessages"] = maxMessages;
            settings["maxWaitSeconds"] = maxWaitSeconds;
            if (revisionForPrompt is not null) Node(graph, "ai")["prompt"] = "graph:" + StateId(state) + ":" + revisionForPrompt;
            if (prompt is not null) Node(graph, "ai")["prompt"] = prompt;
        }
    }

    public static void ConfigureHistory(JsonObject root, int maxMessages, double? maxAgeMinutes, int maxCharacters, bool includeCharacterMessages)
    {
        foreach (var state in States(root))
        {
            var settings = Node(Graph(state), "history")["settings"]!.AsObject();
            settings["maxMessages"] = maxMessages;
            settings["maxAgeMinutes"] = maxAgeMinutes;
            settings["maxCharacters"] = maxCharacters;
            settings["includeCharacterMessages"] = includeCharacterMessages;
        }
    }

    public static void RenameAccumulator(JsonObject root, string stateId, string replacement)
    {
        var graph = Graph(State(root, stateId));
        Node(graph, "accumulate")["id"] = replacement;
        foreach (var edge in graph["edges"]!.AsArray())
        {
            if (edge!["source"]!.GetValue<string>() == "accumulate") edge["source"] = replacement;
            if (edge["target"]!.GetValue<string>() == "accumulate") edge["target"] = replacement;
        }
        foreach (var edge in graph["controlEdges"]!.AsArray())
        {
            if (edge!["source"]!.GetValue<string>() == "accumulate") edge["source"] = replacement;
            if (edge["target"]!.GetValue<string>() == "accumulate") edge["target"] = replacement;
        }
    }

    public static SessionKey Key(string conversation, string channel = "group") => new("fixture-runtime", "filomena", channel, "fixture-account", conversation);

    public static ValueTask<SessionAdmission> ReceiveAsync(
        SessionFlowRuntime runtime,
        SessionKey key,
        string providerMessageId,
        SessionEntryKind entry,
        string? text = null,
        SessionMessageDirection direction = SessionMessageDirection.Inbound,
        DateTimeOffset? timestamp = null) =>
        runtime.ReceiveAsync(key, new SessionMessageEnvelope(providerMessageId, key.ChannelId, key.AccountId, key.NativeConversationId,
            timestamp ?? DateTimeOffset.UnixEpoch, direction, entry,
            JsonSerializer.SerializeToElement(new { texto = text ?? providerMessageId, remetente = new { nome = "Fixture", stableId = "fixture:sender" }, canal = new { nome = key.ChannelId, nativeId = key.NativeConversationId }, reply = (object?)null })), TestContext.Current.CancellationToken);

    public static string Response(string state, string action = "responder") => JsonSerializer.Serialize(new { acao = action, proximo_estado = state, motivo = "fixture" });
    public static string[] Texts(JsonElement messages) => messages.EnumerateArray().Select(message => message.GetProperty("texto").GetString()!).ToArray();
    public static JsonObject State(JsonObject root, string id) => States(root).Single(state => StateId(state) == id);
    public static JsonObject Graph(JsonObject state) => state["graph"]!.AsObject();
    public static JsonObject Node(JsonObject graph, string id) => graph["nodes"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == id)!.AsObject();
    public static string StateId(JsonObject state) => state["id"]!.GetValue<string>();
    public static IReadOnlyList<JsonObject> States(JsonObject root) => root["project"]!["states"]!.AsArray().Select(state => state!.AsObject()).ToArray();

    private static string FixturePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "typed-flow-slice2.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Slice 2 fixture was not found from the test output directory.");
    }
}

internal sealed class RecordingCompletion(IEnumerable<string> responses, int? blockAtCall = null) : IFlowCompletion
{
    private readonly Queue<string> responses = new(responses);
    private readonly TaskCompletionSource<string> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int calls;
    private int active;
    private int maxConcurrent;

    public ConcurrentQueue<FlowCompletionRequest> Requests { get; } = [];
    public TaskCompletionSource BlockStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        Requests.Enqueue(request);
        var concurrent = Interlocked.Increment(ref active);
        while (true)
        {
            var prior = Volatile.Read(ref maxConcurrent);
            if (prior >= concurrent || Interlocked.CompareExchange(ref maxConcurrent, concurrent, prior) == prior) break;
        }
        try
        {
            if (Interlocked.Increment(ref calls) == blockAtCall)
            {
                string planned;
                lock (responses) planned = responses.Dequeue();
                BlockStarted.TrySetResult();
                await release.Task.WaitAsync(token);
                return planned;
            }
            lock (responses) return responses.Dequeue();
        }
        finally { Interlocked.Decrement(ref active); }
    }

    public void Release() => release.TrySetResult("released");
}
