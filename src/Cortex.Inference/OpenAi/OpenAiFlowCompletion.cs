using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Inference.OpenAi;

/// <summary>Official Responses transport. Credentials never belong to the graph contract.</summary>
public sealed class OpenAiFlowCompletion(
    HttpClient http, Func<string?> credential, string reasoningEffort = "medium", int maxOutputTokens = 1024) : IFlowCompletion
{
    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        if (request.Provider != "openai" || string.IsNullOrWhiteSpace(request.Model))
            throw new InvalidDataException("OpenAI provider and model required.");
        var key = credential();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("OPENAI_API_KEY unavailable.");
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        object format = request.ResponseSchema is { } schema
            ? new { type = "json_schema", name = "flow_output", strict = true, schema }
            : new { type = "text" };
        message.Content = JsonContent.Create(new
        {
            model = request.Model,
            store = false,
            instructions = FlowInstructionPrompt.Build(request),
            input = new[] { new { role = "user", content = request.Inputs.GetRawText() } },
            text = new { format },
            reasoning = new { effort = request.ReasoningEffort ?? reasoningEffort },
            max_output_tokens = request.MaxOutputTokens ?? maxOutputTokens
        });
        using var response = await http.SendAsync(message, token);
        // Do not include remote error payloads, headers or credentials in exception/trace.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = document.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
            throw new InvalidDataException("OpenAI response not completed.");
        var parts = new List<string>();
        foreach (var item in root.GetProperty("output").EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "message") continue;
            foreach (var content in item.GetProperty("content").EnumerateArray())
            {
                if (content.GetProperty("type").GetString() != "output_text")
                    throw new InvalidDataException("OpenAI response refused or unsupported.");
                parts.Add(content.GetProperty("text").GetString() ?? "");
            }
        }
        var result = string.Concat(parts);
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("OpenAI response empty.");
        return result;
    }
}
