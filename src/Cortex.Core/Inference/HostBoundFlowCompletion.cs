using Cortex.Core.Flows;

namespace Cortex.Core.Inference;

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
        var diagnostics = FlowDiagnosticContext.Begin(request, profile.DiagnosticOptions);
        var attemptId = diagnostics.AttemptId;
        var started = timeProvider.GetTimestamp();
        var invoked = false;
        var stage = InferenceAttemptStage.DeniedBeforeInvocation;
        var code = "request_invalid";
        Exception? failure = null;
        try
        {
            token.ThrowIfCancellationRequested();
            profile.ValidateRequest(request);
            var copied = request with { Inputs = request.Inputs.Clone(), ResponseSchema = request.ResponseSchema?.Clone(), Diagnostics = diagnostics };
            authority.Admit(grant, scope, token);
            invoked = true;
            var output = await profile.Adapter.CompleteAsync(copied, token).ConfigureAwait(false);
            if (output is null)
            {
                stage = InferenceAttemptStage.InvalidOutput;
                code = "null_output";
                var invalid = new InvalidDataException(code);
                diagnostics.ReportFailure(invalid, FlowFailureCategory.Validation, FlowFailureOrigin.Runtime);
                FlowDiagnostics.Attach(invalid, diagnostics.FailureFor(invalid));
                throw invalid;
            }
            diagnostics.CaptureResponse(output);
            if (profile.RuntimeOwnedValidation)
            {
                stage = InferenceAttemptStage.AdapterReturnedPendingValidation;
                code = "runtime_validation_pending";
            }
            else
            {
                try { profile.ValidateOutput!(output); }
                catch (Exception error)
                {
                    stage = InferenceAttemptStage.InvalidOutput;
                    code = "schema_invalid";
                    var diagnostic = FlowDiagnostics.Create(error, request.NodeId, null, FlowFailureOrigin.HostValidation,
                        error is System.Text.Json.JsonException ? FlowFailureCategory.Parsing : FlowFailureCategory.Validation);
                    var sanitized = new InvalidDataException(code);
                    FlowDiagnostics.Attach(sanitized, diagnostic);
                    throw sanitized;
                }
                stage = InferenceAttemptStage.Completed;
                code = "validated";
            }
            return output;
        }
        catch (OperationCanceledException error)
        {
            stage = invoked ? InferenceAttemptStage.CancelledAfterInvocation : InferenceAttemptStage.CancelledBeforeInvocation;
            code = invoked ? "adapter_outcome_unknown" : "cancelled_before_invocation";
            failure = error;
            var sanitized = new OperationCanceledException(code, token);
            FlowDiagnostics.Attach(sanitized, FlowDiagnostics.Create(error, request.NodeId, null,
                invoked ? FlowFailureOrigin.Adapter : FlowFailureOrigin.Runtime));
            throw sanitized;
        }
        catch (InferenceDeniedException error) when (!invoked)
        {
            code = error.Code;
            failure = error;
            FlowDiagnostics.Attach(error, FlowDiagnostics.Create(error, request.NodeId, null, FlowFailureOrigin.Runtime));
            throw;
        }
        catch (Exception error)
        {
            failure = error;
            if (stage == InferenceAttemptStage.InvalidOutput) throw;
            stage = invoked ? InferenceAttemptStage.FailedAfterInvocation : InferenceAttemptStage.DeniedBeforeInvocation;
            code = invoked ? "adapter_outcome_unknown" : "request_invalid";
            // Keep existing public exception types/messages safe. The original is available through diagnostics,
            // not InnerException/ToString(), which callers commonly write to untrusted logs.
            Exception sanitized = error switch
            {
                HttpRequestException http => new HttpRequestException(code, null, http.StatusCode),
                TimeoutException => new TimeoutException(code),
                _ => new InvalidOperationException(code)
            };
            FlowDiagnostics.Attach(sanitized, invoked ? diagnostics.FailureFor(error) :
                FlowDiagnostics.Create(error, request.NodeId, null, FlowFailureOrigin.Runtime));
            throw sanitized;
        }
        finally
        {
            var diagnosticAttempt = diagnostics.Finish(code, failure, invoked ? FlowFailureOrigin.Adapter : FlowFailureOrigin.Runtime);
            try
            {
                observe?.Invoke(new(scope.ProjectId, principalRef, correlation.RunId, correlation.StepId,
                    attemptId, correlation.ConfigurationRevision, profile.ProfileId, profile.Revision,
                    timeProvider.GetElapsedTime(started), stage, code,
                    profile.ExpectedRequest.Provider, profile.ExpectedRequest.Model)
                { Usage = diagnosticAttempt.Usage, Failure = diagnosticAttempt.Failure,
                    CapturedResponse = diagnosticAttempt.CapturedResponse, CaptureStatus = diagnosticAttempt.CaptureStatus });
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref observationFailed, 1);
                authority.RecordObservationFailure();
            }
        }
    }
}
