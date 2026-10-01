using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Comparison;
using Cortex.Evaluation.Execution;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Evaluation.Tools;

public sealed record OfflineToolResponse(int ContractVersion, bool Success, bool Persisted, JsonElement? Result, string? ErrorCode);

public static class OfflineEvaluationTools
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64
    };

    // Pure operations on caller-supplied evidence only. No store, network, credential resolver,
    // process launcher or runtime bootstrap is reachable from this boundary.
    public static OfflineToolResponse Invoke(string request)
    {
        if (request is null) return Error("invalid_request");
        if (Encoding.UTF8.GetByteCount(request) > 1_048_576) return Error("request_too_large");
        try
        {
            using var document = JsonDocument.Parse(request, new JsonDocumentOptions { MaxDepth = 64 });
            RevisionHasher.ValidateJson(document.RootElement, 0);
            var input = document.RootElement.Deserialize<OfflineRequest>(Options) ?? throw new InvalidDataException();
            if (input.ContractVersion != 1) return Error("unsupported_contract_version");
            if (input.Payload.ValueKind != JsonValueKind.Object) return Error("invalid_request");
            return input.Operation switch
            {
                "revision.hash" => Result(new { sha256 = RevisionHasher.Hash(Read<RevisionDocument>(input.Payload)) }),
                "profile.validate" => ValidateProfile(input.Payload),
                "profile.prepare" => PrepareProfile(input.Payload),
                "comparison.preview" => Compare(input.Payload),
                "assessment.preview" => Assess(input.Payload),
                "instruction.describe" => DescribeInstruction(input.Payload),
                "case.prepare.synthetic" => PrepareSyntheticCase(input.Payload),
                "experiment.preview" => PreviewSchedule(input.Payload),
                "cost.project" => ProjectCost(input.Payload),
                _ => Error("unsupported_operation")
            };
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException or InvalidOperationException or KeyNotFoundException or SessionFlowProjectException or TypedFlowException)
        {
            // Fixed codes only: malformed data, filesystem paths or private payloads never enter errors.
            return Error("invalid_request");
        }
    }

    private static OfflineToolResponse PrepareSyntheticCase(JsonElement payload)
    {
        var input = Read<SyntheticCaseInput>(payload);
        if (input.Instruction is null) throw new InvalidDataException();
        var prepared = SyntheticStepCasePreparation.Prepare(input.CaseId, input.Context, input.SelfActorRef,
            input.Instruction, input.SupplementalFixtures);
        return Result(new { prepared, executionAuthorized = false });
    }

    private static OfflineToolResponse ValidateProfile(JsonElement payload)
    {
        var profile = Read<ConfigurationProfile>(payload);
        var issues = ProfileValidator.Validate(profile);
        return Result(new { valid = issues.Count == 0, issues, sha256 = issues.Count == 0 ? RevisionHasher.Hash(profile) : null });
    }

    private static OfflineToolResponse PrepareProfile(JsonElement payload)
    {
        var input = Read<ProfilePreparationInput>(payload);
        if (input.Instruction is null || input.Settings is null) throw new InvalidDataException();
        var profile = InstructionProfilePreparation.Bind(input.Instruction, input.Settings);
        return Result(new { profile, sha256 = RevisionHasher.Hash(profile.ToRevisionDocument()), executionAuthorized = false });
    }

    private static OfflineToolResponse Compare(JsonElement payload)
    {
        var input = Read<ComparisonInput>(payload);
        if (input.Candidates is null || input.Cells is null || input.Trials is null ||
            input.Cells.Any(c => c is null) || input.Trials.Any(t => t is null || t.Cell is null))
            throw new InvalidDataException();
        return Result(ExperimentComparer.Compare(input.Candidates, input.Cells, input.Trials, input.PairedPolicy));
    }

    private static OfflineToolResponse ProjectCost(JsonElement payload)
    {
        var input = Read<CostInput>(payload);
        if (input.Price is null || input.Price.Currency is null) throw new InvalidDataException();
        return Result(CostProjection.Calculate(input.Usage, input.Price));
    }

    private static OfflineToolResponse Assess(JsonElement payload)
    {
        var input = Read<AssessmentInput>(payload);
        return Result(AssessmentEvaluator.Evaluate(input.Output, input.Criteria, input.ManualCriteria));
    }

    private static OfflineToolResponse DescribeInstruction(JsonElement payload)
    {
        var input = Read<InstructionInput>(payload);
        if (input.Project is null || string.IsNullOrWhiteSpace(input.StateId) || string.IsNullOrWhiteSpace(input.NodeId))
            throw new InvalidDataException();
        var kind = input.EntryKind switch
        {
            "dm" => SessionEntryKind.Dm,
            "group" => SessionEntryKind.Group,
            _ => throw new InvalidDataException()
        };
        return Result(FlowInstructionInspection.Describe(input.Project, input.StateId, kind, input.NodeId));
    }

    private static OfflineToolResponse PreviewSchedule(JsonElement payload)
    {
        var input = Read<ScheduleInput>(payload);
        if (input.Manifest is null || input.Cases is null) throw new InvalidDataException();
        var trials = ExperimentSchedule.Create(input.Manifest, input.Cases);
        return Result(new { trialCount = trials.Count, minimumPhysicalCalls = trials.Count, executionAuthorized = false, trials });
    }

    private static T Read<T>(JsonElement payload) where T : class => payload.Deserialize<T>(Options) ?? throw new InvalidDataException();
    private static OfflineToolResponse Result(object result) => new(1, true, false, JsonSerializer.SerializeToElement(result, Options), null);
    public static OfflineToolResponse Error(string code) => new(1, false, false, null, code);

    private sealed record OfflineRequest(
        [property: JsonRequired] int ContractVersion,
        [property: JsonRequired] string Operation,
        [property: JsonRequired] JsonElement Payload);
    private sealed record ComparisonInput(IReadOnlyList<string> Candidates, IReadOnlyList<EvaluationCell> Cells,
        IReadOnlyList<ComparisonTrial> Trials, PairedBootstrapPolicy? PairedPolicy = null);
    private sealed record CostInput(ProviderTokenUsage? Usage, PriceSnapshot Price);
    private sealed record ScheduleInput(ExperimentManifest Manifest, IReadOnlyList<ScopedRevisionRef> Cases);
    private sealed record AssessmentInput(string Output, IReadOnlyList<FieldCriterion> Criteria, IReadOnlyList<string> ManualCriteria);
    private sealed record InstructionInput(SessionFlowProject Project, string StateId, string EntryKind, string NodeId);
    private sealed record ProfilePreparationInput(FlowInstructionSnapshot Instruction, ConfigurationProfile Settings);
    private sealed record SyntheticCaseInput(string CaseId, JsonElement Context, string SelfActorRef,
        FlowInstructionSnapshot Instruction, JsonElement SupplementalFixtures);
}
