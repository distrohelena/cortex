using System.Text.Json;
using Filomena.Core.Flows;

namespace Filomena.Core.Inference;

public sealed record InferenceScope
{
    public InferenceScope(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ProjectId = projectId;
    }

    public string ProjectId { get; }
}

public sealed record InferenceCorrelation
{
    public InferenceCorrelation(string runId, string stepId, string configurationRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRevision);
        RunId = runId;
        StepId = stepId;
        ConfigurationRevision = configurationRevision;
    }

    public string RunId { get; }
    public string StepId { get; }
    public string ConfigurationRevision { get; }
}

public sealed class InferenceDeniedException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>Trusted composition only. The adapter and validator are not supplied by model output.</summary>
public sealed class HostInferenceProfile
{
    private readonly JsonElement[] additionalSchemas;
    public HostInferenceProfile(string profileId, string revision, IFlowCompletion adapter,
        FlowCompletionRequest expectedRequest, Action<string> validateOutput)
        : this(profileId, revision, adapter, expectedRequest, validateOutput, false) { }

    private HostInferenceProfile(string profileId, string revision, IFlowCompletion adapter,
        FlowCompletionRequest expectedRequest, Action<string>? validateOutput, bool runtimeOwned,
        IReadOnlyList<JsonElement>? additionalSchemas = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(expectedRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRequest.NodeId);
        if (!runtimeOwned) ArgumentNullException.ThrowIfNull(validateOutput);
        ProfileId = profileId;
        Revision = revision;
        Adapter = adapter;
        // Only retain routing/schema constraints, not a private sample prompt or input.
        ExpectedRequest = expectedRequest with
        {
            System = "", Inputs = JsonSerializer.SerializeToElement(new { }), Correction = null,
            ResponseSchema = expectedRequest.ResponseSchema?.Clone()
        };
        ValidateOutput = validateOutput;
        RuntimeOwnedValidation = runtimeOwned;
        this.additionalSchemas = additionalSchemas?.Select(schema => schema.Clone()).ToArray() ?? [];
    }

    public static HostInferenceProfile ForRuntime(string profileId, string revision,
        IFlowCompletion adapter, FlowCompletionRequest expectedRequest, IReadOnlyList<JsonElement>? additionalSchemas = null) =>
        new(profileId, revision, adapter, expectedRequest, null, true, additionalSchemas);

    public string ProfileId { get; }
    public string Revision { get; }
    public FlowCompletionRequest ExpectedRequest { get; }
    internal IFlowCompletion Adapter { get; }
    internal Action<string>? ValidateOutput { get; }
    internal bool RuntimeOwnedValidation { get; }

    public void ValidateRequest(FlowCompletionRequest request)
    {
        var expected = ExpectedRequest;
        if (request.NodeId != expected.NodeId || request.Provider != expected.Provider ||
            request.Model != expected.Model || request.ReasoningEffort != expected.ReasoningEffort ||
            request.MaxOutputTokens != expected.MaxOutputTokens ||
            !(SameSchema(request.ResponseSchema, expected.ResponseSchema) ||
              additionalSchemas.Any(schema => SameSchema(request.ResponseSchema, schema))))
            throw new InferenceDeniedException("profile_mismatch");
    }

    private static bool SameSchema(JsonElement? actual, JsonElement? expected) =>
        actual is null ? expected is null : expected is not null && JsonElement.DeepEquals(actual.Value, expected.Value);
}

public enum InferenceAttemptStage
{
    DeniedBeforeInvocation, CancelledBeforeInvocation, Completed, AdapterReturnedPendingValidation,
    InvalidOutput, FailedAfterInvocation, CancelledAfterInvocation
}

public sealed record InferenceAttemptObservation(
    string ProjectId, string PrincipalRef, string RunId, string StepId, string AttemptId,
    string ConfigurationRevision, string ProfileId, string ProfileRevision,
    TimeSpan Duration, InferenceAttemptStage Stage, string Code,
    string? RequestedProvider, string? RequestedModel)
{
    // Task<string> does not supply trustworthy provider usage or observed deployment identity.
    public string? ObservedModel => null;
    public long? InputTokens => null;
    public long? OutputTokens => null;
    public decimal? Cost => null;
}
