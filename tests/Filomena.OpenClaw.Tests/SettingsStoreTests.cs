using System.Text.Json;
using Filomena.App.Settings;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task FirstUseSeedsTheExactApprovedConversationAllowlist()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");

        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);

        Assert.Equal(
            [
                "whatsapp-dm-5511944558218-s-whatsapp-net",
                "whatsapp-group-120363413037344295-g-us",
            ],
            store.Current.Desired.Generation.AllowedConversationNamespaces);
        Assert.Equal(["5511944558218@s.whatsapp.net"],
            store.Current.Desired.Turns.AllowlistedDirectMessageNativeIds);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveAndReloadPreservesDesiredRevisionAndEffectiveValues()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);
        var before = store.Current;
        var candidate = before.Desired with
        {
            Model = before.Desired.Model with { Model = "test/model" },
        };

        var updated = await store.UpdateAsync(
            new SettingsUpdate(before.Revision, candidate), CancellationToken.None);
        await using var reloaded = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);

        Assert.Equal(before.Revision + 1, updated.Revision);
        Assert.Equal("test/model", updated.Desired.Model.Model);
        Assert.Equal("test/model", updated.Effective.Model.Model);
        Assert.Equal(updated.Revision, reloaded.Current.Revision);
        Assert.Equal(updated.Desired, reloaded.Current.Desired);
    }

    [Fact]
    public async Task StaleExpectedRevisionDoesNotChangeCurrentState()
    {
        using var directory = TemporaryDirectory.Create();
        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = Path.Combine(directory.Path, "settings.json") },
            CancellationToken.None);
        var before = store.Current;

        await Assert.ThrowsAsync<SettingsConflictException>(() => store.UpdateAsync(
            new SettingsUpdate(before.Revision - 1, before.Desired), CancellationToken.None));

        Assert.Equal(before.Revision, store.Current.Revision);
        Assert.Equal(before.Desired, store.Current.Desired);
    }

    [Fact]
    public async Task InvalidFiniteAndRangeValuesAreRejectedWithoutPublishing()
    {
        using var directory = TemporaryDirectory.Create();
        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = Path.Combine(directory.Path, "settings.json") },
            CancellationToken.None);
        var before = store.Current;
        var invalid = before.Desired with
        {
            Model = before.Desired.Model with
            {
                GenerationTemperature = double.NaN,
                RequestTimeoutSeconds = 0,
            },
        };

        var exception = await Assert.ThrowsAsync<SettingsValidationException>(() => store.UpdateAsync(
            new SettingsUpdate(before.Revision, invalid), CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Path == "model.generationTemperature");
        Assert.Contains(exception.Errors, error => error.Path == "model.requestTimeoutSeconds");
        Assert.Equal(before.Revision, store.Current.Revision);
    }

    [Fact]
    public async Task ArraysUseWholeReplacementInsteadOfIndexedMerge()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);
        var before = store.Current;
        var replacement = before.Desired with
        {
            Probe = before.Desired.Probe with { OpenClawArguments = ["custom", "--flag"] },
        };

        await store.UpdateAsync(new SettingsUpdate(before.Revision, replacement), CancellationToken.None);
        await using var reloaded = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);

        Assert.Equal(["custom", "--flag"], reloaded.Current.Desired.Probe.OpenClawArguments);
    }

    [Fact]
    public async Task EnvironmentAndCliOverridesAreAppliedAndRecordedWithoutChangingDesired()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        var overrides = new[]
        {
            new SettingsOverride("model.model", "environment", "override/model"),
            new SettingsOverride("probe.openClawArguments", "cli", "[\"one\",\"two\"]"),
        };
        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path, Overrides = overrides },
            CancellationToken.None);

        Assert.Equal("override/model", store.Current.Effective.Model.Model);
        Assert.NotEqual("override/model", store.Current.Desired.Model.Model);
        Assert.Equal(["one", "two"], store.Current.Effective.Probe.OpenClawArguments);
        Assert.Equal(2, store.Current.Overrides.Count);
        Assert.Contains(store.Current.Overrides, item => item.Path == "model.model" && item.Source == "environment");
    }

    [Fact]
    public async Task CorruptLiveDocumentUsesValidPreviousVersionWithoutDestroyingIt()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        await using (var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None))
        {
            var first = store.Current;
            await store.UpdateAsync(new SettingsUpdate(first.Revision,
                first.Desired with { Model = first.Desired.Model with { Model = "first/model" } }),
                CancellationToken.None);
            var second = store.Current;
            await store.UpdateAsync(new SettingsUpdate(second.Revision,
                second.Desired with { Model = second.Desired.Model with { Model = "second/model" } }),
                CancellationToken.None);
        }

        await File.WriteAllTextAsync(path, "{ definitely not valid json", CancellationToken.None);
        await using var recovered = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);

        Assert.Equal("first/model", recovered.Current.Desired.Model.Model);
        Assert.Equal(SettingsRecoveryKind.UsedPreviousVersion, recovered.Current.Recovery.Kind);
        Assert.Equal("{ definitely not valid json", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CorruptLiveDocumentWithoutBackupFailsClosedButKeepsRepairState()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, "{ definitely not valid json", CancellationToken.None);

        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);

        Assert.False(store.Current.OutboundEnabled);
        Assert.Equal(SettingsRecoveryKind.NoValidDocument, store.Current.Recovery.Kind);
        Assert.True(store.Current.Recovery.RepairRequired);
        Assert.Equal("{ definitely not valid json", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FailedReplacementKeepsThePreviouslyPublishedSnapshot()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);
        var before = store.Current;
        File.Delete(path);
        Directory.CreateDirectory(path);

        await Assert.ThrowsAnyAsync<IOException>(() => store.UpdateAsync(
            new SettingsUpdate(before.Revision,
                before.Desired with { Model = before.Desired.Model with { Model = "must-not-publish" } }),
            CancellationToken.None));

        Assert.Equal(before.Revision, store.Current.Revision);
        Assert.Equal(before.Desired, store.Current.Desired);
    }

    [Fact]
    public async Task UnknownSchemaVersionIsRejectedAndFailsClosed()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":999,\"revision\":1,\"settings\":{}}",
            CancellationToken.None);

        await using var store = await SettingsStore.OpenAsync(
            new SettingsStoreOptions { FilePath = path }, CancellationToken.None);

        Assert.False(store.Current.OutboundEnabled);
        Assert.Equal(SettingsRecoveryKind.NoValidDocument, store.Current.Recovery.Kind);
        Assert.Contains(store.Current.Recovery.Errors, error => error.Code == "unknown_schema_version");
    }

    [Fact]
    public void CatalogDescribesEverySupportedTypedOption()
    {
        var paths = SettingsCatalog.All.Select(field => field.Path).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("turns.allowlistedDirectMessageNativeIds", paths);
        Assert.Contains("turns.generationWindow", paths);
        Assert.Contains("generation.systemPrompt", paths);
        Assert.Contains("arbitration.directMessagePrompt", paths);
        Assert.Contains("probe.targetConversationIds", paths);
        Assert.Contains("bridge.listenUrl", paths);
        Assert.Contains("frontend.buildTimeoutSeconds", paths);
        Assert.All(SettingsCatalog.All, field => Assert.False(string.IsNullOrWhiteSpace(field.Type)));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filomena-settings-" + Guid.NewGuid());
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
