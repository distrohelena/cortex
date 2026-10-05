using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class DurableReviewDriverTests
{
    [Fact]
    public async Task UncertainReassessmentStopsBeforeDecisionPromptAndDoesNotRepeatAfterRestart()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open(); var token = TestContext.Current.CancellationToken;
        var project = Project(); var model = new IncrementalKernelTests.ScriptedModel(); var calls = 0;
        var bindings = new DurableDriverTests.Bindings(project, model) { ReviewValidation = (_, _) => Task.CompletedTask,
            Reassessment = (_, _) => { calls++; return Task.FromResult(new ParticipationReassessment(false, ReassessmentOpportunity.Uncertain, ["input"])); } };
        var start = await DurableDriverTests.PrepareAsync(store, project);
        var batch = (await store.ReadAsync(start.BatchRef, token)).Deserialize<ExecutionBatchV1>()!; var now = DateTimeOffset.UtcNow;
        var original = new SessionHistoryEntry(1, "input", now, SessionMessageDirection.Inbound, SessionEntryKind.Dm, start.Scope.Session.ChannelId, start.Scope.Session.AccountId, start.Scope.Session.NativeConversationId, batch.Message);
        var snapshot = new SessionSnapshot(start.Scope.Session, project.ProjectId, 1, project.ProjectRevision, "initial", 0, now, 2, 1, 0, 0);
        batch = batch with { Purpose = SessionExecutionPurpose.ParticipationReview, Reassess = true, ReviewSnapshot = snapshot,
            ReviewDefinition = new(project.ProjectRevision, ["decision"], [new("branch", "true", ParticipationReviewDecision.Respond), new("branch", "false", ParticipationReviewDecision.Silence)]),
            InitialContexts = [new(snapshot, original, [original], false)] };
        var reference = await store.PutOnceAsync(start.Scope, "reassessment-batch", JsonSerializer.SerializeToElement(batch), token);
        var driver = new FlowExecutionDriver(store, bindings); var descriptor = await driver.InitializeAsync(start with { BatchRef = reference }, token);
        Assert.Equal(StepEffect.Inference, descriptor.Effect); var command = DurableDriverTests.Command(descriptor);
        descriptor = await driver.AdvanceAsync(command, token); Assert.Equal(ExecutionStatus.Completed, descriptor.Status);
        Assert.Equal(descriptor, await new FlowExecutionDriver(store, bindings).AdvanceAsync(command, token)); Assert.Equal(1, calls); Assert.Empty(model.Requests);
    }
    internal static SessionFlowProject Project() => DurableDriverTests.Project(ExecutionSnapshotCodecTests.Json("""
        {"version":3,"nodes":[{"id":"in","kind":"dm"},
        {"id":"decision","kind":"prompt","prompt":"Decide","inputs":[],"outputMode":"structured","responseFields":[{"id":"respond","name":"respond","type":"boolean"}]},
        {"id":"branch","kind":"branch","settings":{"operator":"eq","b":true}},
        {"id":"generate","kind":"prompt","prompt":"Write","inputs":[],"outputMode":"text","responseFields":[{"id":"text","name":"text","type":"text"}]}],
        "edges":[{"source":"decision","sourcePort":"respond","target":"branch","targetPort":"a"}],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"decision","targetPort":"exec"},{"source":"decision","sourcePort":"then","target":"branch","targetPort":"exec"},
        {"source":"branch","sourcePort":"true","target":"generate","targetPort":"exec"}]}
        """));

    [Fact]
    public async Task HostIssuedReviewArtifactReusesDecisionForShadowPreviewAfterDriverRestart()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open(); var project = Project();
        var scope = DurableIdentityTests.Scope; var model = new IncrementalKernelTests.ScriptedModel("{\"respond\":true}", "preview response");
        using var host = new Cortex.Core.Outbound.ShadowOutboundHost(Path.Combine(directory.Path, "outbound.jsonl"), project.Document,
            new([], [], new Dictionary<string, string>(), [], null), [scope.Session], Cortex.Core.Outbound.TransportSafetyCapabilities.Unproven);
        host.Rearm(Cortex.Core.Outbound.OutboundMode.Shadow);
        var bindings = new DurableDriverTests.Bindings(project, model) { ReviewValidation = (_, _) => Task.CompletedTask, Outbound = host };
        var start = await DurableDriverTests.PrepareAsync(store, project); var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), bindings);
        var publication = await artifacts.ReadPublicationAsync(start.ProjectRef, TestContext.Current.CancellationToken);
        var message = (await store.ReadAsync(start.BatchRef, TestContext.Current.CancellationToken)).Deserialize<ExecutionBatchV1>()!.Message;
        var now = DateTimeOffset.UtcNow;
        var current = new SessionSnapshot(scope.Session, project.ProjectId, project.ProjectVersion, project.ProjectRevision, "initial", 0, now, 1, 1, 0, 0);
        var history = new SessionHistoryEntry(1, "original", now, SessionMessageDirection.Inbound, SessionEntryKind.Dm, scope.Session.ChannelId, scope.Session.AccountId, scope.Session.NativeConversationId, message);
        var imported = new SessionImportRequest("import", scope.InstallationId, current, [history], ["original"], "epoch", 1, new string('0', 64));
        var importedRef = await store.PutOnceAsync(scope, "import", JsonSerializer.SerializeToElement(imported), TestContext.Current.CancellationToken);
        await artifacts.ImportIdleAsync(importedRef, publication, TestContext.Current.CancellationToken);
        var definition = new ParticipationReviewDefinition(project.ProjectRevision, ["decision"], [new("branch", "true", ParticipationReviewDecision.Respond), new("branch", "false", ParticipationReviewDecision.Silence)]);
        var request = new SessionReviewOperationV1("review", "original", SessionExecutionPurpose.ParticipationReview, definition, null);
        var requestRef = await store.PutOnceAsync(scope, "review-operation", JsonSerializer.SerializeToElement(request), TestContext.Current.CancellationToken);
        var prepared = await artifacts.PrepareReviewAsync(new(scope.InstallationId, current, publication.ProjectRef, requestRef, 0), TestContext.Current.CancellationToken);
        var driver = new FlowExecutionDriver(store, bindings);
        var descriptor = await driver.InitializeAsync(prepared.Start, TestContext.Current.CancellationToken);
        while (descriptor.Status == ExecutionStatus.Ready) descriptor = await driver.AdvanceAsync(DurableDriverTests.Command(descriptor), TestContext.Current.CancellationToken);
        var committed = await artifacts.CommitReviewAsync(new(prepared, descriptor, current, now), TestContext.Current.CancellationToken);
        var review = await artifacts.ReadParticipationReviewAsync(committed.ResultRef, TestContext.Current.CancellationToken);
        Assert.Equal(ParticipationReviewDecision.Respond, review.Decision); Assert.NotNull(review.ReviewRef);
        request = new("preview", "original", SessionExecutionPurpose.ResponsePreview, null, review.ReviewRef);
        requestRef = await store.PutOnceAsync(scope, "preview-operation", JsonSerializer.SerializeToElement(request), TestContext.Current.CancellationToken);
        prepared = await artifacts.PrepareReviewAsync(new(scope.InstallationId, current, publication.ProjectRef, requestRef, 0), TestContext.Current.CancellationToken);
        driver = new FlowExecutionDriver(store, bindings);
        descriptor = await driver.InitializeAsync(prepared.Start, TestContext.Current.CancellationToken);
        while (descriptor.Status == ExecutionStatus.Ready) descriptor = await driver.AdvanceAsync(DurableDriverTests.Command(descriptor), TestContext.Current.CancellationToken);
        committed = await artifacts.CommitReviewAsync(new(prepared, descriptor, current, now), TestContext.Current.CancellationToken);
        var preview = await artifacts.ReadResponsePreviewAsync(committed.ResultRef, TestContext.Current.CancellationToken);
        Assert.Equal(ParticipationReviewStatus.Completed, preview.Status); Assert.Equal(2, model.Requests.Count);
        Assert.Equal("preview response", preview.Outputs["generate"]["text"].GetString());
        Assert.DoesNotContain("autoriza_envio", model.Requests[1].Inputs.GetProperty("reavaliacao").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task ReviewRunsOnlyApprovedDecisionCutAndPersistsOriginalPromptResponse()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open(); var project = Project();
        var model = new IncrementalKernelTests.ScriptedModel("{\"respond\":true}"); var bindings = new DurableDriverTests.Bindings(project, model);
        bindings.ReviewValidation = (_, _) => Task.CompletedTask;
        var start = await DurableDriverTests.PrepareAsync(store, project);
        var batch = (await store.ReadAsync(start.BatchRef, TestContext.Current.CancellationToken)).Deserialize<ExecutionBatchV1>()!;
        var definition = new ParticipationReviewDefinition(project.ProjectRevision, ["decision"], [new("branch", "true", ParticipationReviewDecision.Respond), new("branch", "false", ParticipationReviewDecision.Silence)]);
        var snapshot = new SessionSnapshot(start.Scope.Session, project.ProjectId, project.ProjectVersion, project.ProjectRevision, "initial", 0, DateTimeOffset.UtcNow, 1, 1, 0, 0);
        batch = batch with { Purpose = SessionExecutionPurpose.ParticipationReview, ReviewSnapshot = snapshot, ReviewDefinition = definition, OriginalTimestamp = DateTimeOffset.UtcNow, ContextCapturedAt = snapshot.EnteredAt };
        var reference = await store.PutOnceAsync(start.Scope, "review-batch", JsonSerializer.SerializeToElement(batch), TestContext.Current.CancellationToken);
        var driver = new FlowExecutionDriver(store, bindings);
        var descriptor = await driver.InitializeAsync(start with { BatchRef = reference }, TestContext.Current.CancellationToken);
        while (descriptor.Status == ExecutionStatus.Ready) descriptor = await driver.AdvanceAsync(DurableDriverTests.Command(descriptor), TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.Completed, descriptor.Status);
        Assert.Equal("decision", Assert.Single(model.Requests).NodeId);
        var restored = ExecutionSnapshotCodec.Decode(await store.ReadAsync(descriptor.SnapshotRef, TestContext.Current.CancellationToken), start.Scope);
        Assert.Equal("{\"respond\":true}", restored.PromptResponses["decision"]);
        Assert.DoesNotContain("generate", restored.Outputs.Keys);
        Assert.False(model.Requests[0].Inputs.GetProperty("reavaliacao").GetProperty("autoriza_envio").GetBoolean());
    }
}
