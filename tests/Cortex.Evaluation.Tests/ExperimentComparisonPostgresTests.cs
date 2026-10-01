using System.Text;
using System.Text.Json;
using Cortex.Evaluation.Comparison;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Cortex.Evaluation.Storage;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class ExperimentComparisonPostgresTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Scoped_comparison_uses_full_schedule_real_result_and_explicit_binding_without_writes()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires disposable PostgreSQL migrated through 006.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var token = TestContext.Current.CancellationToken;
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var evaluator = EvaluationAccess.FromHost("comparison", scope, EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("comparison", scope, EvaluationRole.Proposer);
        var store = new PostgresEvaluationStore(source);
        var fixture = CaseInputBindingTests.Fixture();
        var input = await store.PutRevisionAsync(proposer, fixture.Input, token);
        var provenance = await store.PutRevisionAsync(proposer, fixture.Provenance, token);
        var policy = new TrialComparisonPolicy(1, input, [new("correctness", true)]);
        var policyRef = await store.PutRevisionAsync(proposer, policy.ToRevisionDocument(), token);
        var reference = new ScopedRevisionRef(scope, input);
        var experiment = Guid.NewGuid();
        var manifest = new ExperimentManifest(scope, reference, reference, reference, reference, reference,
            [reference], 1, 1, CandidateOrder.Interleaved, new(2, 1000, 1000, 1), new(1, 1, null));
        var coordinator = new ExperimentCoordinator(source);
        var plan = await coordinator.PlanAsync(evaluator, experiment, manifest, [reference], token);
        await MonetaryTestApproval.GrantAsync(source, scope, plan, manifest.Budgets.MaxCost, token);
        var claim = await coordinator.ClaimNextAsync(evaluator, experiment, Guid.NewGuid(), token);
        Assert.NotNull(claim);
        var caseArtifact = await store.RegisterArtifactAsync(evaluator, new("case", true, Encoding.UTF8.GetBytes("private comparison fixture")), token);
        var caseId = await store.RegisterCaseAsync(evaluator, new("case", caseArtifact), token);
        var binding = new CaseInputBinding(Guid.NewGuid(), caseId, caseArtifact, input, provenance);
        await new CaseInputBindingStore(source).AddAsync(evaluator, binding, token);
        var request = new ExperimentComparisonRequest(experiment, policyRef, [new(input, binding.BindingId)]);
        var reader = new ExperimentComparisonReader(source);
        var incomplete = (await reader.ReadAsync(evaluator, request, token))!;
        Assert.Equal(plan.PlanSha256, incomplete.PlanSha256);
        Assert.False(Assert.Single(incomplete.Report.Statistics.Candidates).Eligible);
        Assert.Contains("execution_incomplete", Assert.Single(incomplete.Report.Trials).Failures);
        foreach (var foreign in new[] { new EvaluationScope(scope.WorkspaceId, Guid.NewGuid()), new EvaluationScope(Guid.NewGuid(), scope.ProjectId) })
            Assert.Null(await reader.ReadAsync(EvaluationAccess.FromHost("comparison", foreign, EvaluationRole.Evaluator), request, token));
        Assert.Null(await reader.ReadAsync(evaluator, request with { Inputs = [new(input, Guid.NewGuid())] }, token));

        var ledger = new AttemptLedger(source, evaluator, experiment, claim.TrialId, claim.Owner, claim.Generation, new(100, 100, 0.01m), "BRL");
        var attempt = await ledger.BeginAsync(new("openai", new string('a', 64), 20), token);
        await ledger.FinishAsync(attempt, new("response_received", TimeSpan.FromMilliseconds(10),
            new(200, "fixture", null, null, "available", new(10, 10, 0, 0), null), null), token);
        var output = new CompletionEvaluationResult(input.Sha256, input.Sha256, "private response", TimeSpan.FromMilliseconds(15));
        var outputRef = await store.RegisterArtifactAsync(evaluator, output.ToArtifact(true), token);
        await coordinator.CompleteAsync(evaluator, experiment, claim, output, outputRef, token);
        var assessment = await store.AppendAssessmentAsync(evaluator,
            new(caseId, claim.TrialId, "correctness", input, "human", AssessmentOutcome.Pass, null, [outputRef], null), token);
        var before = await State();
        var accountingReader = new BudgetExposureReader(source);
        var accounting = (await accountingReader.ReadAsync(EvaluationAccess.FromHost("operator", scope, EvaluationRole.Operator), experiment, token))!;
        Assert.False(accounting.Accounting.ReservationMismatch);
        Assert.Equal(10, accounting.Accounting.ObservedInputTokens);
        Assert.Equal(100, accounting.Accounting.ConservativeExposure.InputTokens);
        Assert.Equal(0.01m, accounting.Accounting.ReservedCost);
        Assert.Equal("not_reconciled_currency_and_billing_unavailable", accounting.Accounting.MonetaryStatus);
        Assert.Equal(before, await State());
        var snapshot = (await reader.ReadAsync(evaluator, request, token))!;
        var candidate = Assert.Single(snapshot.Report.Statistics.Candidates);
        Assert.True(candidate.Eligible);
        Assert.Equal(1, candidate.Observed);
        Assert.Equal(15, candidate.MedianMilliseconds);
        Assert.Null(candidate.MeanCost);
        Assert.Contains(assessment, Assert.Single(snapshot.Report.Trials).AssessmentIds);
        Assert.Equal(before, await State());
        Assert.DoesNotContain("private response", JsonSerializer.Serialize(snapshot));
        Assert.DoesNotContain("private comparison fixture", JsonSerializer.Serialize(snapshot));

        var terms = new AttemptPriceTerms(1, "openai", "fixture", null, new string('a', 64),
            DateTimeOffset.Parse("2020-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2100-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            0, 1000, "USD", "https://provider.example/pricing", DateTimeOffset.UtcNow, 1m, 1m, 5m, true);
        var priceRef = await store.PutRevisionAsync(proposer, terms.ToRevisionDocument(), token);
        var pricedRequest = request with { Prices = [new(attempt, priceRef)] };
        var priced = (await reader.ReadAsync(evaluator, pricedRequest, token))!;
        Assert.Equal(0.00006m, Assert.Single(priced.Report.Statistics.Candidates).MeanCost);
        Assert.Equal(priceRef, Assert.Single(priced.PriceProjections).PriceRevision);
        Assert.Equal("evaluator_declaration", Assert.Single(priced.PriceProjections).Origin);
        Assert.Null(Assert.Single(snapshot.Report.Statistics.Candidates).MeanCost);
        Assert.Equal(before, await State());
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(evaluator,
            request with { Prices = [new(Guid.NewGuid(), priceRef)] }, token));
        var missingPrice = new RevisionRef(Guid.NewGuid(), priceRef.Sha256);
        Assert.Null(await reader.ReadAsync(evaluator, request with { Prices = [new(attempt, missingPrice)] }, token));

        var reports = new ComparisonReportStore(source);
        var save = new ComparisonReportSaveRequest(Guid.NewGuid(), pricedRequest);
        var saved = await reports.SaveAsync(evaluator, save, token);
        Assert.False(saved.AlreadyRecorded);
        Assert.True((await reports.SaveAsync(evaluator, save, token)).AlreadyRecorded);
        Assert.Equal(before, await State());
        var shown = (await reports.ShowAsync(evaluator, save.ReportId, token))!;
        Assert.Equal("available", shown.Status);
        Assert.Equal(saved.Sha256, shown.Sha256);
        Assert.Equal(0.00006m, Assert.Single(shown.Snapshot!.Report.Statistics.Candidates).MeanCost);
        Assert.Contains(outputRef, shown.Snapshot.SourceEvidence);
        Assert.Contains(caseArtifact, shown.Snapshot.SourceEvidence);
        var issuesRequest = JsonSerializer.Serialize(new { contractVersion = 1, operation = "issues.list", payload = new { reportId = save.ReportId } });
        var issues = await new PersistedEvaluationTools(source).InvokeAsync(evaluator, issuesRequest, token);
        Assert.True(issues.Success);
        Assert.Equal("read_only", issues.PersistenceStatus);
        Assert.Equal("available", issues.Result!.Value.GetProperty("status").GetString());
        Assert.Equal(0, issues.Result.Value.GetProperty("items")[0].GetProperty("failedTrials").GetInt32());
        var secondSave = save with { ReportId = Guid.NewGuid() };
        await reports.SaveAsync(evaluator, secondSave, token);
        var historyRequest = JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            operation = "issues.history",
            payload = new { reportIds = new[] { save.ReportId, secondSave.ReportId } }
        });
        var history = await new PersistedEvaluationTools(source).InvokeAsync(evaluator, historyRequest, token);
        Assert.True(history.Success, history.ErrorCode);
        Assert.Equal("read_only", history.PersistenceStatus);
        Assert.Equal("available", history.Result!.Value.GetProperty("status").GetString());
        var historyGroup = Assert.Single(history.Result.Value.GetProperty("groups").EnumerateArray());
        Assert.Equal(1, historyGroup.GetProperty("uniqueTrials").GetInt32());
        Assert.Equal(1, historyGroup.GetProperty("items")[0].GetProperty("assessedTrials").GetInt32());
        Assert.DoesNotContain("private response", history.Result.Value.GetRawText());
        Assert.DoesNotContain("private comparison fixture", history.Result.Value.GetRawText());
        var exportRequest = JsonSerializer.Serialize(new { contractVersion = 1, operation = "bundle.export", payload = new { reportId = save.ReportId } });
        var export = await new PersistedEvaluationTools(source).InvokeAsync(evaluator, exportRequest, token);
        Assert.True(export.Success);
        Assert.Equal("available", export.Result!.Value.GetProperty("status").GetString());
        Assert.DoesNotContain("private response", export.Result.Value.GetRawText());
        Assert.DoesNotContain("private comparison fixture", export.Result.Value.GetRawText());
        Assert.Equal(before, await State());
        foreach (var foreign in new[] { new EvaluationScope(scope.WorkspaceId, Guid.NewGuid()), new EvaluationScope(Guid.NewGuid(), scope.ProjectId) })
        {
            Assert.Null(await reports.ShowAsync(EvaluationAccess.FromHost("comparison", foreign, EvaluationRole.Evaluator), save.ReportId, token));
            var foreignHistory = await new PersistedEvaluationTools(source).InvokeAsync(
                EvaluationAccess.FromHost("comparison", foreign, EvaluationRole.Evaluator), historyRequest, token);
            Assert.False(foreignHistory.Success);
            Assert.Equal("not_found", foreignHistory.ErrorCode);
            Assert.Null(foreignHistory.Result);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.SaveAsync(evaluator, save with { Comparison = request }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.SaveAsync(
            EvaluationAccess.FromHost("other-author", scope, EvaluationRole.Evaluator), save, token));

        var mismatchedPolicy = policy with { EvaluatorRevision = provenance };
        var mismatch = await store.PutRevisionAsync(proposer, mismatchedPolicy.ToRevisionDocument(), token);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(evaluator, request with { PolicyRevision = mismatch }, token));
        var operatorAccess = EvaluationAccess.FromHost("operator", scope, EvaluationRole.Operator);
        await store.RedactArtifactAsync(operatorAccess, outputRef.Id, "fixture", token);
        var withheld = (await reports.ShowAsync(evaluator, save.ReportId, token))!;
        Assert.Equal("evidence_unavailable", withheld.Status);
        Assert.Null(withheld.Snapshot);
        var hiddenIssues = await new PersistedEvaluationTools(source).InvokeAsync(evaluator, issuesRequest, token);
        Assert.Equal("evidence_unavailable", hiddenIssues.Result!.Value.GetProperty("status").GetString());
        Assert.Equal(0, hiddenIssues.Result.Value.GetProperty("items").GetArrayLength());
        var hiddenHistory = await new PersistedEvaluationTools(source).InvokeAsync(evaluator, historyRequest, token);
        Assert.Equal("partial", hiddenHistory.Result!.Value.GetProperty("status").GetString());
        Assert.Equal(0, hiddenHistory.Result.Value.GetProperty("groups").GetArrayLength());
        var hiddenExport = await new PersistedEvaluationTools(source).InvokeAsync(evaluator, exportRequest, token);
        Assert.Equal("evidence_unavailable", hiddenExport.Result!.Value.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, hiddenExport.Result.Value.GetProperty("document").ValueKind);
        Assert.True((await reports.SaveAsync(evaluator, save, token)).AlreadyRecorded); // receipt, not renewed content access
        var redacted = (await reader.ReadAsync(evaluator, request, token))!;
        Assert.False(Assert.Single(redacted.Report.Statistics.Candidates).Eligible);
        Assert.Contains("result_unavailable", Assert.Single(redacted.Report.Trials).Failures);
        await store.RedactArtifactAsync(operatorAccess, caseArtifact.Id, "fixture", token);
        Assert.Null(await reader.ReadAsync(evaluator, request, token));

        async Task<string> State()
        {
            await using var command = source.CreateCommand("""
                SELECT jsonb_build_object(
                  'experiment',(SELECT to_jsonb(e) FROM evaluation.experiments e WHERE e.workspace_id=@w AND e.project_id=@p AND e.id=@e),
                  'trials',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM evaluation.trials t WHERE t.workspace_id=@w AND t.project_id=@p AND t.experiment_id=@e),
                  'attempts',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id) FROM evaluation.attempts a WHERE a.workspace_id=@w AND a.project_id=@p)
                )::text
                """);
            command.Parameters.AddWithValue("w", scope.WorkspaceId);
            command.Parameters.AddWithValue("p", scope.ProjectId);
            command.Parameters.AddWithValue("e", experiment);
            return (string)(await command.ExecuteScalarAsync(token))!;
        }
    }
}
