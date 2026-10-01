using Cortex.Evaluation.Contracts;
using Npgsql;

namespace Cortex.Evaluation.Execution;

public sealed class ExperimentStatusReader(NpgsqlDataSource source)
{
    public async Task<ExperimentStatus?> ReadAsync(EvaluationAccess access, Guid experimentId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.ReadExperiment);
        if (experimentId == Guid.Empty) throw new InvalidDataException("Explicit experiment identity is required.");
        token.ThrowIfCancellationRequested();
        // One SELECT uses one MVCC snapshot. No locks/leases, status repair or payload reads.
        await using var command = source.CreateCommand("""
            WITH scoped_trials AS (
              SELECT id,state FROM evaluation.trials
              WHERE workspace_id=@workspace AND project_id=@project AND experiment_id=@experiment
            ), scoped_attempts AS (
              SELECT a.trial_id,a.state FROM evaluation.attempts a JOIN scoped_trials t ON t.id=a.trial_id
              WHERE a.workspace_id=@workspace AND a.project_id=@project
            )
            SELECT e.execution_state,e.cancel_requested,e.reserved_calls,e.reserved_input_tokens,
              e.reserved_output_tokens,e.reserved_cost,
              (SELECT count(*) FROM scoped_trials WHERE state='pending'),
              (SELECT count(*) FROM scoped_trials WHERE state='running'),
              (SELECT count(*) FROM scoped_trials WHERE state='completed'),
              (SELECT count(*) FROM scoped_trials WHERE state='indeterminate'),
              (SELECT count(*) FROM scoped_trials WHERE state NOT IN ('pending','running','completed','indeterminate')),
              (SELECT count(*) FROM scoped_attempts WHERE state='started'),
              (SELECT count(*) FROM scoped_attempts WHERE state='observed'),
              (SELECT count(*) FROM scoped_attempts WHERE state='indeterminate'),
              (SELECT count(*) FROM scoped_attempts WHERE state NOT IN ('started','observed','indeterminate')),
              (SELECT count(*) FROM scoped_trials t WHERE t.state='completed' AND NOT EXISTS
                (SELECT 1 FROM scoped_attempts a WHERE a.trial_id=t.id AND a.state='observed'))
            FROM evaluation.experiments e
            WHERE e.workspace_id=@workspace AND e.project_id=@project AND e.id=@experiment
            """);
        command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
        command.Parameters.AddWithValue("project", access.Scope.ProjectId);
        command.Parameters.AddWithValue("experiment", experimentId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return ExperimentStatus.Create(experimentId, reader.GetString(0), reader.GetBoolean(1),
            new(reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10))
            { CompletedWithoutObservedAttempt = reader.GetInt64(15) },
            new(reader.GetInt64(11), reader.GetInt64(12), reader.GetInt64(13), reader.GetInt64(14)),
            new(reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetDecimal(5)));
    }
}
