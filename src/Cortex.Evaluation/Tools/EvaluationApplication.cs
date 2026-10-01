using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Npgsql;

namespace Cortex.Evaluation.Tools;

// The host supplies access separately for each invocation. No environment,
// credential, endpoint or role is resolved from the request envelope.
public sealed class EvaluationApplication
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64
    };
    private readonly PersistedEvaluationTools tools;
    private readonly CompletionTrialExecutor? executor;
    private readonly EvaluationEndpointSettings[] endpoints = [];

    public EvaluationApplication(PersistedEvaluationTools tools)
    {
        this.tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public EvaluationApplication(NpgsqlDataSource source, IReadOnlyList<EvaluationEndpointSettings> endpoints)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(endpoints);
        if (endpoints.Count == 0 || endpoints.Any(endpoint => endpoint is null))
            throw new ArgumentException("Explicit host endpoints are required.", nameof(endpoints));
        this.endpoints = endpoints.ToArray();
        tools = new(source);
        executor = new(source);
    }

    public async Task<PersistedToolResponse> InvokeAsync(EvaluationAccess access, string request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(access);
        var invoked = false;
        PersistedToolResponse Error(string code) => new(1, false, invoked ? "unconfirmed" : "not_attempted", null, code);
        if (request is null) return Error("invalid_request");
        if (Encoding.UTF8.GetByteCount(request) > 1_048_576) return Error("request_too_large");
        try
        {
            token.ThrowIfCancellationRequested();
            using var parsed = JsonDocument.Parse(request, new JsonDocumentOptions { MaxDepth = 64 });
            RevisionHasher.ValidateJson(parsed.RootElement, 0);
            var input = parsed.RootElement.Deserialize<Request>(Json) ?? throw new InvalidDataException();
            if (input.ContractVersion != 1) return Error("unsupported_contract_version");
            if (string.IsNullOrWhiteSpace(input.Operation) || input.Payload.ValueKind != JsonValueKind.Object)
                return Error("invalid_request");
            if (input.Operation != "experiment.run")
            {
                invoked = true;
                return await tools.InvokeAsync(access, request, token);
            }

            access.Demand(EvaluationOperation.Evaluate);
            var run = input.Payload.Deserialize<RunInput>(Json) ?? throw new InvalidDataException();
            if (run.ExperimentId == Guid.Empty) return Error("invalid_request");
            if (executor is null) return Error("execution_binding_required");
            invoked = true;
            // One claim only. The existing executor owns reservations, fencing and
            // failure quarantine. A command retry never replays a supplied claim.
            var trial = await executor.ExecuteNextAsync(access, run.ExperimentId, endpoints, token);
            return new(1, true, "confirmed", JsonSerializer.SerializeToElement(new
            {
                status = trial is null ? "no_pending_trial" : "trial_completed",
                experimentId = run.ExperimentId,
                trial
            }, Json), null);
        }
        catch (CompletionTrialExecutionException error)
        {
            return new(1, false, "unconfirmed", JsonSerializer.SerializeToElement(new
            {
                trialId = error.TrialId,
                stopRecorded = error.StopRecorded,
                reconciliationRequired = true
            }, Json), error.Code);
        }
        catch (UnauthorizedAccessException) { return Error("unauthorized"); }
        catch (OperationCanceledException) { return Error("cancelled"); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or IOException)
        {
            return Error("operation_unconfirmed");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return Error(invoked ? "operation_unconfirmed" : "invalid_request");
        }
    }

    private sealed record Request([property: JsonRequired] int ContractVersion,
        [property: JsonRequired] string Operation, [property: JsonRequired] JsonElement Payload);
    private sealed record RunInput([property: JsonRequired] Guid ExperimentId);
}
