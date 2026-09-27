using System.Reflection;
using System.Security.Cryptography;
using Npgsql;

namespace Filomena.Evaluation.Storage;

/// <summary>Applies the embedded baseline when explicitly invoked with migration-owner credentials.</summary>
public sealed class MigrationRunner(NpgsqlDataSource dataSource)
{
    private const int BaselineVersion = 1;
    private const string ResourceName = "Filomena.Evaluation.Storage.Migrations.001_evaluation.sql";
    private const long AdvisoryLockId = 721508624416591201;

    public static string ComputeChecksum(string sql) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n", StringComparison.Ordinal))));

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var migration = ReadMigration();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(@lock_id)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("lock_id", AdvisoryLockId);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var setup = new NpgsqlCommand(
                         "CREATE SCHEMA IF NOT EXISTS evaluation; " +
                         "CREATE TABLE IF NOT EXISTS evaluation.schema_migrations (version integer PRIMARY KEY, checksum char(64) NOT NULL, applied_at timestamptz NOT NULL DEFAULT clock_timestamp())",
                         connection, transaction))
            await setup.ExecuteNonQueryAsync(cancellationToken);

        await using (var versionCommand = new NpgsqlCommand(
                         "SELECT COALESCE(MAX(version), 0) FROM evaluation.schema_migrations", connection, transaction))
        {
            var highestVersion = (int)(await versionCommand.ExecuteScalarAsync(cancellationToken))!;
            if (highestVersion > BaselineVersion || highestVersion < 0)
                throw new InvalidOperationException("Evaluation database contains an unsupported migration version.");
        }

        await using (var lookup = new NpgsqlCommand(
                         "SELECT checksum FROM evaluation.schema_migrations WHERE version = @version",
                         connection, transaction))
        {
            lookup.Parameters.AddWithValue("version", BaselineVersion);
            var existing = await lookup.ExecuteScalarAsync(cancellationToken);
            if (existing is string checksum)
            {
                if (!string.Equals(checksum.Trim(), migration.Checksum, StringComparison.Ordinal))
                    throw new InvalidOperationException("Applied evaluation migration checksum does not match the embedded migration.");
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }

        await using (var apply = new NpgsqlCommand(migration.Sql, connection, transaction))
            await apply.ExecuteNonQueryAsync(cancellationToken);
        await using (var record = new NpgsqlCommand(
                         "INSERT INTO evaluation.schema_migrations (version, checksum) VALUES (@version, @checksum)",
                         connection, transaction))
        {
            record.Parameters.AddWithValue("version", BaselineVersion);
            record.Parameters.AddWithValue("checksum", migration.Checksum);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static (string Sql, string Checksum) ReadMigration()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded evaluation baseline migration is missing.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        var checksum = ComputeChecksum(sql);
        return (sql, checksum);
    }
}
