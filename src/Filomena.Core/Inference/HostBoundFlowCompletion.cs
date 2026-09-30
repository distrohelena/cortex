using Filomena.Core.Flows;

namespace Filomena.Core.Inference;

public sealed class HostBoundFlowCompletion : IFlowCompletion
{
    private readonly HostInferenceAuthority authority;
    private readonly HostInferenceGrant grant;
    private readonly InferenceScope scope;
    private readonly string principalRef;
    private readonly HostInferenceProfile profile;
    private readonly InferenceCorrelation correlation;
    private readonly TimeProvider timeProvider;
    private readonly Action<InferenceAttemptObservation>? observe;
    private int observationFailed;

    internal HostBoundFlowCompletion(HostInferenceAuthority authority, HostInferenceGrant grant, InferenceScope scope,
        string principalRef, HostInferenceProfile profile, InferenceCorrelation correlation,
        TimeProvider timeProvider, Action<InferenceAttemptObservation>? observe)
    {
        this.authority = authority;
        this.grant = grant;
        this.scope = scope;
        this.principalRef = principalRef;
        this.profile = profile;
        this.correlation = correlation;
        this.timeProvider = timeProvider;
        this.observe = observe;
    }

    public bool ObservationFailed => Volatile.Read(ref observationFailed) != 0;

    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        var attemptId = Guid.NewGuid().ToString("N");
        var started = timeProvider.GetTimestamp();
        var invoked = false;
        var stage = InferenceAttemptStage.DeniedBeforeInvocation;
        var code = "request_invalid";
        try
        {
            token.ThrowIfCancellationRequested();
            profile.ValidateRequest(request);
            var copied = request with { Inputs = request.Inputs.Clone(), ResponseSchema = request.ResponseSchema?.Clone() };
            authority.Admit(grant, scope, token);
            invoked = true;
            var output = await profile.Adapter.CompleteAsync(copied, token).ConfigureAwait(false);
            if (output is null)
            {
                stage = InferenceAttemptStage.InvalidOutput;
                code = "null_output";
                throw new InvalidDataException(code);
            }
            if (profile.RuntimeOwnedValidation)
            {
                stage = InferenceAttemptStage.AdapterReturnedPendingValidation;
                code = "runtime_validation_pending";
            }
            else
            {
                try { profile.ValidateOutput!(output); }
                catch (Exception)
                {
                    stage = InferenceAttemptStage.InvalidOutput;
                    code = "schema_invalid";
                    throw new InvalidDataException(code);
                }
                stage = InferenceAttemptStage.Completed;
                code = "validated";
            }
            return output;
        }
        catch (OperationCanceledException)
        {
            stage = invoked ? InferenceAttemptStage.CancelledAfterInvocation : InferenceAttemptStage.CancelledBeforeInvocation;
            code = invoked ? "adapter_outcome_unknown" : "cancelled_before_invocation";
            throw new OperationCanceledException(code, token);
        }
        catch (InferenceDeniedException error) when (!invoked)
        {
            code = error.Code;
            throw;
        }
        catch (Exception error) when (stage != InferenceAttemptStage.InvalidOutput)
        {
            stage = invoked ? InferenceAttemptStage.FailedAfterInvocation : InferenceAttemptStage.DeniedBeforeInvocation;
            code = invoked ? "adapter_outcome_unknown" : "request_invalid";
            // Do not attach an inner provider exception containing prompts, endpoints or secrets.
            throw error switch
            {
                HttpRequestException => new HttpRequestException(code),
                TimeoutException => new TimeoutException(code),
                _ => new InvalidOperationException(code)
            };
        }
        finally
        {
            try
            {
                observe?.Invoke(new(scope.ProjectId, principalRef, correlation.RunId, correlation.StepId,
                    attemptId, correlation.ConfigurationRevision, profile.ProfileId, profile.Revision,
                    timeProvider.GetElapsedTime(started), stage, code,
                    profile.ExpectedRequest.Provider, profile.ExpectedRequest.Model));
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref observationFailed, 1);
                authority.RecordObservationFailure();
            }
        }
    }
}
