using System.Diagnostics;
using System.Text.Json;
using Filomena.Core.Identity;
using Cortex.Core.Memory;
using Cortex.Core.Shadow;

namespace Cortex.Memory;

// Host-owned identifiers, never persisted in a project or included in the public report.
public sealed record ShadowGraphitiProbeRequest(string ConversationNamespace, string ParticipantStableId,
    string LocalCanary, string ForeignNamespace, string ForeignCanary);

// Explicit readiness path for a canonical participant whose exact Person node is not yet present.
// It deliberately has no local positive canary: absence is scoped to this subject only.
public sealed record ShadowGraphitiNewDmProbeRequest(string ConversationNamespace, string ParticipantStableId,
    string ForeignNamespace, string ForeignCanary);

/// <summary>Only existing Graphiti read APIs. No models, mutations, canary creation or fallback memories.</summary>
public sealed class ShadowGraphitiProbe(IGraphitiReadClient client)
{
    public static IReadOnlyList<string> CheckIds { get; } = Array.AsReadOnly(new[] { "graphiti_reachable", "graphiti_namespace", "graphiti_participant",
        "graphiti_boundary_recall", "graphiti_social_recall", "graphiti_canary_isolation", "empty_vs_outage" });
    private static readonly string[] NewDmCheckIds = ["graphiti_reachable", "graphiti_namespace", "graphiti_participant_absent",
        "graphiti_boundary_recall", "graphiti_social_recall", "graphiti_canary_isolation", "empty_vs_outage", "graphiti_new_dm_ready"];

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
            var validating = new NamespaceClient(client, Set, person.ParticipantUuid);
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

    public Task<IReadOnlyList<ShadowPreflightCheck>> RunNewDmAsync(
        ShadowGraphitiNewDmProbeRequest request, CancellationToken cancellationToken) =>
        RunScopedSubjectCoreAsync(request, requireAbsence: true, cancellationToken);

    // Explicit host readiness for a scoped subject without a local seeded canary.
    // Established RunAsync and the stricter absent-only DM contract stay unchanged.
    public async Task<IReadOnlyList<ShadowPreflightCheck>> RunScopedSubjectAsync(
        ShadowGraphitiNewDmProbeRequest request, CancellationToken cancellationToken)
    {
        var checks = await RunScopedSubjectCoreAsync(request, requireAbsence: false, cancellationToken);
        return checks.Select(check => check with
        {
            Id = check.Id == "graphiti_new_dm_ready" ? "graphiti_scoped_subject_ready"
                : check.Id == "graphiti_participant_absent" ? "graphiti_scoped_subject" : check.Id,
            Phase = check.Phase == "new_dm_read_only" ? "scoped_subject_read_only" : check.Phase,
        }).ToArray();
    }

