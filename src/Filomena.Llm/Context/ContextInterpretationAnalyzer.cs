using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Filomena.Llm.Context;

public sealed record ContextInterpretationSettings(string Model, string Provider, string ReasoningEffort,
    int MaxOutputTokens, int BeforeMessages, int AfterMessages, int MaxTextCharacters);

public sealed class ContextInterpretationAnalyzer(IFlowCompletion completion, ContextInterpretationSettings settings)
{
    public Task<ParticipationReassessment> AnalyzeInitialAsync(ParticipationReassessmentContext context, CancellationToken token)
        => AnalyzeAsync(context, false, token);

    public Task<ParticipationReassessment> ReassessAsync(ParticipationReassessmentContext context, CancellationToken token)
        => AnalyzeAsync(context, true, token);

    private async Task<ParticipationReassessment> AnalyzeAsync(ParticipationReassessmentContext context, bool reassessment, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        token.ThrowIfCancellationRequested();
        if (settings.BeforeMessages < 0 || settings.AfterMessages < 1 || settings.MaxTextCharacters < 1
            || settings.MaxOutputTokens < 1 || string.IsNullOrWhiteSpace(settings.Model)
            || string.IsNullOrWhiteSpace(settings.Provider) || string.IsNullOrWhiteSpace(settings.ReasoningEffort))
            throw new InvalidDataException("Invalid host interpretation settings.");
        var original = context.Original;
        var session = context.Snapshot.Session;
        var history = context.History;
        if (original.Direction != SessionMessageDirection.Inbound || original.EntryKind != SessionEntryKind.Group
            || context.HasConfirmedSpeech || history.Count != context.Snapshot.HistoryCount
            || !history.Any(m => m == original) || history.Select(m => m.ProviderMessageId).Distinct().Count() != history.Count
            || history.Select(m => m.AdmissionWatermark).Distinct().Count() != history.Count
            || history.Any(m => m.AccountId != session.AccountId || m.ChannelId != session.ChannelId
                || m.NativeConversationId != session.NativeConversationId || m.EntryKind != SessionEntryKind.Group
                || m.AdmissionWatermark > context.Snapshot.AdmissionWatermark))
            throw new InvalidDataException("Interpretation context binding mismatch.");

        var scope = JsonSerializer.Serialize(session);
        string Ref(string kind, string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\n" + kind + "\n" + value)));
        var before = history.Where(m => m.AdmissionWatermark < original.AdmissionWatermark).OrderBy(m => m.AdmissionWatermark).TakeLast(settings.BeforeMessages).ToArray();
        var after = reassessment ? history.Where(m => m.AdmissionWatermark > original.AdmissionWatermark).OrderBy(m => m.AdmissionWatermark).TakeLast(settings.AfterMessages).ToArray() : [];
        var visible = before.Append(original).Concat(after).ToArray();
        var ids = visible.ToDictionary(m => Ref("message", m.ProviderMessageId), m => m.ProviderMessageId);
        var gaps = new HashSet<string> { "bounded_retained_history_not_complete_chat" };
        if (visible.Length < history.Count) gaps.Add("context_messages_omitted");
        static string? Value(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        string? Text(JsonElement element, string name)
        {
            var text = Value(element, name);
            if (text is null) return null;
            if (text.Length <= settings.MaxTextCharacters) return text;
            gaps.Add("text_truncated");
            return text[..settings.MaxTextCharacters];
        }
        object? Person(JsonElement item)
        {
            if (!item.TryGetProperty("remetente", out var person) || person.ValueKind != JsonValueKind.Object) return null;
            var stable = Value(person, "stableId");
            if (stable is null) gaps.Add("speaker_identity_missing");
            return new { reference = stable is null ? null : Ref("speaker", stable), name = Text(person, "nome") };
        }
        object Project(SessionHistoryEntry item)
        {
            var message = item.Message;
            var quote = message.TryGetProperty("reply", out var reply) && reply.ValueKind == JsonValueKind.Object
                ? new { id = Value(reply, "id") is { } id ? Ref("message", id) : null, text = Text(reply, "texto"), speaker = Person(reply) }
                : null;
            return new { id = Ref("message", item.ProviderMessageId), speaker = Person(message), text = Text(message, "texto"), quote };
        }
        var projectedBefore = before.Select(Project).ToArray();
        var projectedTarget = Project(original);
        var projectedAfter = after.Select(Project).ToArray();
        var input = JsonSerializer.SerializeToElement(new
        {
            caseId = Ref("message", original.ProviderMessageId),
            groupRef = Ref("session", scope),
            phase = reassessment ? "reassessment" : "initial",
            gaps = gaps.Order().ToArray(),
            before = projectedBefore,
            target = projectedTarget,
            after = projectedAfter
        });
        var output = await completion.CompleteAsync(new("context-interpretation", ContextInterpretationContract.Prompt,
            input, ContextInterpretationContract.Schema, settings.Model, settings.Provider, null,
            settings.ReasoningEffort, settings.MaxOutputTokens), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        ContextInterpretationContract.Validate(output, input);
        using var parsed = JsonDocument.Parse(output);
        var value = parsed.RootElement;
        var opportunity = value.GetProperty("responseOpportunity").GetString() switch
        {
            "still_relevant" => ReassessmentOpportunity.Relevant,
            "obsolete" => ReassessmentOpportunity.Obsolete,
            _ => ReassessmentOpportunity.Uncertain
        };
        return new(value.GetProperty("confidence").GetString() == "high" && value.GetProperty("reading").GetString() != "ambigua",
            opportunity, value.GetProperty("evidenceIds").EnumerateArray().Select(id => ids[id.GetString()!]).ToArray())
        {
            Interpretation = new(value.GetProperty("reading").GetString()!, value.GetProperty("alternative").GetString(),
                value.GetProperty("confidence").GetString()!, value.GetProperty("explanation").GetString()!)
        };
    }
}
