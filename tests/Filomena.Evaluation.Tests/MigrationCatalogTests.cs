using Filomena.Evaluation.Storage;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class MigrationCatalogTests
{
    [Fact]
    public void Fresh_database_requires_both_migrations_in_order()
    {
        var pending = MigrationCatalog.Pending(new Dictionary<int, string>());
        Assert.Equal(new[] { 1, 2 }, pending.Select(m => m.Version));
        Assert.All(pending, m => Assert.False(string.IsNullOrWhiteSpace(m.Sql)));
    }

    [Fact]
    public void Baseline_database_requires_only_execution_upgrade()
    {
        var all = MigrationCatalog.Pending(new Dictionary<int, string>());
        var pending = MigrationCatalog.Pending(new Dictionary<int, string> { [1] = all[0].Checksum });
        Assert.Equal(2, Assert.Single(pending).Version);
    }

    [Fact]
    public void Already_applied_migrations_are_not_run_again()
    {
        var applied = MigrationCatalog.Pending(new Dictionary<int, string>()).ToDictionary(m => m.Version, m => m.Checksum);
        Assert.Empty(MigrationCatalog.Pending(applied));
    }

    [Fact]
    public void Changed_baseline_is_rejected_even_if_latest_checksum_matches()
    {
        var applied = MigrationCatalog.Pending(new Dictionary<int, string>()).ToDictionary(m => m.Version, m => m.Checksum);
        applied[1] = new string('0', 64);
        Assert.Throws<InvalidOperationException>(() => MigrationCatalog.Pending(applied));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Unknown_version_is_rejected(int version) => Assert.Throws<InvalidOperationException>(() =>
        MigrationCatalog.Pending(new Dictionary<int, string> { [version] = new string('0', 64) }));

    [Fact]
    public void Missing_predecessor_is_not_silently_repaired()
    {
        var all = MigrationCatalog.Pending(new Dictionary<int, string>());
        Assert.Throws<InvalidOperationException>(() => MigrationCatalog.Pending(new Dictionary<int, string> { [2] = all[1].Checksum }));
    }
}
