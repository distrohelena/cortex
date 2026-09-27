using Filomena.Evaluation.Contracts;
using Filomena.Core.Flows;
using System.Diagnostics;
using System.Text.Json;

namespace Filomena.Evaluation.Execution;

public sealed record CompletionEvaluationCase(EvaluationScope Scope, string NodeId,
    RevisionRef InputRevision, RevisionDocument Input);

public sealed record CompletionEvaluationResult(string ProfileSha256, string InputSha256,
    string Output, TimeSpan Elapsed);

/// <summary>Executes a single pinned completion, not a conversational runtime or a quality judge.
/// Paid execution requires host clients wired to the durable per-physical-call journal.
/// The coordinator persists the result and associates attempts; this class never retries.</summary>
public sealed class FlowCompletionEvaluationTarget(EvaluationProviderFactory providers)
{
    public async Task<CompletionEvaluationResult> ExecuteAsync(EvaluationAccess access,
        ScopedRevisionRef profileRevision, ConfigurationProfile profile, CompletionEvaluationCase input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);
        access.Demand(EvaluationOperation.Evaluate);
        access.DemandScope(profileRevision.Scope);
        access.DemandScope(input.Scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(input.NodeId) || input.NodeId.Length > 256 || input.NodeId.Any(char.IsControl) ||
            input.Input.ContractVersion != 1 || input.Input.Kind != "completion-input" ||
            input.Input.Payload.ValueKind != JsonValueKind.Object || input.InputRevision.Id == Guid.Empty ||
            profileRevision.Revision.Id == Guid.Empty)
            throw new InvalidDataException("Invalid pinned completion input.");
        var inputHash = RevisionHasher.Hash(input.Input);
        var profileHash = RevisionHasher.Hash(profile.ToRevisionDocument());
        if (inputHash != input.InputRevision.Sha256 || profileHash != profileRevision.Revision.Sha256)
            throw new InvalidDataException("Evaluation input or configuration differs from its pinned revision.");
        var completion = providers.Create(profile, access);
        var request = new FlowCompletionRequest(input.NodeId, EvaluationProviderFactory.ReadPrompt(profile.Prompt),
            input.Input.Payload, EvaluationProviderFactory.ReadSchema(profile.Schema), profile.Model, profile.Provider, null);
        var started = Stopwatch.GetTimestamp();
        var output = await completion.CompleteAsync(request, cancellationToken);
        return new(profileHash, inputHash, output, Stopwatch.GetElapsedTime(started));
    }
}
