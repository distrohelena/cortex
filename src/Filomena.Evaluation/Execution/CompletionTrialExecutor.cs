using Filomena.Evaluation.Contracts;
using Npgsql;
using Filomena.Evaluation.Storage;

namespace Filomena.Evaluation.Execution;

public sealed record ExecutedCompletionTrial(Guid TrialId, EvidenceArtifactRef Result);

public sealed class CompletionTrialExecutor(NpgsqlDataSource source)
{
    // Claims only pending work; callers cannot replay a supplied claim through this entrypoint.
    // Failures stop the experiment for reconciliation, never reset a claim to pending.
    public async Task<ExecutedCompletionTrial?> ExecuteNextAsync(EvaluationAccess access, Guid experiment,
        IReadOnlyList<EvaluationEndpointSettings> endpoints, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(endpoints);
        access.Demand(EvaluationOperation.Evaluate);
        if (experiment == Guid.Empty) throw new InvalidDataException("Explicit experiment identity is required.");
        token.ThrowIfCancellationRequested();
        var hostEndpoints = endpoints.ToArray();
        var store = new PostgresEvaluationStore(source);
        ScopedRevisionRef targetReference;
        await using (var connection = await source.OpenConnectionAsync(token))
        await using (var lookup = new NpgsqlCommand("""
            SELECT target_revision_id,target_revision_sha256 FROM evaluation.experiments
            WHERE workspace_id=@workspace AND project_id=@project AND id=@experiment AND plan_sha256 IS NOT NULL
            """, connection))
        {
            lookup.Parameters.AddWithValue("workspace", access.Scope.WorkspaceId);
            lookup.Parameters.AddWithValue("project", access.Scope.ProjectId);
            lookup.Parameters.AddWithValue("experiment", experiment);
            await using var reader = await lookup.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Scoped experiment is unavailable.");
            targetReference = new(access.Scope, new(reader.GetGuid(0), reader.GetString(1).Trim()));
        }
        var target = await store.GetRevisionAsync(access, targetReference.Revision, token)
            ?? throw new InvalidDataException("Pinned target is unavailable.");
        var coordinator = new ExperimentCoordinator(source);
        var claim = await coordinator.ClaimNextAsync(access, experiment, Guid.NewGuid(), token);
        if (claim is null) return null; // No pending work is not proof the experiment completed.
        try
        {
            var profile = await store.GetRevisionAsync(access, claim.Candidate.Revision, token)
                ?? throw new InvalidDataException("Pinned profile is unavailable.");
            var input = await store.GetRevisionAsync(access, claim.Case.Revision, token)
                ?? throw new InvalidDataException("Pinned input is unavailable.");
            var material = CompletionTrialMaterial.FromStoredDocuments(access, claim, targetReference, target, profile, input);
            using var providers = BoundEvaluationProviders.Create(source, access, experiment, claim, hostEndpoints);
            var result = await new FlowCompletionEvaluationTarget(providers.Factory).ExecuteAsync(
                access, claim.Candidate, material.Profile, material.Input, token);
            // Current revision-backed inputs are development corpus, not private holdouts.
            var artifact = await store.RegisterArtifactAsync(access, result.ToArtifact(false), token);
            await coordinator.CompleteAsync(access, experiment, claim, result, artifact, token);
            return new(claim.TrialId, artifact);
        }
        catch (Exception error)
        {
            var code = error is OperationCanceledException ? "execution_cancelled" : "execution_failed";
            var recorded = false;
            try
            {
                // Request cancellation must not suppress the stop record. No remote retry follows.
                await coordinator.StopFailedTrialAsync(access, experiment, claim, code, CancellationToken.None);
                recorded = true;
            }
            catch (Exception)
            {
                // A commit may already have succeeded or storage may be unavailable.
                // Do not replace that uncertainty with a fabricated terminal state.
            }
            throw new CompletionTrialExecutionException(claim.TrialId, code, recorded);
        }
    }
}

public sealed class CompletionTrialExecutionException(Guid trialId, string code, bool stopRecorded)
    : Exception($"Evaluation trial {trialId:D}: {code}; stopRecorded={stopRecorded}.")
{
    public Guid TrialId { get; } = trialId;
    public string Code { get; } = code;
    public bool StopRecorded { get; } = stopRecorded;
}
