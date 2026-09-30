using System.Data;
using Filomena.Evaluation.Contracts;
using Npgsql;

namespace Filomena.Evaluation.Comparison;

public sealed class IssueHistoryReader(NpgsqlDataSource source)
{
    internal static Guid[] Validate(IReadOnlyList<Guid> reportIds)
    {
        if (reportIds is null || reportIds.Count is < 1 or > 20)
            throw new ArgumentException("One to twenty report identities required.", nameof(reportIds));
        var ids = reportIds.ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Explicit unique report identities required.", nameof(reportIds));
        return ids;
    }

    public async Task<IssueHistoryResult?> ReadAsync(EvaluationAccess access, IReadOnlyList<Guid> reportIds, CancellationToken token)
    {
        access.Demand(EvaluationOperation.Evaluate);
        var ids = Validate(reportIds);
        token.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await mode.ExecuteNonQueryAsync(token);
        var reports = new List<StoredComparisonReport>();
        foreach (var id in ids)
        {
            var report = await ComparisonReportStore.ReadInSnapshotAsync(access, id, connection, transaction, token);
            if (report is null) return null; // Never silently drop a missing/foreign report from the denominator.
            reports.Add(report);
        }
        var result = IssueHistoryProjection.Create(reports);
        await transaction.CommitAsync(token);
        return result;
    }
}
