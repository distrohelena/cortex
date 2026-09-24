using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Outbound;
using Xunit;

namespace Filomena.Core.Tests.Outbound;

public sealed class ShadowOutboundTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "outbound-test-" + Guid.NewGuid().ToString("N"));
    private static readonly SessionKey Session = new("runtime", "agent", "whatsapp", "account", "dm-exact");
    private static readonly JsonElement Project = JsonSerializer.SerializeToElement(new { version = 1 });
    private static readonly OutboundBindings Bindings = new(["recall"], ["compile"], new Dictionary<string, string> { ["generate"] = "text", ["regenerate"] = "text" }, ["enforce"], "engaged");
    public ShadowOutboundTests() => Directory.CreateDirectory(directory);
    private ShadowOutboundHost Host() => new(Path.Combine(directory, "journal.jsonl"), Project, Bindings, [Session], TransportSafetyCapabilities.Unproven);
    private static TypedValue Text() => new(new StringType(), JsonSerializer.SerializeToElement("candidate"));
    private static OutboundExecution Begin(ShadowOutboundHost host, string execution = "execution", string input = "input", SessionKey? session = null, SessionEntryKind kind = SessionEntryKind.Dm) =>
        host.Begin(Project, execution, session ?? Session, kind, [input]);
    private static TypedValue Approve(OutboundExecution execution, bool empty = false)
    {
        var recall = new TypedValue(new ArrayType(FlowDomain.Memory), JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var boundaries = new TypedValue(new ArrayType(FlowDomain.Boundary), JsonSerializer.SerializeToElement(empty ? Array.Empty<object>() : new object[] { new { trusted = true } }));
        execution.Recall("recall", recall);
        execution.Compiled("compile", recall, boundaries);
        var text = Text();
        execution.Generated("generate", new Dictionary<string, TypedValue> { ["text"] = text });
        execution.Enforced("enforce", text, boundaries, true, "revision");
        return text;
    }

    [Fact]
    public void DefaultDisabledAndKillSwitchAtSink()
    {
        using var host = Host();
        Assert.Equal(OutboundMode.Disabled, host.Mode);
        var execution = Begin(host);
        var text = Approve(execution);
        host.Rearm(OutboundMode.Shadow);
        host.Rearm(OutboundMode.Disabled);
        Assert.Equal("disabled", execution.Send(text).Status);
    }

    [Fact]
    public void ShadowIsDurableAndDoesNotPromoteAfterRestart()
    {
        using (var host = Host())
        {
            host.Rearm(OutboundMode.Shadow);
            var execution = Begin(host);
            var result = execution.Send(Approve(execution));
            Assert.Equal("shadowed", result.Status);
            Assert.Equal("engaged", result.WouldTransitionTo);
            Assert.Equal("duplicate_attempt", execution.Send(Text()).DiagnosticCode);
        }
        using var restarted = Host();
        Assert.Equal(OutboundMode.Disabled, restarted.Mode);
        restarted.Rearm(OutboundMode.Shadow);
        var replay = Begin(restarted, "new-execution-same-input");
        Assert.Equal("replayed_input", replay.Send(Approve(replay)).DiagnosticCode);
        restarted.Dispose();
        Assert.DoesNotContain("candidate", File.ReadAllText(Path.Combine(directory, "journal.jsonl")));
        Assert.DoesNotContain("dm-exact", File.ReadAllText(Path.Combine(directory, "journal.jsonl")));
    }

    [Fact]
    public void EnabledWithIncompleteCapabilitiesIsBlocked()
    {
        using var host = Host();
        host.Rearm(OutboundMode.Enabled);
        Assert.Equal(OutboundMode.Disabled, host.EffectiveMode);
        var execution = Begin(host);
        Assert.Equal("transport_capabilities_incomplete", execution.Send(Approve(execution)).DiagnosticCode);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("reconstructed")]
    [InlineData("modified")]
    [InlineData("fake_allow")]
    [InlineData("old_generation")]
    [InlineData("other_execution")]
    public void RejectsForgedAndStaleProvenance(string attack)
    {
        using var host = Host();
        host.Rearm(OutboundMode.Shadow);
        var execution = Begin(host);
        var original = Approve(execution);
        var attempted = attack switch
        {
            "copy" => original with { },
            "modified" => original with { Value = JsonSerializer.SerializeToElement("changed") },
            "old_generation" or "other_execution" => original,
            _ => Text()
        };
        if (attack == "old_generation") execution.Generated("regenerate", new Dictionary<string, TypedValue> { ["text"] = Text() });
        if (attack == "other_execution") execution = Begin(host, "other", "other-input");
        Assert.Equal("invalid_provenance", execution.Send(attempted).DiagnosticCode);
    }

    [Fact]
    public void EmptySetRequiresTrustedRecallAndCompilation()
    {
        using var host = Host();
        host.Rearm(OutboundMode.Shadow);
        var legitimate = Begin(host);
        Assert.Equal("shadowed", legitimate.Send(Approve(legitimate, empty: true)).Status);
        var outage = Begin(host, "outage", "outage-input");
        var text = Text();
        outage.Generated("generate", new Dictionary<string, TypedValue> { ["text"] = text });
        outage.Enforced("enforce", text, new(new ArrayType(FlowDomain.Boundary), JsonSerializer.SerializeToElement(Array.Empty<object>())), true, "fake");
        Assert.Equal("invalid_provenance", outage.Send(text).DiagnosticCode);
    }

    [Fact]
    public void ExactSessionAndDmOnlyAndProjectPin()
    {
        using var host = Host();
        host.Rearm(OutboundMode.Shadow);
        var other = Begin(host, session: Session with { NativeConversationId = "other" });
        Assert.Equal("session_not_allowlisted", other.Send(Approve(other)).DiagnosticCode);
        var group = Begin(host, kind: SessionEntryKind.Group);
        Assert.Equal("dm_only", group.Send(Approve(group)).DiagnosticCode);
        var elevated = host.Begin(JsonSerializer.SerializeToElement(new { enabled = true }), "changed", Session, SessionEntryKind.Dm, ["changed"]);
        Assert.Equal("project_not_authorized", elevated.Send(Approve(elevated)).DiagnosticCode);
    }

    [Fact]
    public void ExactGroupCanRecordWouldSendOnlyInShadowMode()
    {
        var groupSession = Session with { NativeConversationId = "123456@g.us" };
        using var host = new ShadowOutboundHost(Path.Combine(directory, "group-shadow.jsonl"), Project, Bindings,
            [groupSession], TransportSafetyCapabilities.Unproven);
        host.Rearm(OutboundMode.Shadow);
        var group = Begin(host, "group-execution", "group-input", groupSession, SessionEntryKind.Group);
        Assert.Equal("shadowed", group.Send(Approve(group)).Status);

        host.Rearm(OutboundMode.Enabled);
        var enabled = Begin(host, "group-enabled", "new-group-input", groupSession, SessionEntryKind.Group);
        Assert.Equal("dm_only", enabled.Send(Approve(enabled)).DiagnosticCode);
    }

    [Fact]
    public void ChunkingAloneBlocksEnabledAndCompleteFakeStillCannotDispatch()
    {
        foreach (var singlePart in new[] { false, true })
        {
            using var host = new ShadowOutboundHost(Path.Combine(directory, singlePart + ".jsonl"), Project, Bindings, [Session], new(true, true, true, true, true, true, singlePart));
            host.Rearm(OutboundMode.Enabled);
            var execution = Begin(host);
            // Complete capabilities without an administrative live route binding still cannot dispatch anything.
            Assert.Equal(singlePart ? "route_not_bound" : "transport_capabilities_incomplete", execution.Send(Approve(execution)).DiagnosticCode);
        }
        Assert.DoesNotContain(typeof(ShadowOutboundHost).GetConstructors().SelectMany(c => c.GetParameters()), p => p.ParameterType.Name.Contains("Sender", StringComparison.Ordinal) || p.ParameterType.Name.Contains("TransportAdapter", StringComparison.Ordinal));
    }

    [Fact]
    public void CrossSessionReceiptCannotBeReusedEvenWhenBothSessionsAllowlisted()
    {
        var other = Session with { NativeConversationId = "second" };
        using var host = new ShadowOutboundHost(Path.Combine(directory, "journal.jsonl"), Project, Bindings, [Session, other], TransportSafetyCapabilities.Unproven);
        host.Rearm(OutboundMode.Shadow);
        var first = Begin(host);
        var text = Approve(first);
        var second = Begin(host, "second", "second", other);
        Approve(second);
        Assert.Equal("invalid_provenance", second.Send(text).DiagnosticCode);
    }

    [Fact]
    public void FakeCompilationAndFakeAllowDoNotAuthorize()
    {
        using var host = Host();
        host.Rearm(OutboundMode.Shadow);
        var execution = Begin(host);
        var fake = new TypedValue(new ArrayType(FlowDomain.Memory), JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var boundary = new TypedValue(new ArrayType(FlowDomain.Boundary), fake.Value);
        execution.Recall("arbitrary-instruction", fake);
        execution.Compiled("compile", fake, boundary);
        var text = Text();
        execution.Generated("generate", new Dictionary<string, TypedValue> { ["text"] = text });
        execution.Enforced("arbitrary-instruction", text, boundary, true, "fake_allow");
        Assert.Equal("invalid_provenance", execution.Send(text).DiagnosticCode);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("indeterminate")]
    public void UnresolvedRealAttemptBlocksNewOutboundAfterRestart(string status)
    {
        // Model a valid durable real history; Phase 0 never creates one.
        var sessionRef = ShadowOutboundHost.Hash(JsonSerializer.Serialize(Session));
        var entry = new OutboundJournalEntry(1, "real:v1", "pending", "old-attempt", "old-execution", sessionRef, ["old-input"], "old-revision");
        var history = JsonSerializer.Serialize(entry) + "\n";
        if (status == "indeterminate") history += JsonSerializer.Serialize(entry with { Status = status }) + "\n";
        File.WriteAllText(Path.Combine(directory, "journal.jsonl"), history);
        using var host = Host();
        host.Rearm(OutboundMode.Shadow);
        var execution = Begin(host, "new", "new-input");
        Assert.Equal("reconciliation_required", execution.Send(Approve(execution)).DiagnosticCode);
    }

    [Fact]
    public void IncompleteJournalFailsClosedAndCannotHaveTwoWriters()
    {
        using (var first = Host()) Assert.Throws<IOException>(() => Host());
        File.WriteAllText(Path.Combine(directory, "journal.jsonl"), "{\"partial\":");
        Assert.Throws<InvalidDataException>(() => Host());
    }

    [Fact]
    public void NewRecallOrRegenerationRevokesOldApproval()
    {
        using var host = Host();
        host.Rearm(OutboundMode.Shadow);
        var execution = Begin(host);
        var old = Approve(execution);
        execution.Recall("recall", new(new ArrayType(FlowDomain.Memory), JsonSerializer.SerializeToElement(Array.Empty<object>())));
        Assert.Equal("invalid_provenance", execution.Send(old).DiagnosticCode);
    }

    public void Dispose() => Directory.Delete(directory, true);
}
