using Cortex.Evaluation.Contracts;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Cortex.Evaluation.Execution;

public sealed class AttemptLedger : IEvaluationCallJournal
{
    private readonly NpgsqlDataSource source;
    private readonly EvaluationAccess access;
    private readonly Guid experiment, trial, owner;
    private readonly long generation;
    private readonly CallReservation reservation;
    private readonly string? reservationCurrency;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public AttemptLedger(NpgsqlDataSource source, EvaluationAccess access, Guid experiment, Guid trial, Guid owner,
        long generation, CallReservation reservation, string? reservationCurrency = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.Evaluate);
        if (experiment == Guid.Empty || trial == Guid.Empty || owner == Guid.Empty || generation < 1)
            throw new InvalidDataException("An explicit experiment, trial and fenced owner are required.");
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.InputTokens < 0 || reservation.OutputTokens < 0 || reservation.Cost is null or < 0)
            throw new InvalidDataException("A known nonnegative reservation bound is required.");
        this.source = source;
        this.access = access;
        this.experiment = experiment;
        this.trial = trial;
        this.owner = owner;
        this.generation = generation;
        this.reservation = reservation;
        this.reservationCurrency = reservationCurrency;
    }

    public async Task<Guid> BeginAsync(PhysicalCallIntent intent, CancellationToken cancellationToken)
    {
        if (intent.Provider is not ("openai" or "jev" or "lmstudio" or "laya") ||
            intent.RequestBytes is < 0 or > 1_048_576 || intent.RequestSha256 is not { Length: 64 } ||
            intent.RequestSha256.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid physical call intent.");
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockAsync(connection, transaction, cancellationToken);
        var monetaryApproval = await MonetaryBudgetStore.ReadInTransactionAsync(access.Scope, experiment,
            connection, transaction, cancellationToken);
        MonetaryBudgetContract.DemandForAttempt(monetaryApproval, access.Scope, experiment,
            state.PlanSha256 ?? "", reservationCurrency, state.Budgets.MaxCost);
        await using var pending = Command("""
            SELECT COALESCE(MAX(attempt_number), 0), COALESCE(bool_or(state IN ('started', 'indeterminate')), false)
            FROM evaluation.attempts WHERE workspace_id=@workspace AND project_id=@project AND trial_id=@trial
            """, connection, transaction);
        int number;
        bool unresolved;
        await using (var reader = await pending.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            number = checked(reader.GetInt32(0) + 1);
            unresolved = reader.GetBoolean(1);
        }
        var next = AttemptAdmission.Reserve(state.Admission with { HasUnresolvedAttempt = unresolved }, owner,
            generation, state.Budgets, state.Consumption, reservation);
        var attempt = Guid.NewGuid();
        await using (var insert = Command("""
            INSERT INTO evaluation.attempts
              (workspace_id, project_id, id, trial_id, attempt_number, state, lease_owner, lease_generation,
               provider, request_sha256, request_bytes, reserved_input_tokens, reserved_output_tokens, reserved_cost,
               monetary_approval_id, reservation_currency)
            VALUES (@workspace,@project,@attempt,@trial,@number,'started',@owner,@generation,
                    @provider,@hash,@bytes,@input,@output,@cost,@approval,@currency)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("attempt", attempt);
            insert.Parameters.AddWithValue("number", number);
            insert.Parameters.AddWithValue("provider", intent.Provider);
            insert.Parameters.AddWithValue("hash", intent.RequestSha256);
            insert.Parameters.AddWithValue("bytes", intent.RequestBytes);
            insert.Parameters.AddWithValue("input", reservation.InputTokens);
            insert.Parameters.AddWithValue("output", reservation.OutputTokens);
            insert.Parameters.AddWithValue("cost", reservation.Cost!.Value);
            insert.Parameters.AddWithValue("approval", monetaryApproval!.ApprovalId);
            insert.Parameters.AddWithValue("currency", reservationCurrency!);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var update = Command("""
            UPDATE evaluation.experiments SET reserved_calls=@calls, reserved_input_tokens=@input,
              reserved_output_tokens=@output, reserved_cost=@cost
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("calls", next.Calls);
            update.Parameters.AddWithValue("input", next.InputTokens);
            update.Parameters.AddWithValue("output", next.OutputTokens);
            update.Parameters.AddWithValue("cost", next.Cost);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        // The HTTP boundary must not send until this commit has completed. Uncertain commits are not retried.
        await transaction.CommitAsync(cancellationToken);
        return attempt;
    }

    public async Task FinishAsync(Guid attempt, PhysicalCallOutcome outcome, CancellationToken cancellationToken)
    {
        if (attempt == Guid.Empty || outcome.Elapsed < TimeSpan.Zero ||
            outcome.Status is not ("response_received" or "indeterminate") ||
            outcome.Status == "response_received" && (outcome.Observation is null || outcome.ErrorCode is not null) ||
            outcome.Status == "indeterminate" && (outcome.Observation is not null || outcome.ErrorCode is not
                ("transport_failure" or "request_cancelled" or "response_too_large")))
            throw new InvalidDataException("Invalid physical call outcome.");
        var serialized = JsonSerializer.Serialize(outcome, Json);
        if (System.Text.Encoding.UTF8.GetByteCount(serialized) > 16_384)
            throw new InvalidDataException("Physical call observation is too large.");
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Same lock order as Begin: experiment, trial, attempt. Cancellation blocks new calls,
        // but must not suppress accounting for a response to a call already sent.
        var state = await LockAsync(connection, transaction, cancellationToken);
        if (state.Admission.LeaseOwner != owner || state.Admission.LeaseGeneration != generation)
            throw new InvalidOperationException("Attempt completion belongs to an obsolete owner.");
        long inputBound, outputBound;
        await using (var lookup = Command("""
            SELECT reserved_input_tokens, reserved_output_tokens FROM evaluation.attempts
            WHERE workspace_id=@workspace AND project_id=@project AND id=@attempt AND trial_id=@trial
              AND lease_owner=@owner AND lease_generation=@generation AND state='started' FOR UPDATE
            """, connection, transaction))
        {
            lookup.Parameters.AddWithValue("attempt", attempt);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Attempt is absent or already finalized.");
            inputBound = reader.GetInt64(0);
            outputBound = reader.GetInt64(1);
        }
        await using (var update = Command("""
            UPDATE evaluation.attempts SET state=@state, outcome=@outcome, finished_at=clock_timestamp()
            WHERE workspace_id=@workspace AND project_id=@project AND id=@attempt AND trial_id=@trial
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("attempt", attempt);
            update.Parameters.AddWithValue("state", outcome.Status == "indeterminate" ? "indeterminate" : "observed");
            update.Parameters.AddWithValue("outcome", NpgsqlDbType.Jsonb, serialized);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        var usage = outcome.Observation?.Usage;
        if (outcome.Status == "indeterminate" || usage?.InputTokens > inputBound || usage?.OutputTokens > outputBound)
        {
            await using var stop = Command("""
                UPDATE evaluation.experiments SET execution_state='indeterminate', cancel_requested=true
                WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment
                """, connection, transaction);
            await stop.ExecuteNonQueryAsync(cancellationToken);
        }
        // Reservations are deliberately retained, including failure/unknown usage. No automatic refund.
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<LockedState> LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        ExperimentManifest manifest;
        BudgetConsumption consumed;
        string experimentState;
        bool cancelled;
        string? planSha256;
        await using (var lookup = Command("""
            SELECT manifest::text, execution_state, cancel_requested, reserved_calls,
              reserved_input_tokens, reserved_output_tokens, reserved_cost, plan_sha256
            FROM evaluation.experiments WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment FOR UPDATE
            """, connection, transaction))
        {
            await using var reader = await lookup.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Scoped experiment is unavailable.");
            manifest = JsonSerializer.Deserialize<ExperimentManifest>(reader.GetString(0), Json)
                ?? throw new InvalidDataException("Experiment manifest unavailable.");
            if (ManifestValidator.Validate(manifest).Count != 0) throw new InvalidDataException("Invalid experiment manifest.");
            access.DemandScope(manifest.Scope);
            experimentState = reader.GetString(1);
            cancelled = reader.GetBoolean(2);
            consumed = new(reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetDecimal(6));
            planSha256 = reader.IsDBNull(7) ? null : reader.GetString(7);
        }
        await using var trialLookup = Command("""
            SELECT state, lease_owner, lease_generation FROM evaluation.trials
            WHERE workspace_id=@workspace AND project_id=@project AND id=@trial AND experiment_id=@experiment FOR UPDATE
            """, connection, transaction);
        await using var trialReader = await trialLookup.ExecuteReaderAsync(token);
        if (!await trialReader.ReadAsync(token)) throw new KeyNotFoundException("Scoped trial is unavailable.");
        return new(manifest.Budgets, consumed, new(experimentState, cancelled, trialReader.GetString(0),
            trialReader.IsDBNull(1) ? null : trialReader.GetGuid(1), trialReader.GetInt64(2), false), planSha256);
    }

    private NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
        command.Parameters.AddWithValue("project", access.Scope.ProjectId);
        command.Parameters.AddWithValue("experiment", experiment);
        command.Parameters.AddWithValue("trial", trial);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("generation", generation);
        return command;
    }

    private sealed record LockedState(ExperimentBudgets Budgets, BudgetConsumption Consumption,
        AttemptAdmissionState Admission, string? PlanSha256);
}
