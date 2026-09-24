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
    private static readonly MemoryPerson OtherPerson = MemoryIdentity.Person(Ns, "whatsapp:5511999999999@s.whatsapp.net", null);
    private static ShadowGraphitiProbeRequest Request => new(Ns, Person.ActorStableId, "local-canary", "foreign-ns", "foreign-canary");
    private static ShadowGraphitiNewDmProbeRequest NewDmRequest => new(Ns, Person.ActorStableId, "foreign-ns", "foreign-canary");

    [Fact]
    public async Task New_DM_requires_exact_subject_absence_and_keeps_empty_recalls_scoped_and_honest()
    {
        var client = new Client("newdm_valid");
        var checks = await new ShadowGraphitiProbe(client).RunNewDmAsync(NewDmRequest, TestContext.Current.CancellationToken);
        Assert.Contains(checks, c => c.Id == "graphiti_participant_absent" && c.Passed && c.Diagnostic == "exact_subject_absent_v1");
        Assert.Contains(checks, c => c.Id == "graphiti_new_dm_ready" && c.Passed);
        Assert.Contains(checks, c => c.Id == "graphiti_boundary_recall" && c.Passed && c.Diagnostic == "success_empty");
        Assert.Contains(checks, c => c.Id == "graphiti_social_recall" && c.Passed && c.Diagnostic == "success_empty");
        Assert.Contains(checks, c => c.Id == "empty_vs_outage" && c.Passed);
        Assert.Contains(client.Queries, q => q.Contains("limites", StringComparison.Ordinal));
        Assert.Contains(client.Queries, q => q.Contains("social", StringComparison.Ordinal));
        Assert.DoesNotContain(checks, c => c.Diagnostic.Contains("namespace_empty", StringComparison.Ordinal));

        var established = await new ShadowGraphitiProbe(new Client("newdm_valid"))
            .RunAsync(Request, TestContext.Current.CancellationToken);
        Assert.Contains(established, c => c.Id == "graphiti_participant" && !c.Passed);
    }

    [Theory]
    [InlineData("newdm_bare_empty")]
    [InlineData("newdm_wrong_namespace")]
    [InlineData("newdm_wrong_subject")]
    [InlineData("newdm_wrong_version")]
    [InlineData("newdm_nonempty_absence")]
    [InlineData("newdm_error")]
    [InlineData("newdm_timeout")]
    [InlineData("newdm_recall_error")]
    [InlineData("newdm_recall_timeout")]
    [InlineData("newdm_missing_foreign_positive")]
    [InlineData("newdm_leak")]
    public async Task New_DM_is_not_ready_without_exact_provenance_and_controls(string mode)
    {
        var client = new Client(mode);
        var checks = await new ShadowGraphitiProbe(client).RunNewDmAsync(NewDmRequest, TestContext.Current.CancellationToken);
        Assert.Contains(checks, c => c.Id == "graphiti_new_dm_ready" && !c.Passed
            && c.Phase == "not_ready");
        Assert.DoesNotContain(checks, c => c.Diagnostic.Contains("private-error", StringComparison.Ordinal));
        if (mode == "newdm_leak")
            Assert.Contains(checks, c => c.Id == "graphiti_canary_isolation" && !c.Passed
                && c.Diagnostic == "confirmed_cross_namespace_leakage");
        if (mode is "newdm_error" or "newdm_timeout" or "newdm_recall_error" or "newdm_recall_timeout")
            Assert.DoesNotContain(checks, c => c.Passed && c.Diagnostic == "success_empty");
    }

    [Theory]
    [InlineData("newdm_recall_missing_endpoint")]
    [InlineData("newdm_recall_missing_person_identity")]
    public async Task New_DM_rejects_positive_recall_facts_without_endpoint_or_subject_provenance(string mode)
    {
        var checks = await new ShadowGraphitiProbe(new Client(mode))
            .RunNewDmAsync(NewDmRequest, TestContext.Current.CancellationToken);

        Assert.Contains(checks, c => c.Id == "graphiti_new_dm_ready" && !c.Passed && c.Phase == "not_ready");
        Assert.DoesNotContain(checks, c => c.Id == "graphiti_boundary_recall" && c.Passed && c.Diagnostic == "success_empty");
        Assert.Contains(checks, c => c.Diagnostic == "recall_provenance_missing");
    }

    [Fact]
    public async Task New_DM_filters_a_structurally_valid_other_person_fact_as_empty_scoped_recall()
    {
        var checks = await new ShadowGraphitiProbe(new Client("newdm_recall_other_person"))
            .RunNewDmAsync(NewDmRequest, TestContext.Current.CancellationToken);

        Assert.Contains(checks, c => c.Id == "graphiti_new_dm_ready" && c.Passed);
        Assert.Contains(checks, c => c.Id == "graphiti_boundary_recall" && c.Passed && c.Diagnostic == "success_empty");
        Assert.Contains(checks, c => c.Id == "graphiti_social_recall" && c.Passed && c.Diagnostic == "success_empty");
    }

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
            if (mode.StartsWith("newdm_", StringComparison.Ordinal))
            {
                if (mode == "newdm_timeout") throw new OperationCanceledException("private-error");
                if (mode == "newdm_error") return Task.FromResult(JsonSerializer.SerializeToElement(new { error = "private-error" }));
                if (mode == "newdm_bare_empty") return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = Array.Empty<object>() }));
                var absence = new
                {
                    status = "entity_not_found",
                    version = mode == "newdm_wrong_version" ? "future" : "entity-neighborhood-absence:v1",
                    group_id = mode == "newdm_wrong_namespace" ? "foreign" : Ns,
                    entity_uuid = mode == "newdm_wrong_subject" ? Guid.NewGuid() : Person.ParticipantUuid,
                    facts = mode == "newdm_nonempty_absence" ? new[] { Fact(Ns, "unexpected") } : Array.Empty<object>()
                };
                return Task.FromResult(JsonSerializer.SerializeToElement(absence));
            }
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
            if (mode.StartsWith("newdm_", StringComparison.Ordinal))
            {
                if (mode == "newdm_recall_timeout" && query.Contains("limites", StringComparison.Ordinal))
                    throw new OperationCanceledException("private-error");
                if (mode == "newdm_recall_error" && query.Contains("limites", StringComparison.Ordinal))
                    return Task.FromResult(JsonSerializer.SerializeToElement(new { error = "private-error" }));
                if (query == "foreign-canary")
                {
                    if (ns == "foreign-ns" && mode != "newdm_missing_foreign_positive")
                        return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = new[] { Fact(ns, "foreign-canary") } }));
                    if (ns != "foreign-ns" && mode == "newdm_leak")
                        return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = new[] { Fact(ns, "foreign-canary") } }));
                }
                if (query.Contains("limites", StringComparison.Ordinal) || query.Contains("social", StringComparison.Ordinal))
                {
                    if (mode == "newdm_recall_missing_endpoint")
                    {
                        var fact = JsonSerializer.SerializeToElement(Fact(ns, "unproven recall"));
                        var malformed = new Dictionary<string, JsonElement>();
                        foreach (var property in fact.EnumerateObject())
                            if (property.Name != "source_node") malformed[property.Name] = property.Value;
                        return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = new[] { malformed } }));
                    }
                    if (mode == "newdm_recall_missing_person_identity")
                        return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = new[] { Fact(ns, "unproven recall", Person, includeStableId: false) } }));
                    if (mode == "newdm_recall_other_person")
                        return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = new[] { Fact(ns, "other person's recall", OtherPerson) } }));
                }
                return Task.FromResult(JsonSerializer.SerializeToElement(new { facts = Array.Empty<object>() }));
            }
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
        private static object Fact(string ns, string content, MemoryPerson? source = null, bool includeStableId = true)
        {
            source ??= Person;
            var sourceNode = new Dictionary<string, object>
            {
                ["uuid"] = source.ParticipantUuid,
                ["labels"] = new[] { "Person" }
            };
            if (includeStableId) sourceNode["stable_id"] = source.ActorStableId;
            return new
            {
                uuid = "fact",
                group_id = ns,
                fact = content,
                name = "LIKES",
                source_node_uuid = source.ParticipantUuid,
                target_node_uuid = "entity",
                source_node = sourceNode,
                target_node = new { uuid = "entity", labels = new[] { "Entity" } }
            };
        }
    }
}
