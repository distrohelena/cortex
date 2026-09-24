using System.Diagnostics;
using System.Text.Json;
using Filomena.Core.Identity;
using Filomena.Core.Memory;
using Filomena.Core.Shadow;

namespace Filomena.Memory;

// Host-owned identifiers, never persisted in a project or included in the public report.
public sealed record ShadowGraphitiProbeRequest(string ConversationNamespace, string ParticipantStableId,
    string LocalCanary, string ForeignNamespace, string ForeignCanary);

/// <summary>Only existing Graphiti read APIs. No models, mutations, canary creation or fallback memories.</summary>
public sealed class ShadowGraphitiProbe(IGraphitiReadClient client)
{
    public static IReadOnlyList<string> CheckIds { get; } = Array.AsReadOnly(new[] { "graphiti_reachable", "graphiti_namespace", "graphiti_participant",
        "graphiti_boundary_recall", "graphiti_social_recall", "graphiti_canary_isolation", "empty_vs_outage" });

    public async Task<IReadOnlyList<ShadowPreflightCheck>> RunAsync(ShadowGraphitiProbeRequest request, CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, ShadowPreflightCheck>();
        var watch = Stopwatch.StartNew();
        void Set(string id, bool pass, string diagnostic) => checks[id] = new(id, pass, "live_read_only", diagnostic, watch.Elapsed.TotalMilliseconds);
        try
        {
            if (new[] { request.ConversationNamespace, request.ParticipantStableId, request.LocalCanary, request.ForeignNamespace, request.ForeignCanary }.Any(string.IsNullOrWhiteSpace)
                || request.ConversationNamespace == request.ForeignNamespace || request.LocalCanary == request.ForeignCanary)
                throw new ArgumentException("Invalid probe binding.");
            var person = MemoryIdentity.Person(request.ConversationNamespace, request.ParticipantStableId, null);
            var validating = new NamespaceClient(client, Set);
            var neighborhood = await validating.GetEntityNeighborhoodAsync(request.ConversationNamespace, person.ParticipantUuid, 50, cancellationToken);
            Set("graphiti_reachable", true, "read_succeeded");
            var root = Unwrap(neighborhood);
            var canonical = root.TryGetProperty("entity", out var entity)
                ? IsPerson(entity, person, request.ConversationNamespace, requireNamespace: true)
                : ContainsPerson(root, person, request.ConversationNamespace);
            Set("graphiti_participant", canonical, "canonical_participant_evidence");
            Set("exact_canonical_subject_valid", canonical, canonical ? "canonical_participant_evidence" : "canonical_subject_unproven");
            if (!canonical) throw GraphitiResponseValidation.Failure("canonical_subject_valid", "canonical_subject_unproven");

            // Namespace metadata is validated before inspecting canary text. No functional recall before isolation.
            var local = await validating.SearchFactsAsync(request.ConversationNamespace, request.LocalCanary, 50, cancellationToken);
            var localText = FactText(local);
            if (localText.Contains(request.ForeignCanary, StringComparison.Ordinal))
                throw GraphitiResponseValidation.Failure("canary_isolation", "confirmed_cross_namespace_leakage");
            var negative = await validating.SearchFactsAsync(request.ConversationNamespace, request.ForeignCanary, 50, cancellationToken);
            if (FactText(negative).Contains(request.ForeignCanary, StringComparison.Ordinal))
                throw GraphitiResponseValidation.Failure("canary_isolation", "confirmed_cross_namespace_leakage");
            var foreign = await validating.SearchFactsAsync(request.ForeignNamespace, request.ForeignCanary, 50, cancellationToken);
            var foreignText = FactText(foreign);
            if (foreignText.Contains(request.LocalCanary, StringComparison.Ordinal))
                throw GraphitiResponseValidation.Failure("canary_isolation", "confirmed_cross_namespace_leakage");
            if (!localText.Contains(request.LocalCanary, StringComparison.Ordinal) || !foreignText.Contains(request.ForeignCanary, StringComparison.Ordinal))
                throw GraphitiResponseValidation.Failure("canary_isolation", "expected_canary_not_found");
            Set("graphiti_canary_isolation", true, "positive_controls_and_cross_namespace_exclusion");

            var reader = new GraphitiMemoryReader(validating);
            var query = new MemoryQuery(request.ConversationNamespace, FilomenaIdentity.AgentUuid(request.ConversationNamespace),
                MemoryMode.Hybrid, MemoryScope.Person, [person], "limites pedidos de interação", [MemoryCategory.Boundary], 8, 1800);
            var boundaries = await reader.QueryAsync(query, cancellationToken);
            Set("graphiti_boundary_recall", true, boundaries.Count == 0 ? "success_empty" : "success_with_memory");
            var social = await reader.QueryAsync(query with { Text = "memória social", Categories = [MemoryCategory.Nickname, MemoryCategory.Relationship, MemoryCategory.Fact] }, cancellationToken);
            Set("graphiti_social_recall", true, social.Count == 0 ? "success_empty" : "success_with_memory");
            Set("empty_vs_outage", true, "successful_results_only_no_error_fallback");

            Set("graphiti_namespace", true, "all_raw_fact_namespaces_matched");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // No raw exception text, endpoint, facts or IDs in the report.
            var code = error.Data["graphiti_code"] as string ?? (error switch
            {
                OperationCanceledException => "timeout",
                HttpRequestException => "backend_unavailable",
                JsonException => "response_schema_invalid",
                InvalidDataException => "memory_validation_failed",
                ArgumentException => "invalid_probe_binding",
                _ => "probe_failure"
            });
            if (error.Data["graphiti_stage"] as string is { } stage) Set("failure_" + stage, false, code);
            foreach (var id in CheckIds.Where(id => !checks.ContainsKey(id)))
                checks[id] = new(id, false, "not_run", code, watch.Elapsed.TotalMilliseconds);
        }
        return CheckIds.Select(id => checks.TryGetValue(id, out var check) ? check : new(id, false, "not_run", "probe_incomplete", 0))
            .Concat(checks.Where(p => !CheckIds.Contains(p.Key)).Select(p => p.Value)).ToArray();
    }

