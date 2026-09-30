using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Npgsql;
using System.Text;
using System.Text.Json.Serialization;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Storage;
using Filomena.Evaluation.Comparison;

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
                case "issues.history":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var history = Read<HistoryInput>(input.Payload);
                        var ids = IssueHistoryReader.Validate(history.ReportIds);
                        attempted = true;
                        var result = await new IssueHistoryReader(source).ReadAsync(access, ids, token);
                        return result is null ? new(1, false, "read_only", null, "not_found") : Success(result, "read_only");
                    }
                case "artifact.import":
                    {
                        access.Demand(EvaluationOperation.RegisterCase);
                        var artifact = Read<ArtifactInput>(input.Payload);
                        if (artifact.Content is null) return Error("invalid_request");
                        var document = new EvidenceArtifactDocument(artifact.Kind, artifact.IsHoldout, artifact.Content);
                        PostgresEvaluationStore.ValidateArtifactImport(access, document);
                        attempted = true;
                        return Success(await store.RegisterArtifactAsync(access, document, token), "confirmed");
                    }
                case "case.import":
                    {
                        access.Demand(EvaluationOperation.RegisterCase);
                        var document = Read<EvidenceCaseDocument>(input.Payload);
                        PostgresEvaluationStore.ValidateCaseImport(access, document);
                        attempted = true;
                        return Success(new { caseId = await store.RegisterCaseAsync(access, document, token) }, "confirmed");
                    }
                case "attempt.charge.declare":
                    {
                        access.Demand(EvaluationOperation.Operate);
                        var declaration = Read<AttemptChargeDeclaration>(input.Payload);
                        AttemptChargePolicy.Validate(declaration);
                        attempted = true;
                        return Success(await new AttemptChargeStore(source).DeclareAsync(access, declaration, token), "confirmed");
                    }
                case "experiment.budget.approve":
                    {
                        access.Demand(EvaluationOperation.Operate);
                        var approval = Read<MonetaryBudgetApprovalRequest>(input.Payload);
                        MonetaryBudgetContract.Validate(approval);
                        attempted = true;
                        return Success(await new MonetaryBudgetStore(source).ApproveAsync(access, approval, token), "confirmed");
                    }
                case "experiment.budget.show":
                    {
                        access.Demand(EvaluationOperation.ReadExperiment);
                        var query = Read<ExperimentInput>(input.Payload);
                        if (query.ExperimentId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        var result = await new MonetaryBudgetStore(source).ReadAsync(access, query.ExperimentId, token);
                        return result is null ? new(1, false, "read_only", null, "not_found") : Success(result, "read_only");
                    }
                case "experiment.accounting":
                    {
                        access.Demand(EvaluationOperation.ReadExperiment);
                        var query = Read<ExperimentInput>(input.Payload);
                        if (query.ExperimentId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        var result = await new BudgetExposureReader(source).ReadAsync(access, query.ExperimentId, token);
                        return result is null ? new(1, false, "read_only", null, "not_found") : Success(result, "read_only");
                    }
                case "bundle.export":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var query = Read<ReportInput>(input.Payload);
                        if (query.ReportId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        var report = await new ComparisonReportStore(source).ShowAsync(access, query.ReportId, token);
                        return report is null ? new(1, false, "read_only", null, "not_found")
                            : Success(ComparisonBundleExporter.Create(access, report), "read_only");
                    }
                case "issues.list":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var query = Read<ReportInput>(input.Payload);
                        if (query.ReportId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        var report = await new ComparisonReportStore(source).ShowAsync(access, query.ReportId, token);
                        return report is null ? new(1, false, "read_only", null, "not_found")
                            : Success(IssueSummaryProjection.Create(report), "read_only");
                    }
                case "comparison.save":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var save = ComparisonReportStore.Validate(Read<ComparisonReportSaveRequest>(input.Payload));
                        attempted = true;
                        return Success(await new ComparisonReportStore(source).SaveAsync(access, save, token), "confirmed");
                    }
                case "comparison.show":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var show = Read<ReportInput>(input.Payload);
                        if (show.ReportId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        var result = await new ComparisonReportStore(source).ShowAsync(access, show.ReportId, token);
                        return result is null ? new(1, false, "read_only", null, "not_found") : Success(result, "read_only");
                    }
                case "experiment.compare":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var query = Read<ExperimentComparisonRequest>(input.Payload);
                        ComparisonPlan.Validate(query);
                        attempted = true;
                        var result = await new ExperimentComparisonReader(source).ReadAsync(access, query, token);
                        return result is null ? new(1, false, "read_only", null, "not_found") : Success(result, "read_only");
                    }
                case "trial.assessment":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var query = Read<TrialAssessmentRequest>(input.Payload);
                        TrialAssessmentReader.Validate(query);
                        attempted = true;
                        var result = await new TrialAssessmentReader(source).ReadAsync(access, query, token);
                        return result is null ? new(1, false, "read_only", null, "not_found") : Success(result, "read_only");
                    }
                case "trial.measurement":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var measurement = Read<TrialInput>(input.Payload);
                        if (measurement.ExperimentId == Guid.Empty || measurement.TrialId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        var result = await new TrialMeasurementReader(source).ReadAsync(access, measurement.ExperimentId, measurement.TrialId, token);
                        return result is null ? new(1, false, "read_only", null, "not_found") : Success(result, "read_only");
                    }
                case "case.bind_input":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var binding = Read<CaseInputBinding>(input.Payload);
                        CaseInputBindingStore.Validate(binding);
                        attempted = true;
                        return Success(await new CaseInputBindingStore(source).AddAsync(access, binding, token), "confirmed");
                    }
                case "decision.list":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var page = Read<DecisionPageInput>(input.Payload);
                        DecisionHistoryReader.ValidatePage(page.Limit, page.After, page.ExperimentId);
                        attempted = true;
                        return Success(await new DecisionHistoryReader(source).ReadAsync(access,
                            page.Limit, page.After, page.ExperimentId, token), "read_only");
                    }
                case "decision.add":
                    {
                        access.Demand(EvaluationOperation.Evaluate);
                        var decision = InvestigationHistory.Validate(Read<InvestigationDecision>(input.Payload));
                        attempted = true;
                        return Success(await new InvestigationHistory(source).AddAsync(access, decision, token), "confirmed");
                    }
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
                        access.Demand(EvaluationOperation.CancelExperiment);
                        var cancel = Read<ExperimentInput>(input.Payload);
                        if (cancel.ExperimentId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        await coordinator.CancelAsync(access, cancel.ExperimentId, token);
                        return Success(new { cancel.ExperimentId, cancellationRequested = true }, "confirmed");
                    }
                case "experiment.quarantine":
                    {
                        access.Demand(EvaluationOperation.Operate);
                        var recovery = Read<RecoveryQuarantineRequest>(input.Payload);
                        RecoveryQuarantine.Validate(recovery);
                        attempted = true;
                        return Success(await new ExperimentRecovery(source).QuarantineAsync(access, recovery, token), "confirmed");
                    }
                case "case.list":
                    {
                        access.Demand(EvaluationOperation.ReadArtifact);
                        var page = Read<CasePageInput>(input.Payload);
                        PostgresEvaluationStore.ValidateCasePage(page.Limit, page.After);
                        attempted = true;
                        return Success(await store.ListCasesAsync(access, page.Limit, page.After, token), "read_only");
                    }
                case "assessment.add":
                    {
                        access.Demand(EvaluationOperation.AppendAssessment);
                        var assessment = PostgresEvaluationStore.ValidateAssessment(Read<Assessment>(input.Payload));
                        attempted = true;
                        var id = await store.AppendAssessmentAsync(access, assessment, token);
                        return Success(new { assessmentId = id }, "confirmed");
                    }
                case "experiment.status":
                    {
                        access.Demand(EvaluationOperation.ReadExperiment);
                        var status = Read<ExperimentInput>(input.Payload);
                        if (status.ExperimentId == Guid.Empty) return Error("invalid_request");
                        attempted = true;
                        var result = await new ExperimentStatusReader(source).ReadAsync(access, status.ExperimentId, token);
                        return result is null ? new(1, false, "read_only", null, "not_found") : Success(result, "read_only");
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
    private sealed record ArtifactInput([property: JsonRequired] string Kind,
        [property: JsonRequired] bool IsHoldout, [property: JsonRequired] byte[] Content);
    private sealed record HistoryInput([property: JsonRequired] IReadOnlyList<Guid> ReportIds);
    private sealed record PlanInput(Guid ExperimentId, ExperimentManifest Manifest, IReadOnlyList<ScopedRevisionRef> Cases);
    private sealed record ExperimentInput(Guid ExperimentId);
    private sealed record CasePageInput(int Limit, Guid? After);
    private sealed record DecisionPageInput(int Limit, Guid? After, Guid? ExperimentId);
    private sealed record TrialInput(Guid ExperimentId, Guid TrialId);
    private sealed record ReportInput(Guid ReportId);
}
