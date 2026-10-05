using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows;

public sealed record HumanFeedbackSettings(string Question, int? TimeoutSeconds);
public static class HumanFeedbackNode
{
    public static HumanFeedbackSettings Read(JsonElement config)
    {
        var settings = config.TryGetProperty("settings", out var nested) ? nested : config;
        if (!settings.TryGetProperty("question", out var question) || question.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(question.GetString()))
            throw new InvalidDataException("Human feedback requires a non-empty question.");
        int? timeout = null;
        if (settings.TryGetProperty("timeoutSeconds", out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var seconds) || seconds <= 0)
                throw new InvalidDataException("Human feedback timeout must be a positive integer in seconds.");
            timeout = seconds;
        }
        return new(question.GetString()!, timeout);
    }

    public static NodeDefinitionResolution Resolve(JsonElement config)
    {
        var issues = new List<FlowIssue>();
        int? timeout = null;
        try { timeout = Read(config).TimeoutSeconds; }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { issues.Add(new("node.human_feedback_config_invalid", "/config", error.Message)); }
        return new([new("context", new StringType())], [new("feedback", new StringType())],
            [new("run")], [new("then"), new("timeout", Required: timeout is not null)], issues);
    }

    internal static FeedbackRequestV1 Request(StepKey key, HumanFeedbackSettings settings, string? context)
    {
        var requestId = DurableIdentity.FeedbackRequestId(key);
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, key,
            settings.Question, settings.TimeoutSeconds, Context = context })));
        return new(key, requestId, settings.Question, context, digest);
    }
}
