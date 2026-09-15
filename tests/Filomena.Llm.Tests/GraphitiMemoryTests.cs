using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Identity;
using Filomena.Core.Memory;
using Filomena.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GraphitiMemoryTests
{
    private const string Ns = "whatsapp-dm-5511944558218-s-whatsapp-net";
    private static readonly MemoryPerson Person = MemoryIdentity.Person(Ns, "whatsapp:5511944558218@s.whatsapp.net", "Helena");
    private static MemoryQuery Query() => new(Ns, FilomenaIdentity.AgentUuid(Ns), MemoryMode.Hybrid, MemoryScope.Person,
        [Person], "Persona", Enum.GetValues<MemoryCategory>(), 8, 1800);

    [Theory]
    [InlineData(MemoryMode.Exact, "exact")]
    [InlineData(MemoryMode.Semantic, "semantic")]
    [InlineData(MemoryMode.Hybrid, "exact")]
    public async Task Live_success_shape_preserves_identity_namespace_subject_episodes_and_source(MemoryMode mode, string source)
    {
        // Sanitized structural reproduction of the observed live FactSearchResponse, not live evidence.
        var fact = JsonSerializer.SerializeToNode(Fact("sanitized-native-edge", "LIKES"))!;
        fact["created_at"] = "2026-09-01T00:00:00Z";
        fact["valid_at"] = "2026-09-01T00:00:00Z";
        fact["invalid_at"] = null; fact["expired_at"] = null;
        fact["episodes"] = new JsonArray("sanitized-episode");
        fact["attributes"] = new JsonObject();
        var envelope = JsonSerializer.SerializeToElement(new { result = new { message = "Neighborhood retrieved successfully", facts = new[] { fact } } });
        var query = Query() with { Mode = mode, Scope = mode == MemoryMode.Semantic ? MemoryScope.Conversation : MemoryScope.Person };
        var result = Assert.Single(await new GraphitiMemoryReader(new FakeClient(envelope)).QueryAsync(query, TestContext.Current.CancellationToken));
        Assert.Equal("sanitized-native-edge", result.Id);
        Assert.Equal(Ns, result.ConversationNamespace);
        Assert.Equal(source, result.SourceKind);
        Assert.Equal(Person.ParticipantUuid, Assert.Single(result.Subjects).ParticipantUuid);
        Assert.Contains("sanitized-episode", result.EvidenceEpisodeIds!);
    }

    [Theory]
    [InlineData(null, "namespace_provenance_missing")]
    [InlineData("", "namespace_provenance_missing")]
    [InlineData("foreign", "confirmed_cross_namespace_leakage")]
    public async Task Missing_provenance_is_not_a_foreign_namespace(string? ns, string code)
    {
        var fact = JsonSerializer.SerializeToNode(Fact("id", "LIKES"))!;
        fact["group_id"] = ns;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new GraphitiMemoryReader(new FakeClient(
            JsonSerializer.SerializeToElement(new { facts = new[] { fact } }))).QueryAsync(Query(), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Data["graphiti_code"]);
    }

    [Fact]
    public async Task Hybrid_backend_error_cannot_return_partial_exact_memories()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new GraphitiMemoryReader(new HybridOutageClient()).QueryAsync(Query(), TestContext.Current.CancellationToken));
        Assert.Equal("backend_read_error", error.Data["graphiti_code"]);
        Assert.DoesNotContain("Connection error", error.Message);
    }

    private sealed class HybridOutageClient : IGraphitiReadClient
    {
        public Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid person, int limit, CancellationToken token) =>
            Task.FromResult(JsonSerializer.SerializeToElement(new { facts = new[] { Fact("f", "LIKES") } }));
        public Task<JsonElement> SearchFactsAsync(string ns, string text, int limit, CancellationToken token) =>
            Task.FromResult(JsonSerializer.SerializeToElement(new { result = new { error = "Error searching facts: Connection error." } }));
    }

    [Fact]
    public async Task HybridCallsOnlyReadOperationsWithOneNamespaceAndCanonicalPerson()
    {
        var client = new FakeClient(JsonSerializer.SerializeToElement(new { facts = new[] { Fact("f", "LIKES"), Fact("b", "REQUESTED_BOUNDARY") } }));
        var result = await new GraphitiMemoryReader(client).QueryAsync(Query(), TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal(MemoryCategory.Boundary, result[0].Category);
        Assert.Equal(["exact:" + Ns, "semantic:" + Ns], client.Calls);
        Assert.Equal(Person.ParticipantUuid, result[0].Subjects.Single().ParticipantUuid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyOrForgedPersonIsNotAdopted(bool legacy)
    {
        var fact = Fact("f", "LIKES", legacy ? null : "whatsapp:5511999999999@s.whatsapp.net");
        var client = new FakeClient(JsonSerializer.SerializeToElement(new { facts = new[] { fact } }));
        Assert.Empty(await new GraphitiMemoryReader(client).QueryAsync(Query(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidExpiredAndFutureFactsAreFilteredLocally()
    {
        var all = new JsonArray();
        foreach (var (key, value) in new[] { ("invalid_at", "2000-01-01T00:00:00Z"), ("expired_at", "2099-01-01T00:00:00Z"), ("valid_at", "2099-01-01T00:00:00Z") })
        {
            var f = JsonSerializer.SerializeToNode(Fact(key, "LIKES"))!;
            f[key] = value; all.Add(f);
        }
        var client = new FakeClient(JsonSerializer.SerializeToElement(new JsonObject { ["facts"] = all }));
        Assert.Empty(await new GraphitiMemoryReader(client).QueryAsync(Query(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("amor", 2, false)]
    [InlineData("Professor", 1, false)]
    [InlineData("Professor", 2, true)]
    public async Task NicknamesRequireDistinctTurnsAndNonGenericTerms(string term, int turns, bool established)
    {
        var f = JsonSerializer.SerializeToNode(Fact("address", "USED_ADDRESS_TERM"))!;
        f["target_node_uuid"] = Query().AgentUuid.ToString();
        f["target_node"] = JsonSerializer.SerializeToNode(new { uuid = Query().AgentUuid, stable_id = "agent:filomena", labels = new[] { "Agent" } });
        f["attributes"] = JsonSerializer.SerializeToNode(new { display_term = term, evidence_run_ids = turns == 2 ? new[] { "r1", "r2" } : new[] { "r1", "r1" } });
        var client = new FakeClient(JsonSerializer.SerializeToElement(new JsonObject { ["facts"] = new JsonArray(f) }));
        var result = await new GraphitiMemoryReader(client).QueryAsync(Query(), TestContext.Current.CancellationToken);
        if (established) Assert.Equal(MemoryCategory.Nickname, Assert.Single(result).Category);
        else Assert.Empty(result);
    }

    [Fact]
    public async Task SemanticPersonScopeRejectsUnrelatedCanonicalEndpoints()
    {
        var other = MemoryIdentity.Person(Ns, "whatsapp:5511999999999@s.whatsapp.net", "Helena");
        var f = JsonSerializer.SerializeToNode(Fact("other", "LIKES"))!;
        f["source_node_uuid"] = other.ParticipantUuid.ToString();
        f["source_node"] = JsonSerializer.SerializeToNode(new { uuid = other.ParticipantUuid, stable_id = other.ActorStableId, labels = new[] { "Person" } });
        var client = new FakeClient(JsonSerializer.SerializeToElement(new JsonObject { ["facts"] = new JsonArray(f) }));
        Assert.Empty(await new GraphitiMemoryReader(client).QueryAsync(Query(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InstalledMcpResultEnvelopeIsSupported()
    {
        var client = new FakeClient(JsonSerializer.SerializeToElement(new { result = new { facts = new[] { Fact("f", "LIKES") } } }));
        Assert.Single(await new GraphitiMemoryReader(client).QueryAsync(Query(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BackendErrorIsNotAnEmptyMemory()
    {
        var client = new FakeClient(JsonSerializer.SerializeToElement(new { error = "internal details should not reach prompt" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GraphitiMemoryReader(client).QueryAsync(Query(), TestContext.Current.CancellationToken));
    }

    private static object Fact(string id, string name, string? stableId = "whatsapp:5511944558218@s.whatsapp.net") => new
    {
        uuid = id,
        group_id = Ns,
        fact = "observação",
        name,
        source_node_uuid = Person.ParticipantUuid,
        target_node_uuid = "game-id",
        source_node = new { uuid = Person.ParticipantUuid, labels = new[] { "Person" }, stable_id = stableId, canonical = true },
        target_node = new { uuid = "game-id", labels = new[] { "Entity" }, canonical = false }
    };

    private sealed class FakeClient(JsonElement response) : IGraphitiReadClient
    {
        public List<string> Calls { get; } = [];
        public Task<JsonElement> GetEntityNeighborhoodAsync(string conversationNamespace, Guid participant, int limit, CancellationToken token)
        {
            Assert.Equal(Person.ParticipantUuid, participant);
            Assert.Equal(50, limit);
            Calls.Add("exact:" + conversationNamespace);
            return Task.FromResult(response);
        }
        public Task<JsonElement> SearchFactsAsync(string conversationNamespace, string query, int limit, CancellationToken token)
        {
            Calls.Add("semantic:" + conversationNamespace);
            return Task.FromResult(response);
        }
    }
}
