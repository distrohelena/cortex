using System.Text;
using System.Text.Json;
using Cortex.Evaluation.Comparison;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Cortex.Evaluation.Storage;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class TrialAssessmentPostgresTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Selection_uses_explicit_binding_pinned_evaluator_and_actual_trial_result_evidence()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires isolated PostgreSQL migrated through 005.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var token = TestContext.Current.CancellationToken;
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var evaluator = EvaluationAccess.FromHost("assessment", scope, EvaluationRole.Evaluator);
        var proposer = EvaluationAccess.FromHost("assessment", scope, EvaluationRole.Proposer);
        var store = new PostgresEvaluationStore(source);
        var fixture = CaseInputBindingTests.Fixture();
        var input = await store.PutRevisionAsync(proposer, fixture.Input, token);
        var provenance = await store.PutRevisionAsync(proposer, fixture.Provenance, token);
        var reference = new ScopedRevisionRef(scope, input);
        var experiment = Guid.NewGuid();
        var manifest = new ExperimentManifest(scope, reference, reference, reference, reference, reference,
            [reference], 1, 1, CandidateOrder.Interleaved, new(2, 1000, 1000, 1), new(1, 1, null));
        var coordinator = new ExperimentCoordinator(source);
        var plan = await coordinator.PlanAsync(evaluator, experiment, manifest, [reference], token);
        await MonetaryTestApproval.GrantAsync(source, scope, plan, manifest.Budgets.MaxCost, token);
        var claim = await coordinator.ClaimNextAsync(evaluator, experiment, Guid.NewGuid(), token);
        Assert.NotNull(claim);
        var caseArtifact = await store.RegisterArtifactAsync(evaluator, new("case", true, Encoding.UTF8.GetBytes("private fixture")), token);
        var caseId = await store.RegisterCaseAsync(evaluator, new("case", caseArtifact), token);
        var binding = new CaseInputBinding(Guid.NewGuid(), caseId, caseArtifact, input, provenance);
        await new CaseInputBindingStore(source).AddAsync(evaluator, binding, token);
        var request = new TrialAssessmentRequest(experiment, claim.TrialId, binding.BindingId, "correctness");
        var reader = new TrialAssessmentReader(source);
        Assert.Equal("missing", (await reader.ReadAsync(evaluator, request, token))!.Selection.Status);
        Assert.Null(await reader.ReadAsync(evaluator, request with { BindingId = Guid.NewGuid() }, token));
        foreach (var foreign in new[] { new EvaluationScope(scope.WorkspaceId, Guid.NewGuid()), new EvaluationScope(Guid.NewGuid(), scope.ProjectId) })
            Assert.Null(await reader.ReadAsync(EvaluationAccess.FromHost("assessment", foreign, EvaluationRole.Evaluator), request, token));
        var ledger = new AttemptLedger(source, evaluator, experiment, claim.TrialId, claim.Owner, claim.Generation, new(100, 100, 0.01m), "BRL");
        var attempt = await ledger.BeginAsync(new("openai", new string('a', 64), 20), token);
        await ledger.FinishAsync(attempt, new("response_received", TimeSpan.FromMilliseconds(10),
            new(200, "fixture", null, null, "available", new(10, 10, 0, 0), null), null), token);
        var output = new CompletionEvaluationResult(input.Sha256, input.Sha256, "private response", TimeSpan.FromMilliseconds(15));
        var outputRef = await store.RegisterArtifactAsync(evaluator, output.ToArtifact(true), token);
        await coordinator.CompleteAsync(evaluator, experiment, claim, output, outputRef, token);
        var assessment = new Assessment(caseId, claim.TrialId, "correctness", input, "human", AssessmentOutcome.Pass, null, [], null);
        var old = await store.AppendAssessmentAsync(evaluator, assessment, token);
        Assert.Equal("evidence_unavailable", (await reader.ReadAsync(evaluator, request, token))!.Selection.Status);
        var current = await store.AppendAssessmentAsync(evaluator, assessment with
        {
            Outcome = AssessmentOutcome.Fail,
            Severity = 4,
            Evidence = [outputRef],
            SupersedesAssessmentId = old
        }, token);
        var resolved = (await reader.ReadAsync(evaluator, request, token))!;
        Assert.Equal("resolved", resolved.Selection.Status);
        Assert.Equal("fail", resolved.Selection.Outcome);
        Assert.Equal(current, resolved.Selection.AssessmentId);
        Assert.Equal(input, resolved.EvaluatorRevision);
        Assert.Equal("evaluator_declaration", resolved.BindingOrigin);
        await store.RedactArtifactAsync(EvaluationAccess.FromHost("operator", scope, EvaluationRole.Operator), outputRef.Id, "fixture", token);
        Assert.Equal("evidence_unavailable", (await reader.ReadAsync(evaluator, request, token))!.Selection.Status);
        await store.AppendAssessmentAsync(evaluator, assessment with { Evidence = [outputRef], SupersedesAssessmentId = old }, token);
        var ambiguous = (await reader.ReadAsync(evaluator, request, token))!;
        Assert.Equal("ambiguous", ambiguous.Selection.Status);
        Assert.Null(ambiguous.Selection.Outcome);
        await store.RedactArtifactAsync(EvaluationAccess.FromHost("operator", scope, EvaluationRole.Operator), caseArtifact.Id, "fixture", token);
        Assert.Null(await reader.ReadAsync(evaluator, request, token));
    }
}
