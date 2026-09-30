using System.Text.Json;
using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class ComparisonPlanTests
{
    [Fact]
    public void Plan_requires_original_input_order_and_exact_stored_schedule()
    {
        var f = Fixture();
        Assert.Equal(2, ComparisonPlan.Resolve(f.Access, f.Request, f.Manifest, f.Hash, f.Rows).Count);
        Assert.Throws<InvalidDataException>(() => ComparisonPlan.Resolve(f.Access,
            f.Request with { Inputs = f.Request.Inputs.Reverse().ToArray() }, f.Manifest, f.Hash, f.Rows));
        Assert.Throws<InvalidDataException>(() => ComparisonPlan.Resolve(f.Access, f.Request, f.Manifest, f.Hash, f.Rows[..1]));
        Assert.Throws<InvalidDataException>(() => ComparisonPlan.Resolve(f.Access, f.Request, f.Manifest, f.Hash,
            [f.Rows[0], f.Rows[1] with { TrialKey = new string('f', 64) }]));
    }

    [Fact]
    public void Duplicated_identity_wrong_revision_and_foreign_scope_cannot_form_plan()
    {
        var f = Fixture();
        Assert.Throws<InvalidDataException>(() => ComparisonPlan.Resolve(f.Access, f.Request, f.Manifest, f.Hash,
            [f.Rows[0], f.Rows[1] with { TrialId = f.Rows[0].TrialId }]));
        Assert.Throws<InvalidDataException>(() => ComparisonPlan.Resolve(f.Access, f.Request, f.Manifest, f.Hash,
            [f.Rows[0] with { Input = f.Rows[1].Input }, f.Rows[1]]));
        Assert.Throws<UnauthorizedAccessException>(() => ComparisonPlan.Resolve(
            EvaluationAccess.FromHost("other", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator),
            f.Request, f.Manifest, f.Hash, f.Rows));
    }

    [Fact]
    public void Duplicate_input_binding_or_revision_is_rejected()
    {
        var f = Fixture();
        Assert.Throws<ArgumentException>(() => ComparisonPlan.Validate(f.Request with
        {
            Inputs = [f.Request.Inputs[0], f.Request.Inputs[1] with { BindingId = f.Request.Inputs[0].BindingId }]
        }));
        Assert.Throws<ArgumentException>(() => ComparisonPlan.Validate(f.Request with
        {
            Inputs = [f.Request.Inputs[0], f.Request.Inputs[1] with { InputRevision = f.Request.Inputs[0].InputRevision }]
        }));
    }

    [Fact]
    public void Explicit_price_binding_is_accepted_but_duplicate_attempt_binding_is_not()
    {
        var f = Fixture();
        var price = new AttemptPriceBinding(Guid.NewGuid(), f.Request.PolicyRevision);
        ComparisonPlan.Validate(f.Request with { Prices = [price] });
        Assert.Throws<ArgumentException>(() => ComparisonPlan.Validate(f.Request with { Prices = [price, price] }));
    }

    private static (EvaluationAccess Access, ExperimentComparisonRequest Request, ExperimentManifest Manifest,
        string Hash, StoredComparisonTrial[] Rows) Fixture()
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var access = EvaluationAccess.FromHost("comparison", scope, EvaluationRole.Evaluator);
        ScopedRevisionRef Ref(char hash) => new(scope, new(Guid.NewGuid(), new string(hash, 64)));
        var baseline = Ref('a');
        var inputs = new[] { Ref('b'), Ref('c') };
        var manifest = new ExperimentManifest(scope, baseline, baseline, baseline, baseline, baseline,
            [baseline], 1, 1, CandidateOrder.Interleaved, new(2, 1000, 1000, 1), new(1, 1, null));
        var request = new ExperimentComparisonRequest(Guid.NewGuid(), baseline.Revision,
            inputs.Select(i => new ComparisonInputBinding(i.Revision, Guid.NewGuid())).ToArray());
        var hash = RevisionHasher.Hash(new RevisionDocument(1, "experiment-plan",
            JsonSerializer.SerializeToElement(new { manifest, cases = inputs })));
        var rows = ExperimentSchedule.Create(manifest, inputs).Select((t, ordinal) =>
            new StoredComparisonTrial(Guid.NewGuid(), ordinal, t.TrialKey, t.Candidate.Revision, t.Case.Revision, t.Repetition)).ToArray();
        return (access, request, manifest, hash, rows);
    }
}