    private async Task<IReadOnlyList<ShadowPreflightCheck>> RunScopedSubjectCoreAsync(
        ShadowGraphitiNewDmProbeRequest request, bool requireAbsence, CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, ShadowPreflightCheck>();
        var watch = Stopwatch.StartNew();
        void Set(string id, bool pass, string diagnostic) => checks[id] = new(id, pass, "new_dm_read_only", diagnostic, watch.Elapsed.TotalMilliseconds);
        try
        {
            if (new[] { request.ConversationNamespace, request.ParticipantStableId, request.ForeignNamespace, request.ForeignCanary }
                    .Any(string.IsNullOrWhiteSpace)
                || request.ConversationNamespace == request.ForeignNamespace)
                throw new ArgumentException("Invalid new-DM probe binding.");

            var person = MemoryIdentity.Person(request.ConversationNamespace, request.ParticipantStableId, null);
            var validating = new NamespaceClient(client, Set, person.ParticipantUuid);
            var neighborhood = await validating.GetEntityNeighborhoodAsync(
                request.ConversationNamespace, person.ParticipantUuid, 50, cancellationToken);
            Set("graphiti_reachable", true, "read_succeeded");
            var neighborhoodRoot = Unwrap(neighborhood);
            var exactSubjectAbsent = neighborhoodRoot.ValueKind == JsonValueKind.Object
                && neighborhoodRoot.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String && status.GetString() == "entity_not_found";
            var canonicalSubject = neighborhoodRoot.TryGetProperty("entity", out var entity)
                ? IsPerson(entity, person, request.ConversationNamespace, requireNamespace: true)
                : ContainsScopedPerson(neighborhoodRoot, person, request.ConversationNamespace);
            if (!exactSubjectAbsent && (requireAbsence || !canonicalSubject))
                throw GraphitiResponseValidation.Failure("new_dm_readiness", "exact_subject_absence_unproven");
            Set("graphiti_participant_absent", true, exactSubjectAbsent ? "exact_subject_absent_v1" : "canonical_participant_evidence");

            // The negative search catches a known foreign marker crossing into this scoped DM query.
            var negative = await validating.SearchFactsAsync(request.ConversationNamespace, request.ForeignCanary, 50, cancellationToken);
            if (FactText(negative).Contains(request.ForeignCanary, StringComparison.Ordinal))
                throw GraphitiResponseValidation.Failure("canary_isolation", "confirmed_cross_namespace_leakage");
            var foreign = await validating.SearchFactsAsync(request.ForeignNamespace, request.ForeignCanary, 50, cancellationToken);
            if (!FactText(foreign).Contains(request.ForeignCanary, StringComparison.Ordinal))
                throw GraphitiResponseValidation.Failure("canary_isolation", "foreign_positive_control_not_found");
            Set("graphiti_canary_isolation", true, "foreign_positive_control_and_dm_negative_query");

            // Use the regular memory reader/compiler path. Empty results mean these scoped
            // recalls succeeded with no selected memories; they do not prove namespace emptiness.
            var agentUuid = FilomenaIdentity.AgentUuid(request.ConversationNamespace);
            var reader = new GraphitiMemoryReader(new NewDmRecallClient(validating, request.ConversationNamespace, agentUuid,
                excludeUnboundMentions: !requireAbsence,
                onExcluded: () => Set("graphiti_unbound_mentions", true, "excluded_from_recall")));
            var query = new MemoryQuery(request.ConversationNamespace, agentUuid,
                MemoryMode.Hybrid, MemoryScope.Person, [person], "limites pedidos de interação", [MemoryCategory.Boundary], 8, 1800);
            var boundaries = await reader.QueryAsync(query, cancellationToken);
            Set("graphiti_boundary_recall", true, boundaries.Count == 0 ? "success_empty" : "success_with_memory");
            var social = await reader.QueryAsync(query with
            {
                Text = "memória social",
                Categories = [MemoryCategory.Nickname, MemoryCategory.Relationship, MemoryCategory.Fact]
            }, cancellationToken);
            Set("graphiti_social_recall", true, social.Count == 0 ? "success_empty" : "success_with_memory");
            Set("graphiti_namespace", true, "exact_subject_scope_and_scoped_recall_facts_validated");
            Set("empty_vs_outage", true, "successful_empty_recalls_distinguished_from_errors");
            Set("graphiti_new_dm_ready", true, "exact_subject_absence_and_required_reads_validated");
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
            if (!checks.ContainsKey("graphiti_participant_absent"))
                checks["graphiti_participant_absent"] = new("graphiti_participant_absent", false, "not_ready", code, watch.Elapsed.TotalMilliseconds);
            if (!checks.ContainsKey("graphiti_new_dm_ready"))
                checks["graphiti_new_dm_ready"] = new("graphiti_new_dm_ready", false, "not_ready", code, watch.Elapsed.TotalMilliseconds);
            foreach (var id in NewDmCheckIds.Where(id => !checks.ContainsKey(id)))
                checks[id] = new(id, false, "not_run", code, watch.Elapsed.TotalMilliseconds);
        }
        return NewDmCheckIds.Select(id => checks[id])
            .Concat(checks.Where(p => !NewDmCheckIds.Contains(p.Key)).Select(p => p.Value)).ToArray();
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
    private static bool ContainsScopedPerson(JsonElement value, MemoryPerson person, string ns) =>
        value.ValueKind == JsonValueKind.Object
            ? IsPerson(value, person, ns, requireNamespace: true)
                || value.EnumerateObject().Any(property => ContainsScopedPerson(property.Value, person, ns))
            : value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(item => ContainsScopedPerson(item, person, ns));
    private static bool IsPerson(JsonElement value, MemoryPerson person, string ns, bool requireNamespace) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("uuid", out var uuid) && uuid.ValueKind == JsonValueKind.String
        && Guid.TryParse(uuid.GetString(), out var parsed) && parsed == person.ParticipantUuid
        && value.TryGetProperty("stable_id", out var stable) && stable.ValueKind == JsonValueKind.String && stable.GetString() == person.ActorStableId
        && value.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
        && labels.EnumerateArray().Any(l => l.ValueKind == JsonValueKind.String && l.GetString() == "Person")
        && (value.TryGetProperty("group_id", out var group)
            ? group.ValueKind == JsonValueKind.String && group.GetString() == ns : !requireNamespace);
    private sealed class NamespaceClient(IGraphitiReadClient inner, Action<string, bool, string> report,
        Guid expectedParticipant) : IGraphitiReadClient
    {
        public async Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid person, int limit, CancellationToken token) =>
            GraphitiResponseValidation.Facts(await inner.GetEntityNeighborhoodAsync(ns, person, limit, token), ns,
                (stage, pass, code) => report("exact_" + stage, pass, code), expectedParticipant);
        public async Task<JsonElement> SearchFactsAsync(string ns, string query, int limit, CancellationToken token) =>
            GraphitiResponseValidation.Facts(await inner.SearchFactsAsync(ns, query, limit, token), ns,
                (stage, pass, code) => report("semantic_" + stage, pass, code));
    }

