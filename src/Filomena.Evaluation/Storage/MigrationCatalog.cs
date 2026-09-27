namespace Filomena.Evaluation.Storage;

public sealed record EvaluationMigration(int Version, string Sql, string Checksum);

public static class MigrationCatalog
{
    public static IReadOnlyList<EvaluationMigration> Pending(IReadOnlyDictionary<int, string> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        var migrations = new[] { Read(1, "001_evaluation.sql"), Read(2, "002_execution.sql"),
            Read(3, "003_schedule.sql"), Read(4, "004_trial_result.sql") };
        if (applied.Keys.Any(version => version < 1 || version > migrations.Length) ||
            !applied.Keys.Order().SequenceEqual(Enumerable.Range(1, applied.Count)))
            throw new InvalidOperationException("Evaluation migration history has an unknown version or a missing predecessor.");
        foreach (var migration in migrations)
            if (applied.TryGetValue(migration.Version, out var checksum) &&
                !string.Equals(checksum?.Trim(), migration.Checksum, StringComparison.Ordinal))
                throw new InvalidOperationException("Applied evaluation migration checksum does not match the embedded migration.");
        return Array.AsReadOnly(migrations.Where(m => !applied.ContainsKey(m.Version)).ToArray());
    }

    private static EvaluationMigration Read(int version, string file)
    {
        using var stream = typeof(MigrationCatalog).Assembly.GetManifestResourceStream(
            "Filomena.Evaluation.Storage.Migrations." + file)
            ?? throw new InvalidOperationException("Embedded evaluation migration is missing.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        return new(version, sql, MigrationRunner.ComputeChecksum(sql));
    }
}
