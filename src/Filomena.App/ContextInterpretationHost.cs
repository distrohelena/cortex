using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Shadow;
using Filomena.Llm.Context;

namespace Filomena.App;

public sealed record ContextInterpretationHostConfig(string Version, ParticipationReviewDefinition Participation,
    ContextInterpretationSettings Model, int MessageHorizon, int AttemptBudget, int Capacity, bool PreviewResponses = false);

internal static class ContextInterpretationHost
{
    internal static void Validate(ContextInterpretationHostConfig? config, PinnedShadowProject pin, ShadowSessionBinding binding, bool live)
    {
        if (config is null) return;
        if (live || binding.Kind != SessionEntryKind.Group || config.Version != "contextual-interpretation:v1"
            || config.Participation is null || config.Model is null || config.MessageHorizon < 1 || config.AttemptBudget < 1 || config.Capacity < 1
            || string.IsNullOrWhiteSpace(config.Model.Model) || string.IsNullOrWhiteSpace(config.Model.Provider)
            || string.IsNullOrWhiteSpace(config.Model.ReasoningEffort) || config.Model.MaxOutputTokens < 1
            || config.Model.BeforeMessages < 0 || config.Model.AfterMessages < 1 || config.Model.MaxTextCharacters < 1)
            throw new InvalidDataException("Invalid shadow context interpretation configuration.");
        config.Participation.Validate(pin.Project, binding.Kind);
    }

    internal static void Configure(ContextInterpretationHostConfig? config, PinnedShadowProject pin,
        ShadowSessionBinding binding, ObservedShadowRun run, IFlowCompletion completion)
    {
        Validate(config, pin, binding, run.OutboundMode != Filomena.Core.Outbound.OutboundMode.Shadow);
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
        preview = result.Preview is not { } preview ? null : new
        {
            status = preview.Status.ToString(),
            preview.Diagnostic,
            outputs = preview.Outputs.Where(p => promptIds.Contains(p.Key)).ToDictionary(),
            trace = preview.Trace.Select(t => new { t.EventName, t.NodeId, t.Exit, t.DurationMilliseconds, t.ErrorCode, t.MemoryMetadata })
        }
    };
}
