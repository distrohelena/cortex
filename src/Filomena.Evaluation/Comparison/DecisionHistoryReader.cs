using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Filomena.Evaluation.Comparison;

public sealed record DecisionHistoryEntry(Guid DecisionId, Guid ExperimentId, string Author,
    string EvidenceStatus, InvestigationDecision? Decision);
public sealed record DecisionHistoryPage(IReadOnlyList<DecisionHistoryEntry> Items, Guid? NextAfter);

/// <summary>Scoped history, not activation or a guarantee that evidence remains available after this snapshot.</summary>
public sealed class DecisionHistoryReader(NpgsqlDataSource source)
{
    public static void ValidatePage(int limit, Guid? after, Guid? experimentId)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (after == Guid.Empty || experimentId == Guid.Empty) throw new ArgumentException("Invalid history reference.");
    }

    public async Task<DecisionHistoryPage> ReadAsync(EvaluationAccess access, int limit, Guid? after,
        Guid? experimentId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        // Reasons may reveal holdout conclusions. Proposers/operators cannot read this content.
        access.Demand(EvaluationOperation.Evaluate);
        ValidatePage(limit, after, experimentId);
        token.ThrowIfCancellationRequested();
        await using var command = source.CreateCommand("""
            WITH page AS (
                SELECT id,experiment_id,decided_by,decision,workspace_id,project_id
                FROM evaluation.decisions WHERE workspace_id=@workspace AND project_id=@project
                  AND (@after IS NULL OR id>@after) AND (@experiment IS NULL OR experiment_id=@experiment)
                ORDER BY id LIMIT @take
            ), checked AS (
                SELECT p.*,
                  jsonb_typeof(decision->'Evidence')='array'
                  AND jsonb_array_length(CASE WHEN jsonb_typeof(decision->'Evidence')='array'
                        THEN decision->'Evidence' ELSE '[]'::jsonb END) BETWEEN 1 AND 64
                  AND NOT EXISTS (
                    SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(decision->'Evidence')='array'
                        THEN decision->'Evidence' ELSE '[]'::jsonb END) e
                    LEFT JOIN evaluation.artifacts a ON a.workspace_id=p.workspace_id AND a.project_id=p.project_id
                      AND a.id::text=e->>'Id' AND a.sha256=e->>'Sha256' AND a.redacted_at IS NULL
                    WHERE a.id IS NULL
                  ) AS available
                FROM page p
            ) SELECT id,experiment_id,decided_by,
                CASE WHEN available AND octet_length(decision::text)<=1048576 THEN decision::text ELSE NULL END
              FROM checked ORDER BY id
            """);
        command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
        command.Parameters.AddWithValue("project", access.Scope.ProjectId);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        command.Parameters.AddWithValue("experiment", NpgsqlDbType.Uuid, (object?)experimentId ?? DBNull.Value);
        command.Parameters.AddWithValue("take", limit + 1);
        var items = new List<DecisionHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (items.Count == limit) return new(items, items[^1].DecisionId);
            var id = reader.GetGuid(0);
            var experiment = reader.GetGuid(1);
            var author = reader.GetString(2);
            if (reader.IsDBNull(3))
            {
                items.Add(new(id, experiment, author, "unavailable", null));
                continue;
            }
            try
            {
                var decision = InvestigationHistory.Validate(JsonSerializer.Deserialize<InvestigationDecision>(reader.GetString(3))
                    ?? throw new InvalidDataException("Missing decision."));
                if (decision.DecisionId != id || decision.ExperimentId != experiment)
                    throw new InvalidDataException("Decision identity mismatch.");
                items.Add(new(id, experiment, author, "available", decision));
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException)
            {
                items.Add(new(id, experiment, author, "invalid_record", null));
            }
        }
        return new(items, null);
    }
}
