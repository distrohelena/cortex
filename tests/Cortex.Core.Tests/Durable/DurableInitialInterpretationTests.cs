using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class DurableInitialInterpretationTests
{
    [Fact]
    public async Task InitialInterpretationUsesCapturedPrefixAndIsNotRepeatedAfterRestart()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var token = TestContext.Current.CancellationToken;
        var project = DurableDriverTests.Project(); var start = await DurableDriverTests.PrepareAsync(store, project);
        var batch = (await store.ReadAsync(start.BatchRef, token)).Deserialize<ExecutionBatchV1>()!;
        var now = DateTimeOffset.UtcNow;
        var original = new SessionHistoryEntry(1, "input", now, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
            start.Scope.Session.ChannelId, start.Scope.Session.AccountId, start.Scope.Session.NativeConversationId, batch.Message);
        var context = new ParticipationReassessmentContext(new(start.Scope.Session, project.ProjectId, 1, project.ProjectRevision, "initial", 0, now, 1, 1, 0, 1), original, [original], false);
        var reference = await store.PutOnceAsync(start.Scope, "interpreted-batch", JsonSerializer.SerializeToElement(batch with { InitialContexts = [context] }), token);
        var calls = 0;
        var model = new IncrementalKernelTests.Model();
        var bindings = new DurableDriverTests.Bindings(project, model) { InitialInterpretation = (captured, _) =>
        {
            calls++; Assert.Single(captured.History); Assert.Equal(1, captured.Snapshot.AdmissionWatermark);
            return Task.FromResult(new ParticipationReassessment(false, ReassessmentOpportunity.Uncertain, ["input"]));
        } };
        var driver = new FlowExecutionDriver(store, bindings);
        var descriptor = await driver.InitializeAsync(start with { BatchRef = reference }, token);
        Assert.Equal(StepEffect.Inference, descriptor.Effect);
        var command = DurableDriverTests.Command(descriptor);
        descriptor = await driver.AdvanceAsync(command, token);
        driver = new(store, bindings);
        Assert.Equal(descriptor, await driver.AdvanceAsync(command, token));
        while (descriptor.Status == ExecutionStatus.Ready) descriptor = await driver.AdvanceAsync(DurableDriverTests.Command(descriptor), token);
        Assert.Equal(1, calls);
        Assert.Equal(2, model.Requests.Count);
        Assert.True(model.Requests[0].Inputs.TryGetProperty("interpretacoes_contextuais", out var interpretations));
        Assert.True(interpretations.TryGetProperty("input", out _));
    }
}
