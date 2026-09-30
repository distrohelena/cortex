using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class AttemptChargePolicyTests
{
    [Theory]
    [InlineData("version")]
    [InlineData("negative")]
    [InlineData("evidence")]
    [InlineData("owner")]
    [InlineData("generation")]
    public void Invalid_billing_declarations_are_rejected(string defect)
    {
        var (request, binding) = Fixture();
        request = defect switch
        {
            "version" => request with { ContractVersion = 2 },
            "negative" => request with { Charge = -1 },
            "evidence" => request with { Evidence = new(Guid.NewGuid(), "not-a-hash") },
            "owner" => request with { AttemptOwner = Guid.Empty },
            _ => request with { AttemptGeneration = 0 }
        };
        Assert.Throws<InvalidDataException>(() => AttemptChargePolicy.Evaluate(request, binding));
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(0.5, 1, false)]
    [InlineData(2, 2, true)]
    public void Declared_charge_never_erases_reserved_exposure_or_claims_execution_success(
        decimal charge, decimal retained, bool overrun)
    {
        var (request, binding) = Fixture();
        var result = AttemptChargePolicy.Evaluate(request with { Charge = charge }, binding);
        Assert.Equal(retained, result.RetainedExposure);
        Assert.Equal(overrun, result.ExceedsReservation);
        Assert.False(result.ReleasesReservation);
        Assert.False(result.ResolvesExecution);
        Assert.Equal("operator_billing_attestation", result.Origin);
    }

    [Theory]
    [InlineData("experiment")]
    [InlineData("trial")]
    [InlineData("attempt")]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("approval")]
    [InlineData("currency")]
    [InlineData("legacy")]
    public void Decision_must_identify_the_original_physical_attempt_not_the_current_worker(string defect)
    {
        var (request, binding) = Fixture();
        binding = defect switch
        {
            "experiment" => binding with { ExperimentId = Guid.NewGuid() },
            "trial" => binding with { TrialId = Guid.NewGuid() },
            "attempt" => binding with { AttemptId = Guid.NewGuid() },
            "owner" => binding with { Owner = Guid.NewGuid() },
            "generation" => binding with { Generation = 2 },
            "approval" => binding with { ApprovalId = Guid.NewGuid() },
            "currency" => binding with { Currency = "USD" },
            _ => binding with { ApprovalId = null, Currency = null }
        };
        Assert.Throws<InvalidOperationException>(() => AttemptChargePolicy.Evaluate(request, binding));
    }

    [Theory]
    [InlineData("observed")]
    [InlineData("indeterminate")]
    public void Billing_evidence_does_not_require_inventing_a_successful_model_outcome(string state)
    {
        var (request, binding) = Fixture();
        var result = AttemptChargePolicy.Evaluate(request, binding with { State = state });
        Assert.False(result.ResolvesExecution);
    }

    [Fact]
    public void In_flight_request_cannot_be_declared_final_without_quarantine_or_completion()
    {
        var (request, binding) = Fixture();
        Assert.Throws<InvalidOperationException>(() => AttemptChargePolicy.Evaluate(request, binding with { State = "started" }));
    }

    [Fact]
    public void Fenced_crashed_attempt_can_record_billing_without_rewriting_its_started_state()
    {
        var (request, binding) = Fixture();
        var result = AttemptChargePolicy.Evaluate(request, binding with { State = "started", ExecutionQuarantined = true });
        Assert.False(result.ResolvesExecution);
        Assert.False(result.ReleasesReservation);
    }

    private static (AttemptChargeDeclaration, ChargeAttemptBinding) Fixture()
    {
        var request = new AttemptChargeDeclaration(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 1, Guid.NewGuid(), "BRL", 1m, new(Guid.NewGuid(), new string('a', 64)));
        return (request, new(request.ExperimentId, request.TrialId, request.AttemptId, request.AttemptOwner,
            request.AttemptGeneration, request.ApprovalId, "BRL", "indeterminate", 1m));
    }
}
