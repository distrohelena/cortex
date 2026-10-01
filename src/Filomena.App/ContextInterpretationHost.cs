using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Shadow;
using Filomena.Llm.Context;

namespace Filomena.App;

public sealed record ContextInterpretationHostConfig(string Version, ParticipationReviewDefinition Participation,
    ContextInterpretationSettings Model, int MessageHorizon, int AttemptBudget, int Capacity, bool PreviewResponses = false,
    bool RespondToReassessedMessages = false);

internal static class ContextInterpretationHost
{
    internal static LiveParticipationOptions? CreateLiveOptions(ContextInterpretationHostConfig? config, PinnedShadowProject pin,
        ShadowSessionBinding binding, IFlowCompletion completion)
    {
        Validate(config, pin, binding, true);
        if (config is null) return null;
        var analyzer = new ContextInterpretationAnalyzer(completion, config.Model);
        return new(config.Participation, analyzer.AnalyzeInitialAsync, analyzer.ReassessAsync,
            config.MessageHorizon, config.AttemptBudget, config.Capacity, config.RespondToReassessedMessages);
    }

    internal static void Validate(ContextInterpretationHostConfig? config, PinnedShadowProject pin, ShadowSessionBinding binding, bool live)
    {
        if (config is null) return;
        if (live && config.PreviewResponses || !live && config.RespondToReassessedMessages
            || binding.Kind != SessionEntryKind.Group || config.Version != "contextual-interpretation:v1"
            || config.Participation is null || config.Model is null || config.MessageHorizon < 1 || config.AttemptBudget < 1 || config.Capacity < 1
            || string.IsNullOrWhiteSpace(config.Model.Model) || string.IsNullOrWhiteSpace(config.Model.Provider)
            || string.IsNullOrWhiteSpace(config.Model.ReasoningEffort) || config.Model.MaxOutputTokens < 1
            || config.Model.BeforeMessages < 0 || config.Model.AfterMessages < 1 || config.Model.MaxTextCharacters < 1)
            throw new InvalidDataException("Invalid context interpretation configuration.");
        config.Participation.Validate(pin.Project, binding.Kind);
    }

    internal static void Configure(ContextInterpretationHostConfig? config, PinnedShadowProject pin,
        ShadowSessionBinding binding, ObservedShadowRun run, IFlowCompletion completion)
    {
        Validate(config, pin, binding, run.OutboundMode != Cortex.Core.Outbound.OutboundMode.Shadow);
        if (config is null) return;
        var analyzer = new ContextInterpretationAnalyzer(completion, config.Model);
        run.ConfigureParticipationObservation(config.Participation, analyzer.ReassessAsync, config.MessageHorizon,
            config.AttemptBudget, config.Capacity, analyzer.AnalyzeInitialAsync, config.PreviewResponses);
    }

    internal static object Report(PendingParticipationReview result, IReadOnlySet<string> promptIds) => new
    {
        targetRef = ShadowSessionBinding.Ref(result.OriginalMessageId),
        result.Status,
        interpretation = result.Assessment?.Interpretation,
        evidenceRefs = result.Assessment?.EvidenceMessageIds.Select(ShadowSessionBinding.Ref).ToArray(),
        participation = result.Review?.Decision?.ToString(),
        continuation = result.Continuation is not { } response ? null : new
        {
            status = response.Status.ToString(),
            response.Diagnostic,
            executionId = response.Outcome?.ExecutionId,
            response.Outcome?.Success,
            response.Outcome?.TransitionApplied,
            outputs = response.Outcome?.Outputs.Where(p => promptIds.Contains(p.Key)).ToDictionary()
        },
        preview = result.Preview is not { } preview ? null : new
        {
            status = preview.Status.ToString(),
            preview.Diagnostic,
            outputs = preview.Outputs.Where(p => promptIds.Contains(p.Key)).ToDictionary(),
            trace = preview.Trace.Select(t => new { t.EventName, t.NodeId, t.Exit, t.DurationMilliseconds, t.ErrorCode, t.MemoryMetadata })
        }
    };
}
