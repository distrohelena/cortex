using Filomena.Evaluation.Contracts;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class AccessTests
{
    [Fact]
    public void Proposer_can_propose_but_cannot_evaluate_operate_or_read_holdout()
    {
        var access = EvaluationAccess.FromHost("alice", Scope(), EvaluationRole.Proposer);

        Assert.True(access.Can(EvaluationOperation.Propose));
        Assert.False(access.Can(EvaluationOperation.Evaluate));
        Assert.False(access.Can(EvaluationOperation.Operate));
        Assert.True(access.CanReadArtifact(isHoldout: false));
        Assert.False(access.CanReadArtifact(isHoldout: true));
        Assert.False(access.Can(EvaluationOperation.AppendAssessment));
        Assert.False(access.Can(EvaluationOperation.RegisterTrial));
    }

    [Fact]
    public void Evaluator_can_evaluate_and_read_holdout_without_operating()
    {
        var access = EvaluationAccess.FromHost("evaluator", Scope(), EvaluationRole.Evaluator);

        Assert.True(access.Can(EvaluationOperation.Evaluate));
        Assert.True(access.CanReadArtifact(isHoldout: true));
        Assert.False(access.Can(EvaluationOperation.Propose));
        Assert.False(access.Can(EvaluationOperation.Operate));
        Assert.True(access.Can(EvaluationOperation.AppendAssessment));
        Assert.False(access.Can(EvaluationOperation.RedactArtifact));
    }

    [Fact]
    public void Operator_has_only_explicit_operational_authority()
    {
        var access = EvaluationAccess.FromHost("operator", Scope(), EvaluationRole.Operator);

        Assert.True(access.Can(EvaluationOperation.Operate));
        Assert.False(access.Can(EvaluationOperation.Propose));
        Assert.False(access.Can(EvaluationOperation.Evaluate));
        Assert.False(access.CanReadArtifact(isHoldout: true));
        Assert.True(access.Can(EvaluationOperation.RedactArtifact));
        Assert.False(access.Can(EvaluationOperation.AppendAssessment));
    }

    [Fact]
    public void Host_access_requires_a_principal_valid_scope_and_known_role()
    {
        Assert.Throws<ArgumentException>(() => EvaluationAccess.FromHost(" ", Scope(), EvaluationRole.Proposer));
        Assert.Throws<ArgumentException>(() => EvaluationAccess.FromHost("alice", new(Guid.Empty, Guid.NewGuid()), EvaluationRole.Proposer));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvaluationAccess.FromHost("alice", Scope(), (EvaluationRole)99));
    }

    [Fact]
    public void Manifest_contract_does_not_contain_access_roles()
    {
        Assert.DoesNotContain(typeof(ExperimentManifest).GetProperties(),
            property => property.PropertyType == typeof(EvaluationRole));
    }

    [Fact]
    public void Access_scope_cannot_be_changed_by_callers()
    {
        var access = EvaluationAccess.FromHost("alice", Scope(), EvaluationRole.Proposer);
        Assert.Throws<UnauthorizedAccessException>(() => access.DemandScope(Scope()));
        Assert.Throws<UnauthorizedAccessException>(() => access.Demand(EvaluationOperation.AppendAssessment));
    }

    private static EvaluationScope Scope() => new(Guid.NewGuid(), Guid.NewGuid());
}
