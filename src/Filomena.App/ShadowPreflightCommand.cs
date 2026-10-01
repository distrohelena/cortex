using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cortex.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using Cortex.Memory;
using Cortex.OpenClaw.Observation;

namespace Filomena.App;

// Separate host document. No appsettings/environment fallback and no project-supplied authority.
public sealed record ShadowBootstrapConfig(string ProjectFile, string ProjectId, long ProjectVersion, string ExpectedProjectSha256,
    string Source, string OutboundMode, string JournalDirectory, SessionKey Session, string ConversationNamespace,
    string[] SelfActorIds, string[] PeerActorIds, OutboundBindings OutboundBindings,
    string? GraphitiEndpoint, ShadowGraphitiProbeRequest? GraphitiProbe,
    string? ObservationEndpoint = null, string? ObservationTokenEnvironmentVariable = null,
    string? ObservationSessionRef = null);

public static class ShadowPreflightCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        WriteIndented = true
    };

    // This command has no listener, legacy host builder, completion/provider factory or outbound adapter.
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        var checks = new List<ShadowPreflightCheck>();
        string? revision = null, sessionRef = null, projectId = null, channel = null;
        string? effectiveConfigRevision = null;
        var index = Array.IndexOf(args, "--shadow-preflight");
        var configPath = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[index + 1] : null;
        var transport = args.Contains("--transport-read-only", StringComparer.Ordinal);
        var provisioning = args.Contains("--observation-provisioning-read-only", StringComparer.Ordinal);
        var provisioningReady = false;
        var watch = Stopwatch.StartNew();
        void Check(string id, bool pass, string diagnostic) => checks.Add(new(id, pass, "static", diagnostic, watch.Elapsed.TotalMilliseconds));
        Check("enabled_impossible", true, "phase0_host_has_no_dispatch_even_with_complete_capabilities");
        Check("transport_send_unavailable", true, "bootstrap_has_no_sender_or_legacy_host_dependency");
        checks.AddRange(ShadowSourceEvidence.Unproven());
        try
        {
            if (configPath is null) throw new ArgumentException("configuration_missing");
            if (transport && provisioning) throw new ArgumentException("Choose one preflight phase.");
            if (args.Contains("--live-read-only", StringComparer.Ordinal)) throw new ArgumentException("Select an explicit preflight phase.");
            var config = JsonSerializer.Deserialize<ShadowBootstrapConfig>(await File.ReadAllTextAsync(configPath, cancellationToken), Json)
                ?? throw new JsonException("Missing host config.");
            var basePath = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            var pinned = PinnedShadowProject.FromBytes(await File.ReadAllBytesAsync(Path.GetFullPath(config.ProjectFile, basePath), cancellationToken),
                config.ProjectId, config.ProjectVersion, config.ExpectedProjectSha256);
            revision = pinned.Project.ProjectRevision;
            projectId = pinned.Project.ProjectId; channel = config.Session.ChannelId;
            Check("project_revision", true, "sha256_exact_match_and_runtime_compiler_validated");
            Check("project_pinned", true, "detached_snapshot_no_hot_reload");
            Check("source_selection", config.Source is "openclaw-mcp" or "openclaw-http-bridge" or "native-observation-v1", "explicit_host_source_no_listener");

            var binding = new ShadowSessionBinding(config.Session, config.ConversationNamespace, config.SelfActorIds, config.PeerActorIds);
            sessionRef = binding.OpaqueRef;
            var canonicalDm = config.Session.ChannelId == "whatsapp" && !config.Session.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal)
                && FilomenaIdentity.NormalizeWhatsAppActorNativeId(config.Session.NativeConversationId) == config.Session.NativeConversationId;
            Check("dm_only", canonicalDm, "host_route_shape_only_live_resolution_still_required");
            var namespaceBound = config.ConversationNamespace == FilomenaIdentity.WhatsAppConversationNamespace(config.Session.NativeConversationId);
            Check("configured_namespace_binding", namespaceBound, "existing_runtime_namespace_derivation");
            Check("configured_self_and_peer", config.SelfActorIds.Concat(config.PeerActorIds).All(id => MemoryIdentity.Person(config.ConversationNamespace, id, null).ActorStableId == id), "canonical_disjoint_host_identities_not_live_self_proof");
            Check("reviewed_outbound_bindings", HasReviewedBindings(pinned, config.OutboundBindings), "explicit_roles_and_send_sink_required_in_pinned_revision");

            var journalDirectory = Path.GetFullPath(config.JournalDirectory, basePath);
            if (!Directory.Exists(journalDirectory)) throw new IOException("Operator must provision journal directory.");
            // Proves durable-write access without creating an inbox anchor or a send attempt.
            using (var probe = new FileStream(Path.Combine(journalDirectory, ".preflight-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.DeleteOnClose))
            { probe.WriteByte(1); probe.Flush(flushToDisk: true); }
            var inboxHealthy = ShadowInboundJournal.InspectExisting(Path.Combine(journalDirectory, "shadow-inbound.jsonl"), binding, revision, forNewNonResumableRun: true);
            Check("inbound_journal", inboxHealthy, inboxHealthy ? "storage_valid_for_new_non_resumable_shadow_run" : "inbox_invalid");
            Check("inbound_dedupe", inboxHealthy, "durable_provider_id_registry_available_source_identity_separate_gate");
            using (var host = new ShadowOutboundHost(Path.Combine(journalDirectory, "shadow-outbound.jsonl"), pinned.Project.Document,
                config.OutboundBindings, [binding.Session], TransportSafetyCapabilities.Unproven))
            {
                // A new isolated preflight host starts disabled. Never touches an existing deployment.
                if (config.OutboundMode == "shadow") host.Rearm(OutboundMode.Shadow);
                Check("outbound_shadow", config.OutboundMode == "shadow" && host.EffectiveMode == OutboundMode.Shadow, "explicit_shadow_request_required");
                Check("outbound_journal", !host.RequiresReconciliation(binding.Session), "journal_parsed_and_exclusively_opened_no_attempt_recorded");
            }

            if (transport || provisioning)
            {
                if (checks.Any(c => c.Phase == "static" && !c.Passed) || config.Source != "native-observation-v1"
                    || config.SelfActorIds.Length != 1 || config.PeerActorIds.Length != 1
                    || !Uri.TryCreate(config.ObservationEndpoint, UriKind.Absolute, out var endpoint)
                    || string.IsNullOrWhiteSpace(config.ObservationTokenEnvironmentVariable)
                    || string.IsNullOrWhiteSpace(config.ObservationSessionRef)) throw new ArgumentException("Invalid observation host binding.");
                var token = Environment.GetEnvironmentVariable(config.ObservationTokenEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Observation credential missing.");
                using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
                var reader = new NativeObservationReader(client, endpoint, token, binding, config.ObservationSessionRef, config.SelfActorIds[0], config.PeerActorIds[0]);
                var inspected = await reader.InspectAsync(cancellationToken);
                checks.RemoveAll(c => c.Phase == "transport");
                if (!provisioning) checks.AddRange(NativeObservationPreflight.Check(inspected.Source, config.ObservationSessionRef));
                effectiveConfigRevision = string.IsNullOrWhiteSpace(inspected.Revision) ? null : inspected.Revision;
                checks.AddRange(await reader.ProvisioningChecksAsync(provisioning, cancellationToken, effectiveConfigRevision));
                checks.Add(new("effective_config_snapshot", effectiveConfigRevision is not null, "transport", "native_owner_snapshot", watch.Elapsed.TotalMilliseconds));
                checks.Add(new("effective_config_revision", effectiveConfigRevision is not null, "transport", "revision_bound_inspection", 0));
                foreach (var path in provisioning ? [] : inspected.PathDiagnostics)
                    checks.Add(new("dispatch_path_" + path.Split(':')[0], !path.EndsWith(":unknown", StringComparison.Ordinal), "transport", path, 0));
                if (!provisioning && checks.Where(c => c.Phase == "transport").All(c => c.Passed))
                {
                    var approval = await reader.PreflightAsync(cancellationToken);
                    if (approval.Revision != effectiveConfigRevision) throw new NativeObservationException("preflight_stale");
                    // Atomic read-only Open proves C0; it neither creates a run nor ingests the snapshot.
                    await reader.OpenAsync(approval, cancellationToken);
                    checks.Add(new("effective_config_current", true, "transport", "revision_rechecked_at_snapshot_no_execution", 0));
                }
                if (provisioning)
                {
                    provisioningReady = checks.Where(c => c.Phase is "static" or "transport").All(c => c.Passed);
                    checks.Add(new("capture_armed", false, "transport", "provisioning_only_capture_must_remain_off", 0));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            Check("configuration", false, configPath is null ? "configuration_missing" : error switch
            {
                NativeObservationException observation => observation.Code,
                JsonException or ArgumentException => "invalid_host_configuration",
                SessionFlowProjectException => "invalid_project_revision",
                InvalidDataException => "pin_or_journal_invalid",
                IOException or UnauthorizedAccessException => "local_storage_unavailable",
                _ => "preflight_failed"
            });
        }
        var result = ShadowPreflightResult.Create(checks, revision, sessionRef, projectId, channel, effectiveConfigRevision);
        if (args.Contains("--json", StringComparer.Ordinal)) await output.WriteLineAsync(provisioning
            ? JsonSerializer.Serialize(new { provisioningReady, preflight = result }, Json)
            : JsonSerializer.Serialize(result, Json));
        else
        {
            foreach (var check in result.Checks) await output.WriteLineAsync($"{check.Id,-32} {(check.Passed ? "PASS" : "FAIL")} {check.Phase}: {check.Diagnostic}");
            await output.WriteLineAsync($"Project revision: {revision ?? "unresolved"}");
            await output.WriteLineAsync($"Project id: {projectId ?? "unresolved"}; channel: {channel ?? "unresolved"}");
            await output.WriteLineAsync($"Session opaque ref: {sessionRef ?? "unresolved"} (configured route only; live resolution unproven)");
            await output.WriteLineAsync($"READY FOR SHADOW LIVE: {(result.Ready ? "YES" : "NO")}; listener not started.");
            await output.WriteLineAsync($"TRANSPORT PREFLIGHT: {(result.TransportReady ? "PASS" : "FAIL")}; effective config revision: {effectiveConfigRevision ?? "unresolved"}");
            if (provisioning) await output.WriteLineAsync($"PROVISIONING PREFLIGHT: {(provisioningReady ? "PASS" : "FAIL")}; capture remains OFF.");
        }
        return (provisioning ? provisioningReady : transport ? result.TransportReady : result.Ready) ? 0 : 2;
    }

    private static bool HasReviewedBindings(PinnedShadowProject pin, OutboundBindings bindings)
    {
        var nodes = pin.Project.Document.GetProperty("states").EnumerateArray().SelectMany(s => s.GetProperty("graph").GetProperty("nodes").EnumerateArray()).ToArray();
        bool Has(string id, string kind) => nodes.Any(n => n.GetProperty("id").GetString() == id && n.GetProperty("kind").GetString() == kind);
        return nodes.Any(n => n.GetProperty("kind").GetString() == "send") && bindings.RecallNodes.Count > 0 && bindings.CompilerNodes.Count > 0
            && bindings.CandidatePorts.Count > 0 && bindings.EnforcementNodes.Count > 0
            && bindings.RecallNodes.All(id => Has(id, "memory")) && bindings.CompilerNodes.All(id => Has(id, "interpretBoundaries"))
            && bindings.CandidatePorts.Keys.All(id => Has(id, "prompt")) && bindings.EnforcementNodes.All(id => nodes.Any(n => n.GetProperty("id").GetString() == id
                && n.TryGetProperty("executionProfile", out var profile) && profile.GetString() == "boundary_enforcement_v1"));
    }
}
