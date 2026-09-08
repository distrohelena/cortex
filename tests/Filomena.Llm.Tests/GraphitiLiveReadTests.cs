using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Memory;
using Filomena.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

// Read-only tests against existing data. Never seed or repair a missing canary.
public sealed class GraphitiLiveReadTests
{
    public static bool Enabled => GraphitiCanaryTests.Enabled;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Drift = "whatsapp-group-5511981042361-1589823314-g-us";
    private const string Plex = "whatsapp-group-120363318973251043-g-us";
    private const string Dm = "whatsapp-dm-5511944558218-s-whatsapp-net";
    private const string Actor = "whatsapp:5511944558218@s.whatsapp.net";
    private static GraphitiReadClient Client() => new(new Uri(Environment.GetEnvironmentVariable("FILOMENA_GRAPHITI_MCP_ENDPOINT") ?? "http://127.0.0.1:8000/mcp"), TimeSpan.FromSeconds(30));
    private static MemoryQuery Query(string ns, MemoryMode mode, string text) => new(ns, FilomenaIdentity.AgentUuid(ns), mode,
        mode == MemoryMode.Semantic ? MemoryScope.Conversation : MemoryScope.Person,
        mode == MemoryMode.Semantic ? [] : [MemoryIdentity.Person(ns, Actor, "Helena")], text, Enum.GetValues<MemoryCategory>(), 50, 100000);

    [Theory(SkipUnless = nameof(Enabled), Skip = "Requires existing read-only Graphiti canaries.")]
    [InlineData(Drift, "DRIFT42")]
    [InlineData(Plex, "PLEX73")]
    [InlineData(Dm, "PRIVATE91")]
    public async Task SemanticCanaryAndCrossConversationNegatives(string ns, string expected)
    {
        await using var client = Client();
        var reader = new GraphitiMemoryReader(client);
        var found = false;
        foreach (var marker in new[] { "DRIFT42", "PLEX73", "PRIVATE91" })
        {
            var raw = await client.SearchFactsAsync(ns, marker, 50, Token);
            var envelope = raw.TryGetProperty("result", out var nested) ? nested : raw;
            Assert.False(envelope.TryGetProperty("error", out _));
            var results = await reader.QueryAsync(Query(ns, MemoryMode.Semantic, marker), Token);
            Assert.All(results, m => Assert.Equal(ns, m.ConversationNamespace));
            foreach (var other in new[] { "DRIFT42", "PLEX73", "PRIVATE91" }.Where(m => m != expected))
                Assert.DoesNotContain(results, m => m.Content.Contains(other, StringComparison.Ordinal));
            found |= results.Any(m => m.Content.Contains(expected, StringComparison.Ordinal));
            TestContext.Current.TestOutputHelper!.WriteLine($"{ns}; query={marker}; API facts={envelope.GetProperty("facts").GetArrayLength()}; reader facts={results.Count}; cross-chat negatives passed");
        }
        Assert.True(found, $"Required existing canary {expected} was not returned. No data was written.");
    }

