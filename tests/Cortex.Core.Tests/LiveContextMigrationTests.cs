using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using Cortex.Core.Tests.Outbound;
using Xunit;

namespace Cortex.Core.Tests;

public sealed class LiveContextMigrationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("context-migration-").FullName;
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "default", "5511944558218@s.whatsapp.net");
    private static readonly ShadowSessionBinding Binding = new(Session, FilomenaIdentity.WhatsAppConversationNamespace(Session.NativeConversationId),
        ["self"], ["whatsapp:" + Session.NativeConversationId]);
    private static readonly PinnedShadowProject Pin = new(ShadowRuntimeTests.Project(false, sendExit: "sent", history: true));
    private static readonly OutboundBindings Roles = new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text" }, ["enforce"], "engaged");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("same")]
    [InlineData("reply")]
    [InlineData("natural")]
    [InlineData("conversation-repair")]
    [InlineData("situation-repair")]
    public void Group_checkpoint_transfer_preserves_group_history_without_execution(string variant)
    {
        var oldPin = Pin;
        var newPin = Pin;
        if (variant != "same")
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Filomena.slnx"))) repo = repo.Parent;
            Assert.NotNull(repo);
            PinnedShadowProject Read(string name)
            {
                var bytes = File.ReadAllBytes(Path.Combine(repo.FullName, "docs", name));
                return PinnedShadowProject.FromBytes(bytes, "manual-shadow-direct-rule-v3", 3,
                    Convert.ToHexString(SHA256.HashData(bytes)));
            }
            oldPin = Read(variant == "situation-repair" ? "manual-group-live-project-v3-conversation-repair-v1.json" : variant == "conversation-repair" ? "manual-group-live-project-v3-natural-v1.json" : variant == "natural" ? "manual-group-live-project-v3-optional-reply-v1.json" : "manual-group-live-project-v3-punctuation-v2.json");
            newPin = Read(variant == "situation-repair" ? "../evaluation/conversation-quality-v1/group-situation-grounded-v2.project.json" : variant == "conversation-repair" ? "manual-group-live-project-v3-conversation-repair-v1.json" : variant == "natural" ? "manual-group-live-project-v3-natural-v1.json" : "manual-group-live-project-v3-optional-reply-v1.json");
        }
        var session = Session with { NativeConversationId = "120363000001@g.us" };
        var binding = new ShadowSessionBinding(session, "group-test", ["self"], ["peer"], SessionEntryKind.Group);
        var source = Directory.CreateDirectory(Path.Combine(root, "group-source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(root, "group-target")).FullName;
        File.WriteAllText(Path.Combine(source, "observation-live-outbound.jsonl"), "");
        using (ShadowInboundJournal.StartNonResumableRun(Path.Combine(source, "observation-live-inbound.jsonl"),
            binding, oldPin.Project.ProjectRevision, "epoch", 0)) { }
        var message = JsonSerializer.SerializeToElement(new { texto = "Oi", remetente = new { nome = "Peer", stableId = "peer" },
            canal = new { nome = "Grupo", nativeId = session.NativeConversationId }, reply = (object?)null });
        var snapshot = new SessionSnapshot(session, oldPin.Project.ProjectId, oldPin.Project.ProjectVersion,
            oldPin.Project.ProjectRevision, oldPin.Project.Document.GetProperty("initialStateId").GetString()!,
            0, DateTimeOffset.UtcNow, 1, 1, 0, 0);
        new LiveSessionContextStore(source, binding, oldPin).Save(new(snapshot,
            [new(1, "group-message", DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Group,
                session.ChannelId, session.AccountId, session.NativeConversationId, message)], ["group-message"]));
        LiveContextMigration.TransferCheckpoint(source, target, binding, oldPin, newPin,
            Hash(Path.Combine(source, "observation-live-inbound.jsonl")),
            Hash(Path.Combine(source, "observation-live-outbound.jsonl")), Hash(Path.Combine(source, "observation-live-context.json")));
        var restored = new LiveSessionContextStore(target, binding, newPin).Load()!;
        Assert.Equal(SessionEntryKind.Group, Assert.Single(restored.History).EntryKind);
        Assert.Equal("group-message", Assert.Single(restored.SeenProviderIds));
        Assert.Equal(snapshot with { ProjectRevision = newPin.Project.ProjectRevision }, restored.Snapshot);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("valid_care")]
    [InlineData("valid_care_v3")]
    [InlineData("valid_dm_continuity")]
    [InlineData("valid_gpt6_luna")]
    [InlineData("wrong_hash")]
    [InlineData("wrong_session")]
    [InlineData("wrong_project")]
    [InlineData("corrupt_checkpoint")]
    public void Checkpoint_revision_transfer_preserves_exact_context_without_replaying(string scenario)
    {
        var care = scenario == "valid_care";
        var careV3 = scenario == "valid_care_v3";
        var dmContinuity = scenario == "valid_dm_continuity";
        var gpt6Luna = scenario == "valid_gpt6_luna";
        if (care || careV3 || dmContinuity || gpt6Luna) scenario = "valid";
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Filomena.slnx"))) repo = repo.Parent;
        Assert.NotNull(repo);
        PinnedShadowProject ReadPin(string name)
        {
            var bytes = File.ReadAllBytes(Path.Combine(repo.FullName, "docs", name));
            return PinnedShadowProject.FromBytes(bytes, "filomena-v3-live-sent", 3, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        var oldPin = ReadPin(gpt6Luna ? "manual-live-project-v3-dm-continuity.json" : dmContinuity ? "manual-live-project-v3-care-v3.json" : careV3 ? "manual-live-project-v3-care.json" : care ? "manual-live-project-v3-conversational.json" : "manual-live-project-v3-continuity.json");
        var newPin = ReadPin(gpt6Luna ? "manual-live-project-v3-gpt6-luna.json" : dmContinuity ? "manual-live-project-v3-dm-continuity.json" : careV3 ? "manual-live-project-v3-care-v3.json" : care ? "manual-live-project-v3-care.json" : "manual-live-project-v3-conversational.json");
        var source = Directory.CreateDirectory(Path.Combine(root, "checkpoint-source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(root, "checkpoint-target")).FullName;
        File.WriteAllText(Path.Combine(source, "observation-live-outbound.jsonl"), "");
        using (ShadowInboundJournal.StartNonResumableRun(Path.Combine(source, "observation-live-inbound.jsonl"), Binding,
            oldPin.Project.ProjectRevision, "old-epoch", 0)) { }
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        var message = JsonSerializer.SerializeToElement(new { texto = "O prazo mudou.", remetente = new { nome = "Helena", stableId = "whatsapp:" + Session.NativeConversationId },
            canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null });
        var snapshot = new SessionSnapshot(Session, oldPin.Project.ProjectId, 3, oldPin.Project.ProjectRevision,
            "participation-engaged-v1", 4, at, 1, 1, 0, 0);
        var context = new SessionRuntimeContext(snapshot,
            [new(1, "confirmed-inbound", at, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
                Session.ChannelId, Session.AccountId, Session.NativeConversationId, message)], ["confirmed-inbound"]);
        new LiveSessionContextStore(source, Binding, oldPin).Save(context);
        var checkpoint = Path.Combine(source, "observation-live-context.json");
        if (scenario == "corrupt_checkpoint") File.WriteAllText(checkpoint, "{}");
        var inbox = Hash(Path.Combine(source, "observation-live-inbound.jsonl"));
        var outbox = Hash(Path.Combine(source, "observation-live-outbound.jsonl"));
        var saved = Hash(checkpoint);
        var binding = scenario == "wrong_session" ? new ShadowSessionBinding(Session with { AccountId = "other" },
            Binding.ConversationNamespace, ["self"], ["whatsapp:" + Session.NativeConversationId]) : Binding;
        void Transfer() => LiveContextMigration.TransferCheckpoint(source, target, binding, oldPin,
            scenario == "wrong_project" ? Pin : newPin, inbox, outbox, scenario == "wrong_hash" ? "BAD" : saved);
        if (scenario != "valid")
        {
            Assert.ThrowsAny<Exception>(Transfer);
            Assert.False(File.Exists(Path.Combine(target, "observation-live-context.json")));
        }
        else
        {
            Transfer();
            var restored = new LiveSessionContextStore(target, Binding, newPin).Load()!;
            Assert.Equal(newPin.Project.ProjectRevision, restored.Snapshot.ProjectRevision);
            Assert.Equal(snapshot with { ProjectRevision = newPin.Project.ProjectRevision }, restored.Snapshot);
            Assert.Equal(context.SeenProviderIds, restored.SeenProviderIds);
            Assert.Equal(at, Assert.Single(restored.History).Timestamp);
            Assert.Equal(message.GetRawText(), restored.History[0].Message.GetRawText());
            Assert.Equal(inbox, Hash(Path.Combine(target, "observation-live-inbound.jsonl")));
            Assert.Equal(outbox, Hash(Path.Combine(target, "observation-live-outbound.jsonl")));
        }
        Assert.Equal(saved, Hash(checkpoint));
    }

    private ObservedLiveRun Open(string path, LiveRuntimeTests.SequencedTransport sender) => new(Pin, Binding, path, Roles,
        new ShadowRuntimeTests.Completion("empty"), new ShadowRuntimeTests.Reader("empty"), "epoch", 10,
        LiveRouteBindings.FromShadow(Binding, "route", Session.NativeConversationId, "Filomena", 1000), sender,
        new(true, true, true, true, true, true, true), _ => Task.CompletedTask);

    [Theory]
    [InlineData("valid")]
    [InlineData("forged_transition")]
    [InlineData("missing_audit")]
    [InlineData("wrong_execution")]
    [InlineData("wrong_session")]
    [InlineData("wrong_hash")]
    [InlineData("wrong_project")]
    [InlineData("uncommitted_send")]
    [InlineData("valid_empty_poll")]
    [InlineData("invalid_empty_poll")]
    public async Task Legacy_import_requires_audit_state_and_confirmed_journal_receipt(string scenario)
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
        string runId;
        ObservedShadowInteraction result;
        await using (var run = Open(source, new() { ProviderPrefix = "old-" }))
        {
            runId = run.RunId;
            var message = JsonSerializer.SerializeToElement(new { texto = "Guarda o contexto desta conversa.",
                remetente = new { nome = "Helena" }, canal = new { nome = "DM", nativeId = Session.NativeConversationId }, reply = (object?)null });
            result = await run.AcceptAsync([new("epoch", 11, Session, SessionEntryKind.Dm, Binding.ConversationNamespace,
                "whatsapp:" + Session.NativeConversationId, new("old-input", "whatsapp", "default", Session.NativeConversationId,
                    DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm, message))], Token);
        }
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var audit = Path.Combine(source, "audit.jsonl");
        var lines = new object[] {
            new { phase = "transport_preflight", status = "PASS", projectId = Pin.Project.ProjectId, revision = Pin.Project.ProjectRevision },
            new { phase = "run_started", runId },
            new { phase = scenario == "missing_audit" ? "missing" : "interaction", runId,
                outcomes = result.Outcomes.Select(o => new { executionId = scenario == "wrong_execution" ? "forged" : o.ExecutionId, o.StateId, o.Success, o.TransitionApplied }),
                trace = result.Trace.Select(t => new { t.EventName, t.NodeId, t.Exit, t.StateId,
                    currentSessionStateId = scenario == "forged_transition" ? "initial" : t.CurrentSessionStateId }) },
            new { phase = "run_closed", runId }
        };
        if (scenario is "valid_empty_poll" or "invalid_empty_poll")
        {
            lines = [.. lines.Take(2), new { phase = "interaction", runId,
                outcomes = Array.Empty<object>(), trace = Array.Empty<object>(),
                decisions = scenario == "invalid_empty_poll" ? new object[] { new { status = "admitted" } } : [] },
                .. lines.Skip(2)];
            if (scenario == "valid_empty_poll") scenario = "valid";
        }
        await File.WriteAllLinesAsync(audit, lines.Select(x => JsonSerializer.Serialize(x, json)), Token);
        File.Delete(Path.Combine(source, "observation-live-context.json"));
        if (scenario == "uncommitted_send")
        {
            var outbox = Path.Combine(source, "observation-live-outbound.jsonl");
            var rows = await File.ReadAllLinesAsync(outbox, Token);
            await File.WriteAllLinesAsync(outbox, rows.SkipLast(1), Token);
        }
        var before = Hash(Path.Combine(source, "observation-live-outbound.jsonl"));
        if (scenario != "valid")
        {
            var binding = scenario == "wrong_session" ? new ShadowSessionBinding(Session with { AccountId = "another" },
                Binding.ConversationNamespace, ["self"], ["whatsapp:" + Session.NativeConversationId]) : Binding;
            var project = scenario == "wrong_project" ? new PinnedShadowProject(ShadowRuntimeTests.Project(false,
                sendExit: "sent", history: true, version: 2)) : Pin;
            Assert.Throws<InvalidDataException>(() => LiveContextMigration.Import(source, target, audit,
                binding, Pin, project, Hash(Path.Combine(source, "observation-live-inbound.jsonl")),
                scenario == "wrong_hash" ? "BAD" : before, Hash(audit)));
            Assert.False(File.Exists(Path.Combine(target, "observation-live-context.json")));
        }
        else
        {
            var imported = LiveContextMigration.Import(source, target, audit, Binding, Pin, Pin,
                Hash(Path.Combine(source, "observation-live-inbound.jsonl")), before, Hash(audit));
            Assert.Equal(2, imported.HistoryCount);
            Assert.Equal("engaged", imported.CurrentStateId);
            var sender = new LiveRuntimeTests.SequencedTransport();
            await using var restored = Open(target, sender);
            Assert.Equal(2, restored.History.Count);
            Assert.Equal("Vamos por partes.", restored.History[1].Message.GetProperty("texto").GetString());
            Assert.Empty(restored.Outcomes);
            Assert.Empty(sender.Sends);
        }
        Assert.Equal(before, Hash(Path.Combine(source, "observation-live-outbound.jsonl")));
    }

    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    public void Dispose() => Directory.Delete(root, true);
}
