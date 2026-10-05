using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class RecoveryFaultMatrixTests
{
    [Fact]
    public async Task RevokedHostAtInitializationIsDurableFailureWithoutInference()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = DurableDriverTests.Project(); var model = new IncrementalKernelTests.Model();
        var bindings = new DurableDriverTests.Bindings(project, model) { Revoked = true };
        var driver = new FlowExecutionDriver(store, bindings); var start = await DurableDriverTests.PrepareAsync(store, project);
        var failed = await driver.InitializeAsync(start, TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.Failed, failed.Status); Assert.Empty(model.Requests);
        bindings.Revoked = false;
        Assert.Equal(failed, await driver.InitializeAsync(start, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CrashAfterAtomicStepResultBeforeSnapshotDoesNotRegenerateModelOrTraces()
    {
        using var directory = new ArtifactDirectory();
        var project = DurableDriverTests.Project(); var model = new IncrementalKernelTests.Model();
        var bindings = new DurableDriverTests.Bindings(project, model); StepCommand command; ExecutionDescriptor completed;
        await using (var store = directory.Open())
        {
            var driver = new FlowExecutionDriver(store, bindings);
            var ready = await driver.InitializeAsync(await DurableDriverTests.PrepareAsync(store, project), TestContext.Current.CancellationToken);
            ready = await driver.AdvanceAsync(DurableDriverTests.Command(ready), TestContext.Current.CancellationToken);
            command = DurableDriverTests.Command(ready);
            completed = await driver.AdvanceAsync(command, TestContext.Current.CancellationToken);
        }
        var afterKey = "after-" + DurableIdentity.OperationId(command.Key);
        foreach (var file in Directory.GetFiles(directory.Path, "*.json"))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            if (document.RootElement.GetProperty("Reference").GetProperty("Key").GetString() == afterKey) File.Delete(file);
        }
        bindings.Revoked = true;
        await using var reopened = directory.Open();
        Assert.Equal(completed, await new FlowExecutionDriver(reopened, bindings).RecoverAsync(command.Key, TestContext.Current.CancellationToken));
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task ReadyCancellationIsPrivateTerminalCheckpointAfterReopen()
    {
        using var directory = new ArtifactDirectory(); var project = DurableDriverTests.Project();
        var bindings = new DurableDriverTests.Bindings(project, new IncrementalKernelTests.Model());
        ExecutionDescriptor cancelled;
        await using (var store = directory.Open())
        {
            var driver = new FlowExecutionDriver(store, bindings);
            var ready = await driver.InitializeAsync(await DurableDriverTests.PrepareAsync(store, project), TestContext.Current.CancellationToken);
            cancelled = await driver.CancelAsync(ready.SnapshotRef, TestContext.Current.CancellationToken);
            Assert.Equal(ExecutionStatus.Cancelled, cancelled.Status);
        }
        await using var reopened = directory.Open();
        var snapshot = ExecutionSnapshotCodec.Decode(await reopened.ReadAsync(cancelled.SnapshotRef, TestContext.Current.CancellationToken), cancelled.Scope);
        Assert.Equal(ExecutionStatus.Cancelled, snapshot.Status); Assert.Null(snapshot.Cursor);
    }

    [Fact]
    public async Task FeedbackIntentWithoutSnapshotCanRecoverWithoutInference()
    {
        using var directory = new ArtifactDirectory();
        await using var store = directory.Open();
        var project = DurableDriverTests.Project(JsonSerializer.SerializeToElement(HumanFeedbackNodeTests.Graph()));
        var model = new IncrementalKernelTests.Model();
        var driver = new FlowExecutionDriver(store, new DurableDriverTests.Bindings(project, model));
        var ready = await driver.InitializeAsync(await DurableDriverTests.PrepareAsync(store, project), TestContext.Current.CancellationToken);
        ready = await driver.AdvanceAsync(DurableDriverTests.Command(ready), TestContext.Current.CancellationToken);
        var command = DurableDriverTests.Command(ready);
        await store.PutOnceAsync(command.Key.Scope, "intent-" + DurableIdentity.OperationId(command.Key), JsonSerializer.SerializeToElement(command), TestContext.Current.CancellationToken);
        var recovered = await new FlowExecutionDriver(store, new DurableDriverTests.Bindings(project, model)).AdvanceAsync(command, TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.WaitingForHuman, recovered.Status); Assert.NotNull(recovered.Feedback); Assert.Empty(model.Requests);
        Assert.Equal(recovered, await driver.RecoverAsync(command.Key, TestContext.Current.CancellationToken));
    }
}