    private static JsonElement Unwrap(JsonElement value) => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1
        && value.TryGetProperty("result", out var result) ? result : value;
    private static string FactText(JsonElement value) => string.Join('\n', Unwrap(value).GetProperty("facts").EnumerateArray()
        .Select(f => f.TryGetProperty("fact", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : ""));
    private static bool ContainsPerson(JsonElement value, MemoryPerson person, string ns)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (IsPerson(value, person, ns, requireNamespace: false)) return true;
            return value.EnumerateObject().Any(p => ContainsPerson(p.Value, person, ns));
        }
        return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(v => ContainsPerson(v, person, ns));
    }
    private static bool IsPerson(JsonElement value, MemoryPerson person, string ns, bool requireNamespace) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("uuid", out var uuid) && uuid.ValueKind == JsonValueKind.String
        && Guid.TryParse(uuid.GetString(), out var parsed) && parsed == person.ParticipantUuid
        && value.TryGetProperty("stable_id", out var stable) && stable.ValueKind == JsonValueKind.String && stable.GetString() == person.ActorStableId
        && value.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
        && labels.EnumerateArray().Any(l => l.ValueKind == JsonValueKind.String && l.GetString() == "Person")
        && (value.TryGetProperty("group_id", out var group)
            ? group.ValueKind == JsonValueKind.String && group.GetString() == ns : !requireNamespace);
    private sealed class NamespaceClient(IGraphitiReadClient inner, Action<string, bool, string> report) : IGraphitiReadClient
    {
        public async Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid person, int limit, CancellationToken token) =>
            GraphitiResponseValidation.Facts(await inner.GetEntityNeighborhoodAsync(ns, person, limit, token), ns,
                (stage, pass, code) => report("exact_" + stage, pass, code));
        public async Task<JsonElement> SearchFactsAsync(string ns, string query, int limit, CancellationToken token) =>
            GraphitiResponseValidation.Facts(await inner.SearchFactsAsync(ns, query, limit, token), ns,
                (stage, pass, code) => report("semantic_" + stage, pass, code));
    }
}