    [Theory(SkipUnless = nameof(Enabled), Skip = "Requires existing canonical participants.")]
    [InlineData(Drift, "b4964ff3-b8e0-5a95-8e82-517ab0dc88ee", "DRIFT42")]
    [InlineData(Dm, "87943b77-4bb0-555c-84a7-7443b84a674e", "PRIVATE91")]
    public async Task ExactUsesCanonicalParticipantAndActiveFacts(string ns, string uuid, string marker)
    {
        await using var client = Client();
        var query = Query(ns, MemoryMode.Exact, marker);
        Assert.Equal(Guid.Parse(uuid), query.Persons.Single().ParticipantUuid);
        var results = await new GraphitiMemoryReader(client).QueryAsync(query, Token);
        Assert.Contains(results, m => m.Content.Contains(marker, StringComparison.Ordinal));
        Assert.All(results, m =>
        {
            Assert.Equal(ns, m.ConversationNamespace);
            Assert.Contains(m.Subjects, p => p.ParticipantUuid == Guid.Parse(uuid));
            Assert.Null(m.ExpiredAt);
            Assert.True(m.InvalidAt is null || m.InvalidAt > DateTimeOffset.UtcNow);
        });
        TestContext.Current.TestOutputHelper!.WriteLine($"{ns}: exact canonical recall returned {results.Count} active facts.");
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "Requires existing Graphiti data.")]
    public async Task HybridDeduplicatesRealOverlapAndPreservesAttributionAndBudget()
    {
        await using var client = Client();
        var observed = new ObservedClient(client);
        var reader = new GraphitiMemoryReader(observed);
        var exact = await reader.QueryAsync(Query(Drift, MemoryMode.Exact, "DRIFT42"), Token);
        var semantic = await reader.QueryAsync(Query(Drift, MemoryMode.Semantic, "DRIFT42"), Token);
        var overlap = exact.Select(m => m.Id).Intersect(semantic.Select(m => m.Id)).ToArray();
        Assert.NotEmpty(overlap);
        observed.Calls.Clear();
        var hybrid = await reader.QueryAsync(Query(Drift, MemoryMode.Hybrid, "DRIFT42") with { MaxItems = 8, MaxCharacters = 1800 }, Token);
        Assert.Equal(new[] { "exact:" + Drift, "semantic:" + Drift }, observed.Calls);
        Assert.Equal(hybrid.Count, hybrid.DistinctBy(m => m.Id).Count());
        Assert.All(overlap, id => Assert.Single(hybrid, m => m.Id == id));
        Assert.InRange(hybrid.Count, 1, 8);
        Assert.True(hybrid.Sum(m => m.Content.Length) <= 1800);
        Assert.All(hybrid, m => Assert.Contains(m.Subjects, p => p.ParticipantUuid == Guid.Parse("b4964ff3-b8e0-5a95-8e82-517ab0dc88ee") && p.DisplayName == "Helena"));
        TestContext.Current.TestOutputHelper!.WriteLine($"Hybrid: {hybrid.Count} facts; overlap deduplicated={overlap.Length}; boundaries present={hybrid.Count(m => m.Category == MemoryCategory.Boundary)}.");
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "Requires the existing Plex participant, currently without facts.")]
    public async Task ValidEmptyNeighborhoodIsNotAnUnavailableEndpoint()
    {
        await using var client = Client();
        Assert.Empty(await new GraphitiMemoryReader(client).QueryAsync(Query(Plex, MemoryMode.Exact, "PLEX73"), Token));
        // Reserve a local port without listening: deterministic refusal, no real service stopped.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        await using var unavailable = new GraphitiReadClient(new Uri($"http://127.0.0.1:{port}/mcp"), TimeSpan.FromSeconds(30));
        await Assert.ThrowsAnyAsync<Exception>(() => new GraphitiMemoryReader(unavailable).QueryAsync(Query(Drift, MemoryMode.Exact, "DRIFT42"), Token));
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "Requires Graphiti; completion remains fake.")]
    public async Task UnmodifiedSlice3FixtureRunsWithRealMemoryAndFakeCompletion()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "typed-flow-slice3.json"), Token));
        await using var client = Client();
        var observed = new ObservedClient(client);
        var completion = new FakeCompletion();
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion, new GraphitiMemoryReader(observed));
        runtime.UpdateProject(new SessionFlowProject("slice3-live", 1, "unchanged-fixture", document.RootElement));
        var key = new SessionKey("read-validation", "filomena", "whatsapp", "default", "5511981042361-1589823314@g.us");
        foreach (var id in new[] { "fixture-inbound-1", "fixture-inbound-2" })
            await runtime.ReceiveAsync(key, new SessionMessageEnvelope(id, key.ChannelId, key.AccountId, key.NativeConversationId,
                DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Group,
                JsonSerializer.SerializeToElement(new { texto = "fixture", remetente = new { nome = "Helena", stableId = Actor }, canal = new { nome = "Drift", nativeId = key.NativeConversationId }, reply = (object?)null })), Token);
        await runtime.WaitForIdleAsync(key, Token);
        var outcome = Assert.Single(runtime.ReadOutcomes(key));
        Assert.True(outcome.Success, outcome.Error);
        var inputs = Assert.Single(completion.Requests).Inputs;
        Assert.Equal(2, inputs.GetProperty("mensagens").GetArrayLength());
        Assert.Empty(inputs.GetProperty("historico").EnumerateArray());
        var memories = inputs.GetProperty("memorias");
        Assert.Contains(memories.EnumerateArray(), m => m.GetProperty("conteudo").GetString()!.Contains("DRIFT42", StringComparison.Ordinal));
        Assert.DoesNotContain("b4964ff3-b8e0-5a95-8e82-517ab0dc88ee", inputs.GetRawText());
        Assert.DoesNotContain("95fdb20c-dc66-4ce7-8e4c-3bd4a29bed41", inputs.GetRawText());
        Assert.All(observed.Calls, c => Assert.EndsWith(Drift, c));
        TestContext.Current.TestOutputHelper!.WriteLine($"Unmodified slice3 fixture: successful; {memories.GetArrayLength()} real memories; 3 separate inputs; completion fake; no writes/sends.");
    }

    private sealed class FakeCompletion : IFlowCompletion
    {
        public List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult("""{"ok":true}""");
        }
    }
    private sealed class ObservedClient(IGraphitiReadClient inner) : IGraphitiReadClient
    {
        public List<string> Calls { get; } = [];
        public Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid participant, int limit, CancellationToken token)
        { Calls.Add("exact:" + ns); return inner.GetEntityNeighborhoodAsync(ns, participant, limit, token); }
        public Task<JsonElement> SearchFactsAsync(string ns, string query, int limit, CancellationToken token)
        { Calls.Add("semantic:" + ns); return inner.SearchFactsAsync(ns, query, limit, token); }
    }
}
