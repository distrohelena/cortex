using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Inference;

internal static class ProviderResponseDiagnostics
{
    internal static async Task CaptureErrorAsync(CompletionDiagnostics? diagnostics, HttpResponseMessage response, CancellationToken token)
    {
        if (diagnostics?.CaptureEnabled != true) return;
        try { diagnostics.CaptureResponse(await response.Content.ReadAsStringAsync(token)); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { /* Debug capture must never replace the provider's status exception. */ }
    }

    internal static void ReportUsage(CompletionDiagnostics? diagnostics, JsonElement response, string? provider)
    {
        if (diagnostics is null || response.ValueKind != JsonValueKind.Object) return;
        string? model = response.TryGetProperty("model", out var modelValue) && modelValue.ValueKind == JsonValueKind.String
            ? modelValue.GetString() : null;
        long? Read(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var count) && count >= 0 ? count : null;
        response.TryGetProperty("usage", out var usage);
        diagnostics.ReportUsage(new(Read(usage, "input_tokens") ?? Read(usage, "prompt_tokens"), Read(usage, "output_tokens") ?? Read(usage, "completion_tokens"),
            ObservedModel: model, ObservedProvider: provider));
    }
}
