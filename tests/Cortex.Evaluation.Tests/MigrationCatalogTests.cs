using Cortex.Evaluation.Storage;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class MigrationCatalogTests
{
    [Fact]
    public void PreCortexDatabaseChecksumsRemainAppliedWithoutRewritingHistory()
    {
        // Independently captured before the assembly rename; LF-normalized SQL SHA-256.
        var applied = new Dictionary<int, string>
        {
            [1] = "aeae3a58ad2dcf9a4286d5ba62885e02e17a3dfbe6b7eee4dd9e328588e6ebc0",
            [2] = "8423b5984140f804aa9e4b597f12a8f957e8496b284aae951d6f474a5a1e8872",
            [3] = "dd9b516368e465d8c891e7bc28a420af04b8631bc84ed11fd66ceb2fa881b36f",
            [4] = "65bb91e1977ca9e5abc572dec40e7016ca0e331f219743c93b1e9906665e8737",
            [5] = "12b3a8737aa87d459ff41f7e3728656b2af2ca0b2cac9b550fde54184877f7ce",
            [6] = "6727a6c51181d24f9a40fd9d0475e79f65abc5ceeba58eb3907273d7c753685e",
            [7] = "226587b116c09c5027a8f530740b313c176820dc3f861d42df9c1f5032afac46",
            [8] = "d43140397d67f6effbe5fa2ecda6c5a43946248ff79092bab3c978eb033b5d40",
            [9] = "4c2e27c4d47f0026a99f68d804e185321698a12374bf4f957f25506c53d1dcc4",
        };
        var available = MigrationCatalog.Pending(new Dictionary<int, string>());
        Assert.Equal(applied.Count, available.Count);
        Assert.All(available, migration => Assert.Equal(applied[migration.Version], migration.Checksum));
        Assert.Empty(MigrationCatalog.Pending(applied));
    }

    [Fact]
    public void Fresh_database_requires_all_migrations_in_order()
    {
        var pending = MigrationCatalog.Pending(new Dictionary<int, string>());
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, pending.Select(m => m.Version));
        Assert.All(pending, m => Assert.False(string.IsNullOrWhiteSpace(m.Sql)));
    }

    [Fact]
    public void Baseline_database_requires_execution_and_schedule_upgrades()
    {
        var all = MigrationCatalog.Pending(new Dictionary<int, string>());
        var pending = MigrationCatalog.Pending(new Dictionary<int, string> { [1] = all[0].Checksum });
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8, 9 }, pending.Select(m => m.Version));
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
    [InlineData(10)]
    public void Unknown_version_is_rejected(int version) => Assert.Throws<InvalidOperationException>(() =>
        MigrationCatalog.Pending(new Dictionary<int, string> { [version] = new string('0', 64) }));

    [Fact]
    public void Missing_predecessor_is_not_silently_repaired()
    {
        var all = MigrationCatalog.Pending(new Dictionary<int, string>());
        Assert.Throws<InvalidOperationException>(() => MigrationCatalog.Pending(new Dictionary<int, string> { [2] = all[1].Checksum }));
    }
}
