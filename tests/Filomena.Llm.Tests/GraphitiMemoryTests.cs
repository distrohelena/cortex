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
