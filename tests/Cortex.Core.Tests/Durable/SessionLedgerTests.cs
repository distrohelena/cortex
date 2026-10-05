using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class SessionLedgerTests
{
    private static LedgerAdmissionRequest Request(ArtifactRef input) => new("installation", DurableIdentityTests.Session,
        "epoch-provider-1", input, input.Sha256, "batch", "execution", 1);

    [Fact]
    public async Task LostAdmissionResponseReplaysOriginalReceipt()
    {
        using var directory = new ArtifactDirectory();
        LedgerAdmission first;
        LedgerAdmissionRequest request;
        await using (var store = directory.Open())
        {
            var input = await store.PutOnceAsync(DurableIdentityTests.Scope, "input", ExecutionSnapshotCodecTests.Json("1"), TestContext.Current.CancellationToken);
            request = Request(input);
            first = await new DurableSessionLedger(store).AdmitAsync(request, TestContext.Current.CancellationToken);
        }
        await using var reopened = directory.Open();
        var ledger = new DurableSessionLedger(reopened);
        Assert.Equal(first, await ledger.AdmitAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(first, await ledger.FindAdmissionAsync("installation", DurableIdentityTests.Session, request.InputKey, TestContext.Current.CancellationToken));
        Assert.Equal(1, first.Watermark);
        Assert.Equal("batch", first.BatchId);
    }

    [Fact]
    public async Task ConflictingInputIdFails()
    {
        using var directory = new ArtifactDirectory();
        await using var store = directory.Open();
        var ledger = new DurableSessionLedger(store);
        var input = await store.PutOnceAsync(DurableIdentityTests.Scope, "input", ExecutionSnapshotCodecTests.Json("1"), TestContext.Current.CancellationToken);
        var other = await store.PutOnceAsync(DurableIdentityTests.Scope, "other", ExecutionSnapshotCodecTests.Json("2"), TestContext.Current.CancellationToken);
        await ledger.AdmitAsync(Request(input), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await ledger.AdmitAsync(Request(other), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AdmissionsArePageableAfterReopen()
    {
        using var directory = new ArtifactDirectory();
        await using (var store = directory.Open())
        {
            var input = await store.PutOnceAsync(DurableIdentityTests.Scope, "input", ExecutionSnapshotCodecTests.Json("1"), TestContext.Current.CancellationToken);
            await new DurableSessionLedger(store).AdmitAsync(Request(input), TestContext.Current.CancellationToken);
        }
        await using var reopened = directory.Open();
        var page = await new DurableSessionLedger(reopened).ReadPageAsync("installation", DurableIdentityTests.Session, "admissions", new(), TestContext.Current.CancellationToken);
        var reference = Assert.Single(page.Items);
        Assert.Equal("epoch-provider-1", (await reopened.ReadAsync(reference, TestContext.Current.CancellationToken)).GetProperty("InputKey").GetString());
    }

    [Fact]
    public async Task DuplicateDoesNotIncrementWatermark()
    {
        using var directory = new ArtifactDirectory();
        await using var store = directory.Open();
        var ledger = new DurableSessionLedger(store);
        var input = await store.PutOnceAsync(DurableIdentityTests.Scope, "input", ExecutionSnapshotCodecTests.Json("1"), TestContext.Current.CancellationToken);
        var first = await ledger.AdmitAsync(Request(input), TestContext.Current.CancellationToken);
        Assert.Equal(first, await ledger.AdmitAsync(Request(input) with { ProposedWatermark = 2 }, TestContext.Current.CancellationToken));
        var next = await ledger.AdmitAsync(Request(input) with { InputKey = "epoch-provider-2", ProposedWatermark = 2 }, TestContext.Current.CancellationToken);
        Assert.Equal(2, next.Watermark);
    }

    [Fact]
    public async Task StablePaginationHasNoDuplicateOrMissingItem()
    {
        using var directory = new ArtifactDirectory();
        await using var store = directory.Open();
        var ledger = new DurableSessionLedger(store);
        for (var i = 0; i < 205; i++)
        {
            var reference = await store.PutOnceAsync(DurableIdentityTests.Scope, "trace-" + i, ExecutionSnapshotCodecTests.Json(i.ToString()), TestContext.Current.CancellationToken);
            var record = new LedgerRecord("installation", DurableIdentityTests.Session, "traces", "op-" + i, reference);
            await ledger.RecordOnceAsync(record, TestContext.Current.CancellationToken);
            await ledger.RecordOnceAsync(record, TestContext.Current.CancellationToken);
        }
        var page1 = await ledger.ReadPageAsync("installation", DurableIdentityTests.Session, "traces", new(), TestContext.Current.CancellationToken);
        var page2 = await ledger.ReadPageAsync("installation", DurableIdentityTests.Session, "traces", new(page1.NextCursor), TestContext.Current.CancellationToken);
        var page3 = await ledger.ReadPageAsync("installation", DurableIdentityTests.Session, "traces", new(page2.NextCursor), TestContext.Current.CancellationToken);
        Assert.Equal(100, page1.Items.Count); Assert.Equal(100, page2.Items.Count); Assert.Equal(5, page3.Items.Count);
        Assert.Null(page3.NextCursor);
        var values = new List<int>();
        foreach (var item in page1.Items.Concat(page2.Items).Concat(page3.Items)) values.Add((await store.ReadAsync(item, TestContext.Current.CancellationToken)).GetInt32());
        Assert.Equal(Enumerable.Range(0, 205), values);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await ledger.ReadPageAsync("installation", DurableIdentityTests.Session with { AccountId = "other" }, "traces", new(page1.NextCursor), TestContext.Current.CancellationToken));
    }
}
