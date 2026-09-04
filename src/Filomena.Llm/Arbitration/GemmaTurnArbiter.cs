using System.Text.Json;
using Filomena.Core.Arbitration;
using Filomena.Llm.LmStudio;

namespace Filomena.Llm.Arbitration;

public sealed class GemmaTurnArbiter(ILmStudioChatClient client) : ITurnArbiter
{
    private const int MaximumAttempts = 2;

    public async Task<TurnDecision> DecideAsync(
        TurnArbitrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prompt = ArbiterPrompt.BuildUser(request);
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            try
            {
                var content = await client.CompleteArbitrationJsonAsync(
                    ArbiterPrompt.System,
                    prompt,
                    cancellationToken);
                if (TryParse(content, out var decision) &&
                    TurnDecisionPolicy.IsValid(decision, request))
                {
                    return decision;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                HttpRequestException or
                InvalidDataException or
                JsonException or
                TaskCanceledException)
            {
            }
        }

        return TurnDecisionPolicy.InvalidOutputFallback(request);
    }

    private static bool TryParse(string content, out TurnDecision decision)
    {
        decision = default!;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryAction(root, out var action) ||
                !TryReason(root, out var reason) ||
                !TryOptionalString(root, "anchorRef", out var anchor) ||
                !TryOptionalString(root, "targetRef", out var target))
            {
                return false;
            }

            decision = new TurnDecision(action, anchor, target, reason);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryAction(JsonElement root, out TurnAction action)
    {
        action = default;
        return String(root, "decision") switch
        {
            "respond" => Assign(TurnAction.Respond, out action),
            "wait" => Assign(TurnAction.Wait, out action),
            "ignore" => Assign(TurnAction.Ignore, out action),
            _ => false,
        };
    }

    private static bool TryReason(JsonElement root, out TurnReason reason)
    {
        reason = default;
        return String(root, "reason") switch
        {
            "direct_address" => Assign(TurnReason.DirectAddress, out reason),
            "structural_reply" => Assign(TurnReason.StructuralReply, out reason),
            "useful_contribution" => Assign(TurnReason.UsefulContribution, out reason),
            "natural_social_opportunity" => Assign(TurnReason.NaturalSocialOpportunity, out reason),
            "conversation_still_flowing" => Assign(TurnReason.ConversationStillFlowing, out reason),
            "recently_participated" => Assign(TurnReason.RecentlyParticipated, out reason),
            "no_useful_contribution" => Assign(TurnReason.NoUsefulContribution, out reason),
            _ => false,
        };
    }

    private static bool TryOptionalString(
        JsonElement root,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static string? String(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim().ToLowerInvariant()
            : null;

    private static bool Assign<T>(T value, out T target)
    {
        target = value;
        return true;
    }
}
