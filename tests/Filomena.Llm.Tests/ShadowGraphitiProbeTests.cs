using System.Text.Json;
using Filomena.Core.Identity;
using Filomena.Core.Memory;
using Filomena.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ShadowGraphitiProbeTests
{
    [Fact]
    public async Task Foreign_canary_stops_before_functional_boundary_or_social_recall()
    {
        var client = new Client("leak");
        var checks = await new ShadowGraphitiProbe(client).RunAsync(Request, TestContext.Current.CancellationToken);
        Assert.Contains(checks, c => c.Diagnostic == "confirmed_cross_namespace_leakage");
        Assert.DoesNotContain(client.Queries, q => q.Contains("limites", StringComparison.Ordinal) || q.Contains("social", StringComparison.Ordinal));
        Assert.DoesNotContain(checks, c => c.Id == "graphiti_boundary_recall" && c.Passed);
    }

    [Theory]
    [InlineData("backend_error", "backend_read_error")]
    [InlineData("missing_provenance", "namespace_provenance_missing")]
    [InlineData("invalid_schema", "response_schema_invalid")]
    public async Task Distinguishes_backend_error_missing_provenance_and_invalid_schema(string mode, string code)
    {
        var checks = await new ShadowGraphitiProbe(new Client(mode)).RunAsync(Request, TestContext.Current.CancellationToken);
        Assert.Contains(checks, c => !c.Passed && c.Diagnostic == code);
        Assert.DoesNotContain(checks, c => c.Diagnostic == "invalid_or_cross_namespace_result");
        Assert.DoesNotContain(checks, c => c.Diagnostic == "confirmed_cross_namespace_leakage");
        Assert.Contains(checks, c => c.Id == "graphiti_boundary_recall" && c.Phase == "not_run" && !c.Passed);
    }

    private const string Ns = "whatsapp-dm-5511000000000-s-whatsapp-net";
    private static readonly MemoryPerson Person = MemoryIdentity.Person(Ns, "whatsapp:5511000000000@s.whatsapp.net", null);
    private static ShadowGraphitiProbeRequest Request => new(Ns, Person.ActorStableId, "local-canary", "foreign-ns", "foreign-canary");

    [Theory]
    [InlineData("healthy", true)]
    [InlineData("empty", true)]
    [InlineData("outage", false)]
    [InlineData("wrong_namespace", false)]
    [InlineData("wrong_node_namespace", false)]
    [InlineData("leak", false)]
    [InlineData("missing_positive_control", false)]
    [InlineData("missing_person", false)]
    [InlineData("entity_empty", true)]
    [InlineData("entity_missing_namespace", false)]
    [InlineData("entity_wrong_namespace", false)]
    [InlineData("entity_wrong_actor", false)]
    [InlineData("entity_wrong_uuid", false)]
    public async Task Read_only_probe_distinguishes_evidence_from_empty_and_outage(string mode, bool pass)
    {
        var fake = new Client(mode);
        var checks = await new ShadowGraphitiProbe(fake).RunAsync(Request, TestContext.Current.CancellationToken);
        Assert.Equal(pass, checks.All(c => c.Passed));
        if (mode == "empty")
        {
            Assert.Contains(checks, c => c.Id == "graphiti_boundary_recall" && c.Passed && c.Diagnostic == "success_empty");
            Assert.Contains(checks, c => c.Id == "graphiti_social_recall" && c.Passed && c.Diagnostic == "success_empty");
        }
        if (mode == "outage") Assert.DoesNotContain(checks, c => c.Diagnostic == "success_empty");
        if (mode == "leak") Assert.Contains(checks, c => c.Id == "graphiti_canary_isolation" && !c.Passed);
        Assert.DoesNotContain(checks, c => c.Diagnostic.Contains("private-error", StringComparison.Ordinal));
        Assert.True(fake.Calls > 0);
    }

    private sealed class Client(string mode) : IGraphitiReadClient
    {
        public int Calls { get; private set; }
        public List<string> Queries { get; } = [];
        public Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid participant, int limit, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(Person.ParticipantUuid, participant);
            if (mode == "outage") throw new HttpRequestException("private-error");
            if (mode.StartsWith("entity_", StringComparison.Ordinal))
            {
                var entity = new Dictionary<string, object>
                {
                    ["uuid"] = mode == "entity_wrong_uuid" ? Guid.Empty : Person.ParticipantUuid,
                    ["stable_id"] = mode == "entity_wrong_actor" ? "other" : Person.ActorStableId,
                    ["labels"] = new[] { "Entity", "Person" }
                };
                if (mode != "entity_missing_namespace")
                    entity["group_id"] = mode == "entity_wrong_namespace" ? "foreign" : Ns;
                return Task.FromResult(JsonSerializer.SerializeToElement(new { entity, facts = Array.Empty<object>() }));
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                nodes = mode == "missing_person" ? Array.Empty<object>() : new object[] { new { uuid = Person.ParticipantUuid, stable_id = Person.ActorStableId, group_id = mode == "wrong_node_namespace" ? "foreign" : Ns, labels = new[] { "Person" } } },
                facts = mode == "healthy" ? new[] { Fact(Ns, "memory") } : Array.Empty<object>()
            }));
        }
        public Task<JsonElement> SearchFactsAsync(string ns, string query, int limit, CancellationToken cancellationToken)
        {
            Calls++;
            Queries.Add(query);
            // Sanitized actual live failure: valid MCP success envelope containing backend ErrorResponse.
            if (mode == "backend_error") return Task.FromResult(JsonSerializer.SerializeToElement(new { result = new { error = "Error searching facts: Connection error." } }));
            if (mode == "invalid_schema") return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = "not-an-array" }));
            if (mode == "missing_provenance") return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = new[] { new { uuid = "f", fact = "test" } } }));
            if (mode == "outage") throw new HttpRequestException("private-error");
            if (query.Contains("canary", StringComparison.Ordinal))
            {
                var content = ns == "foreign-ns" ? "foreign-canary" : mode == "leak" ? "local-canary foreign-canary" : "local-canary";
                return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = mode == "missing_positive_control" ? Array.Empty<object>() : new[] { Fact(mode == "wrong_namespace" ? "bad" : ns, content) } }));
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = Array.Empty<object>() }));
        }
        private static object Fact(string ns, string content) => new
        {
            uuid = "fact",
            group_id = ns,
            fact = content,
            name = "LIKES",
            source_node_uuid = Person.ParticipantUuid,
            target_node_uuid = "entity",
            source_node = new { uuid = Person.ParticipantUuid, stable_id = Person.ActorStableId, labels = new[] { "Person" } },
            target_node = new { uuid = "entity", labels = new[] { "Entity" } }
        };
    }
}
