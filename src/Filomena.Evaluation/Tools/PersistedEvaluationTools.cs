using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Npgsql;
using System.Text;
using System.Text.Json.Serialization;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Storage;

namespace Filomena.Evaluation.Tools;

public sealed record PersistedToolResponse(int ContractVersion, bool Success, string PersistenceStatus,
    JsonElement? Result, string? ErrorCode);

public sealed class PersistedEvaluationTools(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64
    };

    // Authority and datasource come from the host, never the request envelope.
    // No completion provider, runtime or sender is reachable from these operations.
    public async Task<PersistedToolResponse> InvokeAsync(EvaluationAccess access, string request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(access);
        var attempted = false;
        PersistedToolResponse Error(string code) => new(1, false, attempted ? "unconfirmed" : "not_attempted", null, code);
        if (request is null) return Error("invalid_request");
        if (Encoding.UTF8.GetByteCount(request) > 1_048_576) return Error("request_too_large");
        try
        {
            token.ThrowIfCancellationRequested();
            using var parsed = JsonDocument.Parse(request, new JsonDocumentOptions { MaxDepth = 64 });
            RevisionHasher.ValidateJson(parsed.RootElement, 0);
            var input = Read<Request>(parsed.RootElement);
            if (input.ContractVersion != 1) return Error("unsupported_contract_version");
            if (string.IsNullOrWhiteSpace(input.Operation) || input.Payload.ValueKind != JsonValueKind.Object)
                return Error("invalid_request");
            var store = new PostgresEvaluationStore(source);
            var coordinator = new ExperimentCoordinator(source);
            switch (input.Operation)
            {
                case "revision.import":
                    {
                        access.Demand(EvaluationOperation.Propose);
                        var document = Read<RevisionDocument>(input.Payload);
                        _ = RevisionHasher.Hash(document);
                        attempted = true;
                        return Success(await store.PutRevisionAsync(access, document, token), "confirmed");
                    }
                case "revision.show":
                    {
                        access.Demand(EvaluationOperation.ReadRevision);
                        var reference = Read<RevisionRef>(input.Payload);
                        var issues = new List<ValidationIssue>();
                        ManifestValidator.AddReferenceIssue(new(access.Scope, reference), access.Scope, "revision", issues);
                        if (issues.Count != 0) return Error("invalid_request");
                        attempted = true;
                        var document = await store.GetRevisionAsync(access, reference, token);
                        return document is null ? new(1, false, "read_only", null, "not_found") : Success(document, "read_only");
                    }
                case "experiment.plan":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var plan = Read<PlanInput>(input.Payload);
                        if (plan.ExperimentId == Guid.Empty || plan.Manifest is null || plan.Cases is null) return Error("invalid_request");
                        access.DemandScope(plan.Manifest.Scope);
                        _ = ExperimentSchedule.Create(plan.Manifest, plan.Cases);
                        attempted = true;
                        return Success(await coordinator.PlanAsync(access, plan.ExperimentId, plan.Manifest, plan.Cases, token), "confirmed");
                    }
                case "experiment.cancel":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var cancel = Read<ExperimentInput>(input.Payload);
                        if (cancel.ExperimentId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        await coordinator.CancelAsync(access, cancel.ExperimentId, token);
                        return Success(new { cancel.ExperimentId, cancellationRequested = true }, "confirmed");
                    }
                default: return Error("unsupported_operation");
            }
        }
        catch (UnauthorizedAccessException) { return Error("unauthorized"); }
        catch (OperationCanceledException) { return Error("cancelled"); }
        catch (NpgsqlException) { return Error("storage_failure"); }
        catch (TimeoutException) { return Error("storage_failure"); }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return Error(attempted ? "operation_unconfirmed" : "invalid_request");
        }
    }

    private static T Read<T>(JsonElement input) where T : class => input.Deserialize<T>(Json) ?? throw new InvalidDataException();
    private static PersistedToolResponse Success(object result, string status) => new(1, true, status, JsonSerializer.SerializeToElement(result, Json), null);
    private sealed record Request([property: JsonRequired] int ContractVersion,
        [property: JsonRequired] string Operation, [property: JsonRequired] JsonElement Payload);
    private sealed record PlanInput(Guid ExperimentId, ExperimentManifest Manifest, IReadOnlyList<ScopedRevisionRef> Cases);
    private sealed record ExperimentInput(Guid ExperimentId);
}
