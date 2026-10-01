using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Outbound;

namespace Cortex.Core.Shadow;

internal static class LiveContextMigration
{
    private const string Inbox = "observation-live-inbound.jsonl";
    private const string Outbox = "observation-live-outbound.jsonl";

    // Offline, explicitly approved revision change. Unlike legacy reconstruction,
    // this preserves the checkpoint's exact timestamps, state and dedupe history.
    internal static SessionSnapshot TransferCheckpoint(string source, string target, ShadowSessionBinding binding,
        PinnedShadowProject oldProject, PinnedShadowProject project, string inboxSha256, string outboxSha256, string checkpointSha256)
    {
        Require(!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any(), "target_not_empty");
        ValidateProjectChange(oldProject, project);
        var checkpoint = Path.Combine(source, "observation-live-context.json");
        Require(Hash(Path.Combine(source, Inbox)) == inboxSha256 && Hash(Path.Combine(source, Outbox)) == outboxSha256
            && Hash(checkpoint) == checkpointSha256, "source_hash_mismatch");
        using var lease = new OutboundJournal(Path.Combine(source, Outbox));
        Require(!lease.RequiresReconciliation(binding.OpaqueRef), "unresolved_outbound");
        Require(ShadowInboundJournal.InspectExisting(Path.Combine(source, Inbox), binding,
            oldProject.Project.ProjectRevision, forNewNonResumableRun: true), "invalid_inbox");
        var context = new LiveSessionContextStore(source, binding, oldProject).Load()
            ?? throw new InvalidDataException("context_import_missing_checkpoint");
        Require(context.Snapshot.ProjectRevision == oldProject.Project.ProjectRevision, "checkpoint_revision_mismatch");
        var next = context with { Snapshot = context.Snapshot with { ProjectRevision = project.Project.ProjectRevision } };
        var runtime = new SessionFlowRuntime(TimeProvider.System, new NoCompletion());
        try { runtime.UpdateProject(project.Project); runtime.RestoreContext(binding.Session, next, binding.Kind); }
        finally { runtime.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        File.Copy(Path.Combine(source, Inbox), Path.Combine(target, Inbox));
        File.Copy(Path.Combine(source, Outbox), Path.Combine(target, Outbox));
        Require(Hash(Path.Combine(source, Inbox)) == inboxSha256 && Hash(Path.Combine(target, Inbox)) == inboxSha256
            && Hash(Path.Combine(target, Outbox)) == outboxSha256 && Hash(checkpoint) == checkpointSha256, "source_changed");
        new LiveSessionContextStore(target, binding, project).Save(next);
        File.WriteAllText(Path.Combine(target, "context-import-audit.json"), JsonSerializer.Serialize(new {
            version = "checkpoint-revision-transfer:v1", inboxSha256, outboxSha256, checkpointSha256,
            sourceRevision = oldProject.Project.ProjectRevision, targetRevision = project.Project.ProjectRevision,
            historyCount = next.History.Count, exactTimestampsPreserved = true, executions = 0, sends = 0 }));
        return next.Snapshot;
    }

    // Explicit offline import into a NEW private directory. Sources are never changed. This
    // is not startup recovery: it cannot execute, reconcile an uncertain send or grant authority.
    internal static SessionSnapshot Import(string source, string target, string auditFile, ShadowSessionBinding binding,
        PinnedShadowProject oldProject, PinnedShadowProject project, string inboxSha256, string outboxSha256, string auditSha256)
    {
        Require(!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any(), "target_not_empty");
        Require(Hash(Path.Combine(source, Inbox)) == inboxSha256 && Hash(Path.Combine(source, Outbox)) == outboxSha256
            && Hash(auditFile) == auditSha256, "source_hash_mismatch");
        ValidateProjectChange(oldProject, project);
        // Read one immutable set; recheck before publishing the checkpoint.
        var inputs = Read(Path.Combine(source, Inbox));
        var attempts = Read(Path.Combine(source, Outbox)).Select(x => x.Deserialize<OutboundJournalEntry>()
            ?? throw new InvalidDataException("import_invalid_outbox")).ToArray();
        var audit = Read(auditFile);
        File.Copy(Path.Combine(source, Inbox), Path.Combine(target, Inbox));
        File.Copy(Path.Combine(source, Outbox), Path.Combine(target, Outbox));
        Require(ShadowInboundJournal.InspectExisting(Path.Combine(target, Inbox), binding,
            oldProject.Project.ProjectRevision, forNewNonResumableRun: true), "invalid_inbox");
        using (var journal = new OutboundJournal(Path.Combine(target, Outbox)))
            Require(!journal.RequiresReconciliation(binding.OpaqueRef), "unresolved_outbound");
        Require(attempts.All(x => x.SessionRef == binding.OpaqueRef), "wrong_outbound_session");

        var observed = inputs.Where(x => Text(x, "Type") == "observed").ToArray();
        Require(observed.Length > 0 && !inputs.Any(x => Text(x, "Type") == "quarantined"), "missing_or_quarantined_input");
        var completed = inputs.Where(x => Text(x, "Type") is "completed" or "execution_failed")
            .ToDictionary(x => Text(x, "InputRef"), StringComparer.Ordinal);
        var envelopes = new List<SessionMessageEnvelope>();
        var inboundByRef = new Dictionary<string, CanonicalShadowInbound>(StringComparer.Ordinal);
        foreach (var entry in observed)
        {
            var input = entry.GetProperty("Input").Deserialize<CanonicalShadowInbound>()!;
            var reference = Text(entry, "InputRef");
            Require(binding.Accepts(input) && !input.FromMe && !binding.IsSelf(input.ActorId)
                && completed.ContainsKey(reference), "unresolved_or_wrong_input");
            inboundByRef.Add(reference, input);
        }
        var receipts = attempts.GroupBy(x => x.AttemptRef).Where(g => g.Last().Status == "history_committed")
            .Select(g => (Reserved: g.First(), Sent: g.Single(x => x.Status == "sent"))).ToArray();
        foreach (var receipt in receipts)
        {
            var reserved = receipt.Reserved;
            Require(reserved.Namespace == "real:v1" && !string.IsNullOrWhiteSpace(reserved.Text)
                && !string.IsNullOrWhiteSpace(reserved.SpeakerName) && binding.IsSelf(reserved.SpeakerStableId ?? "")
                && reserved.InputRefs.All(id => inboundByRef.ContainsKey(id)
                    && Text(completed[id], "Type") == "completed"
                    && ShadowSessionBinding.Ref(binding.OpaqueRef + "\n" + Text(completed[id], "Execution")) == reserved.ExecutionRef),
                "receipt_not_bound_to_completed_input");
        }
        var positions = observed.Select((x, i) => (Id: Text(x, "InputRef"), Index: i)).ToDictionary(x => x.Id, x => x.Index);
        foreach (var entry in observed)
        {
            var reference = Text(entry, "InputRef");
            var input = inboundByRef[reference];
            envelopes.Add(ShadowInboundCanonicalizer.Normalize(input));
            foreach (var receipt in receipts.Where(r => r.Reserved.InputRefs.Max(id => positions[id]) == positions[reference]))
            {
                // Old journal has no send timestamp. This is an explicit lower-bound context
                // anchor, NOT a reconstructed provider timestamp. Migration manifest records it.
                var message = JsonSerializer.SerializeToElement(new { texto = receipt.Reserved.Text,
                    remetente = new { nome = receipt.Reserved.SpeakerName, stableId = receipt.Reserved.SpeakerStableId },
                    canal = new { nome = "DM", nativeId = binding.Session.NativeConversationId }, reply = (object?)null });
                envelopes.Add(input.Message with { ProviderMessageId = receipt.Sent.ProviderMessageId!,
                    Direction = SessionMessageDirection.Outbound, Message = message });
            }
        }
        var lastRunIndex = Array.FindLastIndex(inputs, x => Text(x, "Type") == "shadow-run:v1");
        Require(lastRunIndex >= 0 && Text(inputs[lastRunIndex], "Revision") == oldProject.Project.ProjectRevision, "last_run_revision");
        var runId = Text(inputs[lastRunIndex], "RunId");
        var state = ReadState(audit, runId, inputs.Skip(lastRunIndex).ToArray(), oldProject);
        var history = envelopes.Select((e, i) => new SessionHistoryEntry(i + 1, e.ProviderMessageId, e.Timestamp,
            e.Direction, e.EntryKind, e.ChannelId, e.AccountId, e.NativeConversationId, e.Message)).ToArray();
        var snapshot = new SessionSnapshot(binding.Session, project.Project.ProjectId, project.Project.ProjectVersion,
            project.Project.ProjectRevision, state.State, state.Version, DateTimeOffset.MinValue,
            history.Length, history.Length, 0, 0);
        var context = new SessionRuntimeContext(snapshot, history, history.Select(x => x.ProviderMessageId).ToArray());
        // The real hydration validator proves domain/session/state constraints without receiving
        // a message or calling a model. No outbound host exists in this runtime.
        var runtime = new SessionFlowRuntime(TimeProvider.System, new NoCompletion());
        try { runtime.UpdateProject(project.Project); runtime.RestoreContext(binding.Session, context, binding.Kind); }
        finally { runtime.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        Require(Hash(Path.Combine(source, Inbox)) == inboxSha256 && Hash(Path.Combine(source, Outbox)) == outboxSha256
            && Hash(auditFile) == auditSha256 && Hash(Path.Combine(target, Inbox)) == inboxSha256
            && Hash(Path.Combine(target, Outbox)) == outboxSha256, "source_changed_or_nonterminal_journal");
        File.WriteAllText(Path.Combine(target, "context-import-audit.json"), JsonSerializer.Serialize(new {
            version = "legacy-context-import:v1", inboxSha256, outboxSha256, auditSha256,
            sourceRevision = oldProject.Project.ProjectRevision, targetRevision = project.Project.ProjectRevision,
            runId, historyCount = history.Length, state = state.State,
            legacySendTimestamp = "associated_inbound_lower_bound_not_provider_time",
            legacyStateEnteredAt = "unknown_min_value_not_reconstructed", executions = 0, sends = 0 }));
        new LiveSessionContextStore(target, binding, project).Save(context);
        return snapshot;
    }

    private static (string State, long Version) ReadState(JsonElement[] audit, string runId, JsonElement[] inputs, PinnedShadowProject pin)
    {
        Require(audit.Any(x => Text(x, "phase") == "transport_preflight" && Text(x, "status") == "PASS"
            && Text(x, "projectId") == pin.Project.ProjectId && Text(x, "revision") == pin.Project.ProjectRevision)
            && audit.Any(x => Text(x, "phase") == "run_started" && Text(x, "runId") == runId), "audit_pin_missing");
        var expected = inputs.Where(x => Text(x, "Type") is "completed" or "execution_failed")
            .Select(x => Text(x, "Execution")).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var state = Text(pin.Project.Document, "initialStateId");
        long version = 0;
        foreach (var interaction in audit.Where(x => Text(x, "phase") == "interaction" && Text(x, "runId") == runId))
        {
            var outcomes = interaction.GetProperty("outcomes").EnumerateArray().ToArray();
            // Native polling records healthy empty batches. They contain no execution,
            // admission or transition and must not be mistaken for a legacy batch.
            if (outcomes.Length == 0)
            {
                Require(interaction.GetProperty("trace").GetArrayLength() == 0
                    && interaction.GetProperty("decisions").GetArrayLength() == 0, "empty_poll_has_work");
                continue;
            }
            Require(outcomes.Length == 1, "unsupported_legacy_batch");
            var outcome = outcomes[0];
            Require(expected.Contains(Text(outcome, "executionId")) && seen.Add(Text(outcome, "executionId"))
                && outcome.GetProperty("success").GetBoolean() && Text(outcome, "stateId") == state, "audit_execution_mismatch");
            var trace = interaction.GetProperty("trace").EnumerateArray().ToArray();
            if (outcome.GetProperty("transitionApplied").GetBoolean())
            {
                var graph = pin.Project.Document.GetProperty("states").EnumerateArray().Single(x => Text(x, "id") == state).GetProperty("graph");
                var exits = graph.GetProperty("nodes").EnumerateArray().Where(n => Text(n, "kind") == "exit"
                    && trace.Any(t => Text(t, "nodeId") == Text(n, "id") && Text(t, "eventName") == "node-completed")).ToArray();
                Require(exits.Length == 1 && Text(exits[0].GetProperty("settings"), "mode") == "switch", "transition_not_proven");
                var target = Text(exits[0].GetProperty("settings"), "targetStateId");
                var applied = trace.Where(t => Text(t, "eventName") == "transition-applied").ToArray();
                Require(applied.Length == 1 && Text(applied[0], "currentSessionStateId") == target, "transition_target_mismatch");
                state = target; version++;
            }
            Require(trace.Length > 0 && Text(trace[^1], "currentSessionStateId") == state, "audit_final_state_mismatch");
        }
        Require(seen.SetEquals(expected) && seen.Count > 0, "audit_incomplete");
        return (state, version);
    }

    private static void ValidateProjectChange(PinnedShadowProject oldPin, PinnedShadowProject pin)
    {
        Require(oldPin.Project.ProjectId == pin.Project.ProjectId && oldPin.Project.ProjectVersion == pin.Project.ProjectVersion, "project_identity_changed");
        // Explicit operator-approved punctuation-only artifact pair; not permission
        // to migrate arbitrary prompt/FSM revisions or to hot-reload a running run.
        if (oldPin.Project.ProjectRevision == "1B1E2DC571D50F77ACE0AC1E6B060BB6749988996E6324BFFF24FD6C4756AFD5"
            && pin.Project.ProjectRevision == "E01D0012A223EABAACEE5D741EFBC19098E5185790543E1594481AFB4832A792") return;
        // Approved conversational-care posture; exact artifacts, not arbitrary prompt changes.
        if (oldPin.Project.ProjectRevision == "E01D0012A223EABAACEE5D741EFBC19098E5185790543E1594481AFB4832A792"
            && pin.Project.ProjectRevision == "E6542C60C32F2C8FE04DC5FB9928794FB9B2CA8AC375BE861D790B40E03B0BE1") return;
        // Approved care-tone follow-up, offline only; both immutable artifacts pinned.
        if (oldPin.Project.ProjectRevision == "E6542C60C32F2C8FE04DC5FB9928794FB9B2CA8AC375BE861D790B40E03B0BE1"
            && pin.Project.ProjectRevision == "BDE240996D7739FF7CB025DDDF9998928F22D06CE7238DA50DC9FD58E2C25686") return;
        // Approved bilateral-DM situation correction; no arbitrary revision transfer.
        if (oldPin.Project.ProjectRevision == "BDE240996D7739FF7CB025DDDF9998928F22D06CE7238DA50DC9FD58E2C25686"
            && pin.Project.ProjectRevision == "62368EC88ED3B7690066BB56BE61A0E59C08348128ACDD3A4EC7DECD170D32A5") return;
        // Approved Luna-only upgrade; preserve history without replay or hot reload.
        if (oldPin.Project.ProjectRevision == "62368EC88ED3B7690066BB56BE61A0E59C08348128ACDD3A4EC7DECD170D32A5"
            && pin.Project.ProjectRevision == "7789735AC766B4FA38A8C1749679E1352BBA8B8F9760D3FA9189F076F89BDB39") return;
        var oldDoc = JsonNode.Parse(oldPin.Project.Document.GetRawText())!;
        var newDoc = JsonNode.Parse(pin.Project.Document.GetRawText())!;
        // Context-only group repair: exact artifacts plus structural equality after
        // restoring three situation prompts. No model, FSM, DM or delivery changes.
        if (oldPin.Project.ProjectRevision == "CE356E1335EAC1C149F1A2DAA7A756C94777F30439E05386E5A4BB0ABA03FE6C"
            && pin.Project.ProjectRevision == "77ECDAABBF48A957516AD18371C32377495843D499F747425AC47A2C414F8FA8")
        {
            var changed = 0;
            foreach (var state in newDoc["states"]!.AsArray())
                foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                    if (node!["id"]!.GetValue<string>() == "group-situation")
                    {
                        var original = oldDoc["states"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == state["id"]!.GetValue<string>())!
                            ["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "group-situation")!;
                        node["prompt"] = original["prompt"]!.DeepClone();
                        changed++;
                    }
            Require(changed == 3 && JsonNode.DeepEquals(oldDoc, newDoc), "incompatible_group_situation_migration");
        }
        // Explicitly approved group conversation repair; no generic prompt migration.
        // Retain every state, model, authority, delivery setting, DM prompt and edge.
        if (oldPin.Project.ProjectRevision == "FE18A25EB48388EE65704A9D3FA9E234DB944D3A187DA6733AC703B2594DC0ED"
            && pin.Project.ProjectRevision == "CE356E1335EAC1C149F1A2DAA7A756C94777F30439E05386E5A4BB0ABA03FE6C")
        {
            var changed = 0;
            foreach (var state in newDoc["states"]!.AsArray())
                foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                    if (node!["id"]!.GetValue<string>() is "group-situation" or "group-presence" or "group-generation" or "group-regeneration")
                    {
                        var original = oldDoc["states"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == state["id"]!.GetValue<string>())!
                            ["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == node["id"]!.GetValue<string>())!;
                        Require(node["prompt"]!.GetValue<string>().StartsWith(original["prompt"]!.GetValue<string>() + "\n\n", StringComparison.Ordinal),
                            "group_repair_prompt_replaced");
                        node["prompt"] = original["prompt"]!.DeepClone();
                        changed++;
                    }
            Require(changed == 12 && JsonNode.DeepEquals(oldDoc, newDoc), "incompatible_group_repair_migration");
        }
        // Approved voice-only revision. Preserve every graph edge, schema, model,
        // role and state; only six appended generation instructions may differ.
        if (oldPin.Project.ProjectRevision == "6F35BE1195D1CB9FC3B31FDBEA662D910BC922818FB209A9E029D69BAD40FC87"
            && pin.Project.ProjectRevision == "FE18A25EB48388EE65704A9D3FA9E234DB944D3A187DA6733AC703B2594DC0ED")
        {
            var changed = 0;
            foreach (var state in newDoc["states"]!.AsArray())
                foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                    if (node!["id"]!.GetValue<string>() is "group-generation" or "group-regeneration")
                    {
                        var original = oldDoc["states"]!.AsArray().Single(s =>
                            s!["id"]!.GetValue<string>() == state["id"]!.GetValue<string>())!["graph"]!["nodes"]!
                            .AsArray().Single(n => n!["id"]!.GetValue<string>() == node["id"]!.GetValue<string>())!;
                        Require(node["prompt"]!.GetValue<string>().StartsWith(
                            original["prompt"]!.GetValue<string>() + "\n\nCONVERSA NATURAL\n", StringComparison.Ordinal),
                            "natural_prompt_replaced");
                        node["prompt"] = original["prompt"]!.DeepClone();
                        changed++;
                    }
            Require(changed == 6 && JsonNode.DeepEquals(oldDoc, newDoc), "incompatible_natural_migration");
        }
        // Operator-approved pair, still requiring structural equality after removing
        // only optional reply outputs/guidance. Never bypass state/graph validation.
        if (oldPin.Project.ProjectRevision == "C10691673E0A1BF1064EBE2464A246F0D2D220552112C1E1523E3DD12E302F97"
            && pin.Project.ProjectRevision == "6F35BE1195D1CB9FC3B31FDBEA662D910BC922818FB209A9E029D69BAD40FC87")
        {
            var changed = 0;
            foreach (var state in newDoc["states"]!.AsArray())
                foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                    if (node!["id"]!.GetValue<string>() is "group-generation" or "group-regeneration")
                    {
                        var original = oldDoc["states"]!.AsArray().Single(s =>
                            s!["id"]!.GetValue<string>() == state["id"]!.GetValue<string>())!["graph"]!["nodes"]!
                            .AsArray().Single(n => n!["id"]!.GetValue<string>() == node["id"]!.GetValue<string>())!;
                        Require(node["prompt"]!.GetValue<string>().StartsWith(original["prompt"]!.GetValue<string>(),
                            StringComparison.Ordinal), "reply_prompt_replaced");
                        var fields = node["responseFields"]!.AsArray();
                        Require(fields.Count == 2 && fields[1]!["id"]!.GetValue<string>() == "replyTo"
                            && fields[1]!["type"]!.GetValue<string>() == "text?", "invalid_reply_output");
                        fields.RemoveAt(1);
                        node["outputMode"] = "text";
                        node["prompt"] = original["prompt"]!.DeepClone();
                        changed++;
                    }
            Require(changed == 6 && JsonNode.DeepEquals(oldDoc, newDoc), "incompatible_reply_migration");
        }
        if (!JsonNode.DeepEquals(oldDoc, newDoc))
        {
            foreach (var state in newDoc["states"]!.AsArray())
                foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                    if (node!["kind"]!.GetValue<string>() == "history" && node["id"]!.GetValue<string>().StartsWith("dm-", StringComparison.Ordinal)
                        && node["settings"]!["maxAgeMinutes"] is null)
                        node["settings"]!["maxAgeMinutes"] = 15;
            Require(JsonNode.DeepEquals(oldDoc, newDoc), "unapproved_project_change");
        }
    }
    private static JsonElement[] Read(string path)
    {
        var text = File.ReadAllText(path);
        Require(text.EndsWith('\n'), "incomplete_file");
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => JsonSerializer.Deserialize<JsonElement>(x)).ToArray();
    }
    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString()! : "";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException("context_import_" + reason); }
    private sealed class NoCompletion : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => throw new InvalidOperationException("Import cannot execute.");
    }
}
