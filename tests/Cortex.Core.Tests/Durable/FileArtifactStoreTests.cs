using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class FileArtifactStoreTests
{
    [Fact]
    public async Task ReopenReturnsIdenticalArtifact()
    {
        using var directory = new ArtifactDirectory();
        ArtifactRef reference;
        await using (var store = directory.Open()) reference = await store.PutOnceAsync(DurableIdentityTests.Scope, "step", ExecutionSnapshotCodecTests.Json("{\"result\":42}"), TestContext.Current.CancellationToken);
        await using var reopened = directory.Open();
        Assert.Equal(42, (await reopened.ReadAsync(reference, TestContext.Current.CancellationToken)).GetProperty("result").GetInt32());
        Assert.Equal(reference, await reopened.FindAsync(reference.Scope, "step", TestContext.Current.CancellationToken));
        Assert.Equal(reference, await reopened.PutOnceAsync(reference.Scope, "step", ExecutionSnapshotCodecTests.Json("{\"result\":42}"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SameKeyDifferentPayloadFails()
    {
        using var directory = new ArtifactDirectory();
        await using var store = directory.Open();
        await store.PutOnceAsync(DurableIdentityTests.Scope, "step", ExecutionSnapshotCodecTests.Json("1"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.PutOnceAsync(DurableIdentityTests.Scope, "step", ExecutionSnapshotCodecTests.Json("2"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WrongScopeAndDigestFail()
    {
        using var directory = new ArtifactDirectory();
        await using var store = directory.Open();
        var reference = await store.PutOnceAsync(DurableIdentityTests.Scope, "step", ExecutionSnapshotCodecTests.Json("1"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ReadAsync(reference with { Sha256 = new string('0', 64) }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ReadAsync(reference with { Scope = reference.Scope with { ExecutionId = "foreign" } }, TestContext.Current.CancellationToken));
        var file = Assert.Single(Directory.GetFiles(directory.Path, "*.json", SearchOption.AllDirectories));
        await File.WriteAllTextAsync(file, "{\"truncated\":", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ReadAsync(reference, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TraversalAndForeignInstallationFail()
    {
        using var directory = new ArtifactDirectory();
        await using (var store = directory.Open())
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await store.PutOnceAsync(DurableIdentityTests.Scope, "../escape", ExecutionSnapshotCodecTests.Json("1"), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await store.PutOnceAsync(DurableIdentityTests.Scope with { InstallationId = "foreign" }, "step", ExecutionSnapshotCodecTests.Json("1"), TestContext.Current.CancellationToken));
        }
        Assert.Throws<InvalidDataException>(() => new FileExecutionArtifactStore(directory.Path, "foreign"));
    }

    [Fact]
    public async Task SecondWriterCannotOpen()
    {
        using var directory = new ArtifactDirectory();
        await using var store = directory.Open();
        Assert.Throws<IOException>(() => directory.Open());
        // Cross-process lock is exercised by a child PowerShell/.NET file opener.
        var shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        var info = new System.Diagnostics.ProcessStartInfo(shell) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("try { $s=[IO.File]::Open($env:CORTEX_TEST_LOCK_PATH, 'OpenOrCreate', 'ReadWrite', 'None'); $s.Dispose(); exit 1 } catch [IO.IOException] { exit 0 } catch { exit 2 }");
        info.Environment["CORTEX_TEST_LOCK_PATH"] = System.IO.Path.Combine(directory.Path, ".writer.lock");
        using var process = System.Diagnostics.Process.Start(info)!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
        await store.DisposeAsync();
        using var unlocked = System.Diagnostics.Process.Start(info)!;
        await unlocked.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, unlocked.ExitCode);
    }

    [Fact]
    public async Task IncompleteTemporaryWriteDoesNotReplaceCommittedArtifact()
    {
        using var directory = new ArtifactDirectory();
        ArtifactRef reference;
        await using (var store = directory.Open()) reference = await store.PutOnceAsync(DurableIdentityTests.Scope, "step", ExecutionSnapshotCodecTests.Json("42"), TestContext.Current.CancellationToken);
        var committed = Assert.Single(Directory.GetFiles(directory.Path, "*.json", SearchOption.AllDirectories));
        await File.WriteAllTextAsync(committed + ".tmp", "partial", TestContext.Current.CancellationToken);
        await using var reopened = directory.Open();
        Assert.Equal(42, (await reopened.ReadAsync(reference, TestContext.Current.CancellationToken)).GetInt32());
    }
}

internal sealed class ArtifactDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cortex-artifacts-test-" + Guid.NewGuid().ToString("N"));
    internal FileExecutionArtifactStore Open() => new(Path, "installation");
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}
