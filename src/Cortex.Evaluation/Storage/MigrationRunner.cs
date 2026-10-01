using System.Security.Cryptography;
using Npgsql;

namespace Cortex.Evaluation.Storage;

/// <summary>Applies embedded migrations when explicitly invoked with migration-owner credentials.</summary>
public sealed class MigrationRunner(NpgsqlDataSource dataSource)
{
    private const long AdvisoryLockId = 721508624416591201;

    public static string ComputeChecksum(string sql) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n", StringComparison.Ordinal))));

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
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

        var applied = new Dictionary<int, string>();
        await using (var lookup = new NpgsqlCommand(
                         "SELECT version, checksum FROM evaluation.schema_migrations ORDER BY version",
                         connection, transaction))
        {
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                applied.Add(reader.GetInt32(0), reader.GetString(1));
        }

        foreach (var migration in MigrationCatalog.Pending(applied))
        {
            await using (var apply = new NpgsqlCommand(migration.Sql, connection, transaction))
                await apply.ExecuteNonQueryAsync(cancellationToken);
            await using var record = new NpgsqlCommand(
                "INSERT INTO evaluation.schema_migrations (version, checksum) VALUES (@version, @checksum)",
                connection, transaction);
            record.Parameters.AddWithValue("version", migration.Version);
            record.Parameters.AddWithValue("checksum", migration.Checksum);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