    // New-DM recalls must not let the regular reader's scope filter turn malformed
    // endpoint evidence into an apparently successful empty result. Established
    // recall semantics intentionally remain unchanged.
    private sealed class NewDmRecallClient(IGraphitiReadClient inner, string conversationNamespace,
        Guid agentUuid, bool excludeUnboundMentions = false, Action? onExcluded = null) : IGraphitiReadClient
    {
        public async Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid person, int limit, CancellationToken token) =>
            Validate(await inner.GetEntityNeighborhoodAsync(ns, person, limit, token), person);

        public async Task<JsonElement> SearchFactsAsync(string ns, string query, int limit, CancellationToken token) =>
            Validate(await inner.SearchFactsAsync(ns, query, limit, token));

        private JsonElement Validate(JsonElement response, Guid? requiredParticipant = null)
        {
            var root = Unwrap(response);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("facts", out var facts)
                || facts.ValueKind != JsonValueKind.Array)
                throw GraphitiResponseValidation.Failure("recall_provenance", "recall_provenance_missing");

            var accepted = new List<JsonElement>();
            foreach (var fact in facts.EnumerateArray())
            {
                var sourceValid = Endpoint(fact, "source_node", "source_node_uuid");
                var targetValid = Endpoint(fact, "target_node", "target_node_uuid");
                if (!sourceValid || !targetValid)
                {
                    // Established scoped recalls can contain ordinary mentioned people that
                    // are not transport-bound participants. The runtime reader excludes these
                    // facts too. Missing metadata or contradictory canonical identities still fail.
                    if (excludeUnboundMentions
                        && (sourceValid || UnboundMention(fact, "source_node", "source_node_uuid", requiredParticipant))
                        && (targetValid || UnboundMention(fact, "target_node", "target_node_uuid", requiredParticipant)))
                    {
                        onExcluded?.Invoke();
                        continue;
                    }
                    throw GraphitiResponseValidation.Failure("recall_provenance", "recall_provenance_missing");
                }
                accepted.Add(fact);
            }
            return accepted.Count == facts.GetArrayLength()
                ? root : JsonSerializer.SerializeToElement(new { facts = accepted });
        }

        private bool UnboundMention(JsonElement fact, string nodeKey, string uuidKey, Guid? requiredParticipant)
        {
            if (!fact.TryGetProperty(nodeKey, out var node) || node.ValueKind != JsonValueKind.Object
                || !fact.TryGetProperty(uuidKey, out var endpoint) || endpoint.ValueKind != JsonValueKind.String
                || !node.TryGetProperty("uuid", out var uuid) || uuid.ValueKind != JsonValueKind.String
                || uuid.GetString() != endpoint.GetString() || !Guid.TryParse(uuid.GetString(), out var parsed)
                || parsed == agentUuid || parsed == requiredParticipant
                || !node.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array
                || labels.EnumerateArray().Any(label => label.ValueKind != JsonValueKind.String)
                || !labels.EnumerateArray().Any(label => label.GetString() == "Person")
                || labels.EnumerateArray().Any(label => label.GetString() == "Agent")) return false;
            return !node.TryGetProperty("stable_id", out var stable) || stable.ValueKind == JsonValueKind.Null;
        }

        private bool Endpoint(JsonElement fact, string nodeKey, string uuidKey)
        {
            if (!fact.TryGetProperty(nodeKey, out var node) || node.ValueKind != JsonValueKind.Object
                || !fact.TryGetProperty(uuidKey, out var endpointUuid) || endpointUuid.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(endpointUuid.GetString())
                || !node.TryGetProperty("uuid", out var nodeUuid) || nodeUuid.ValueKind != JsonValueKind.String
                || nodeUuid.GetString() != endpointUuid.GetString()
                || !node.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array
                || !labels.EnumerateArray().Any(label => label.ValueKind == JsonValueKind.String)) return false;

            var labelValues = labels.EnumerateArray().Where(label => label.ValueKind == JsonValueKind.String)
                .Select(label => label.GetString()).ToArray();
            var stableId = node.TryGetProperty("stable_id", out var stable)
                && stable.ValueKind == JsonValueKind.String ? stable.GetString() : null;
            if (labelValues.Contains("Agent") || stableId == "agent:filomena")
                return stableId == "agent:filomena" && Guid.TryParse(nodeUuid.GetString(), out var parsedAgent)
                    && parsedAgent == agentUuid;
            if (stableId is null) return !labelValues.Contains("Person");
            if (string.IsNullOrWhiteSpace(stableId) || !Guid.TryParse(nodeUuid.GetString(), out var parsedPerson)) return false;
            try
            {
                return MemoryIdentity.Person(conversationNamespace, stableId, null).ParticipantUuid == parsedPerson;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }
}
