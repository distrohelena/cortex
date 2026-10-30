using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Inference.OpenJev;

public sealed record OpenJevOption(string Label, string Text, double Logprob, double Probability);
public sealed record OpenJevDecision(string Model, IReadOnlyList<OpenJevOption> Options, int InputTokens, int OutputTokens, TimeSpan Elapsed);

/// <summary>
/// OpenJev's direct method against a local llama.cpp <c>llama-server</c>: one chat completion per question,
/// constrained to option letters, read back as a softmax over the letters' logprobs.
/// </summary>
public sealed class OpenJevClient(HttpClient http, Uri baseEndpoint)
{
    private const int MaximumOptions = 20;
    private const string SystemPrompt = "Make the requested decision from the supplied state. Follow the output format exactly.";
    private static readonly string[] AllLabels = Enumerable.Range(0, MaximumOptions).Select(i => ((char)('A' + i)).ToString()).ToArray();
    private readonly Dictionary<string, int[]> labelTokens = new(StringComparer.Ordinal);

    public Task<OpenJevDecision> DecideAsync(string model, string state, string question, IReadOnlyList<string> options,
        CompletionDiagnostics? diagnostics, CancellationToken token, bool applyLabelBias = true) =>
        DecideAsync(model, state, question, options, diagnostics, token, applyLabelBias, 0, 0);

    /// <summary>Prior usage lets a multi-question invocation report cumulative totals, as diagnostics require.</summary>
    internal async Task<OpenJevDecision> DecideAsync(string model, string state, string question, IReadOnlyList<string> options,
        CompletionDiagnostics? diagnostics, CancellationToken token, bool applyLabelBias, long priorInputTokens, long priorOutputTokens)
    {
        token.ThrowIfCancellationRequested();
        if (!baseEndpoint.IsAbsoluteUri || baseEndpoint.Scheme is not ("http" or "https") || !baseEndpoint.IsLoopback ||
            !string.IsNullOrEmpty(baseEndpoint.UserInfo) || !string.IsNullOrEmpty(baseEndpoint.Query) ||
            !string.IsNullOrEmpty(baseEndpoint.Fragment) || !baseEndpoint.AbsolutePath.EndsWith('/'))
            throw new InvalidDataException("OpenJev requires an absolute loopback HTTP(S) base endpoint ending in '/', without credentials, query or fragment.");
        if (string.IsNullOrWhiteSpace(model) || state is null || string.IsNullOrWhiteSpace(question))
            throw new InvalidDataException("OpenJev model, state and question required.");
        if (options is null || options.Count is < 2 or > MaximumOptions || options.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"OpenJev requires 2 to {MaximumOptions} non-empty options.");
        var labels = AllLabels[..options.Count];
        var bias = applyLabelBias ? await LabelTokensAsync(model, diagnostics, token) : null;
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"State:\n{state}\n\nQuestion:\n{question}\n\nAllowed options:\n" +
                    string.Join("\n", options.Select((option, i) => $"{labels[i]}. {option}")) +
                    $"\n\nReply with exactly one option letter from: {string.Join(", ", labels)}." }
            },
            ["max_tokens"] = 1, ["temperature"] = 1, ["top_k"] = 0, ["top_p"] = 1,
            ["logprobs"] = true, ["top_logprobs"] = MaximumOptions
        };
        if (bias is not null)
            body["logit_bias"] = labels.Select((_, i) => bias[i]).ToDictionary(id => id.ToString(CultureInfo.InvariantCulture), _ => 100);
        body["grammar"] = "root ::= " + string.Join(" | ", labels.Select(label => $"\"{label}\""));
        body["cache_prompt"] = false;
        body["chat_template_kwargs"] = new { enable_thinking = false };
        var started = Stopwatch.GetTimestamp();
        using var response = await PostAsync("v1/chat/completions", body, diagnostics, token);
        return await ReadResponseAsync(diagnostics, () =>
            ParseDecision(response, model, options, labels, diagnostics, priorInputTokens, priorOutputTokens, started, token));
    }

    private async Task<int[]> LabelTokensAsync(string model, CompletionDiagnostics? diagnostics, CancellationToken token)
    {
        lock (labelTokens) if (labelTokens.TryGetValue(model, out var cached)) return cached;
        var ids = new int[AllLabels.Length];
        for (var i = 0; i < AllLabels.Length; i++)
        {
            using var response = await PostAsync("tokenize", new { content = AllLabels[i], add_special = false }, diagnostics, token);
            var label = AllLabels[i];
            ids[i] = await ReadResponseAsync(diagnostics, async () =>
            {
                // Never captured: the single capture slot belongs to the decision response.
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                UniqueObject(document.RootElement);
                var tokens = document.RootElement.GetProperty("tokens");
                // A label that is not exactly one token cannot be biased or read back as one logprob.
                if (tokens.ValueKind != JsonValueKind.Array || tokens.GetArrayLength() != 1 || tokens[0].GetInt32() < 0)
                    throw new InvalidDataException($"Invalid OpenJev label tokenization: label '{label}' is not exactly one token.");
                return tokens[0].GetInt32();
            });
        }
        if (ids.Distinct().Count() != ids.Length)
            throw Rejected(new InvalidDataException("Invalid OpenJev label tokenization: labels share a token."), diagnostics);
        lock (labelTokens) labelTokens.TryAdd(model, ids);
        return ids;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object body, CompletionDiagnostics? diagnostics, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseEndpoint, path)) { Content = JsonContent.Create(body) };
        var response = await http.SendAsync(message, token);
        if (response.IsSuccessStatusCode) return response;
        using (response)
        {
            // Error bodies (e.g. llama-server's context overflow) only reach opt-in, redacted capture.
            await ProviderResponseDiagnostics.CaptureErrorAsync(diagnostics, response, token);
            var failure = new HttpRequestException($"OpenJev HTTP {(int)response.StatusCode}.", null, response.StatusCode);
            failure.Data["failureCategory"] = "read_only_model_response_rejected";
            throw failure;
        }
    }

    private static async Task<OpenJevDecision> ParseDecision(HttpResponseMessage response, string model, IReadOnlyList<string> options,
        string[] labels, CompletionDiagnostics? diagnostics, long priorInput, long priorOutput, long started, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);
        diagnostics?.CaptureResponse(body);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        ReportUsage(diagnostics, root, priorInput, priorOutput);
        UniqueObject(root);
        var returnedModel = root.GetProperty("model").GetString();
        // The server may have loaded another GGUF than the alias requested; never attribute its answer.
        if (returnedModel != model) throw new InvalidDataException("Invalid OpenJev decision contract: model_mismatch.");
        var usage = root.GetProperty("usage");
        UniqueObject(usage);
        var input = usage.GetProperty("prompt_tokens").GetInt32();
        var output = usage.GetProperty("completion_tokens").GetInt32();
        if (input < 0 || output < 0) throw Invalid();
        var choices = root.GetProperty("choices");
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1) throw Invalid();
        var content = choices[0].GetProperty("logprobs").GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0) throw Invalid();
        var top = content[0].GetProperty("top_logprobs");
        if (top.ValueKind != JsonValueKind.Array) throw Invalid();
        var logprobs = labels.Select(label => LabelLogprob(top, label)).ToArray();
        var maximum = logprobs.Max();
        var exponents = logprobs.Select(value => Math.Exp(value - maximum)).ToArray();
        var total = exponents.Sum();
        return new(returnedModel, labels.Select((label, i) => new OpenJevOption(label, options[i], logprobs[i], exponents[i] / total)).ToArray(),
            input, output, Stopwatch.GetElapsedTime(started));
    }

    private static double LabelLogprob(JsonElement top, string label)
    {
        // Reference matching: exact token text, or a single byte equal to the label's ASCII code.
        var matches = top.EnumerateArray().Where(entry =>
        {
            UniqueObject(entry);
            if (entry.GetProperty("token").GetString() == label) return true;
            return entry.TryGetProperty("bytes", out var bytes) && bytes.ValueKind == JsonValueKind.Array &&
                bytes.GetArrayLength() == 1 && bytes[0].GetInt32() == label[0];
        }).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Invalid OpenJev decision contract: label '{label}' has {matches.Length} logprob entries.");
        var logprob = matches[0].GetProperty("logprob").GetDouble();
        if (!double.IsFinite(logprob)) throw new InvalidDataException("Invalid OpenJev decision contract: non_finite_logprob.");
        return logprob;
    }

    private static void ReportUsage(CompletionDiagnostics? diagnostics, JsonElement root, long priorInput, long priorOutput)
    {
        if (diagnostics is null || root.ValueKind != JsonValueKind.Object) return;
        string? model = root.TryGetProperty("model", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        long? Read(string name) => root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
            usage.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number &&
            count.TryGetInt64(out var tokens) && tokens >= 0 ? tokens : null;
        // Cumulative for the invocation: earlier questions' totals stay counted even when this response is unusable.
        diagnostics.ReportUsage(new(priorInput + Read("prompt_tokens") ?? NullIfZero(priorInput),
            priorOutput + Read("completion_tokens") ?? NullIfZero(priorOutput), ObservedModel: model, ObservedProvider: "openjev"));
    }
    private static long? NullIfZero(long value) => value == 0 ? null : value;

    private static async Task<T> ReadResponseAsync<T>(CompletionDiagnostics? diagnostics, Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            var failure = Rejected(error, diagnostics);
            if (ReferenceEquals(failure, error)) throw;
            throw failure;
        }
    }

    private static InvalidDataException Rejected(Exception error, CompletionDiagnostics? diagnostics)
    {
        // Neither malformed payloads nor provider error bodies belong in normal logs.
        diagnostics?.ReportFailure(error, error is JsonException ? FlowFailureCategory.Parsing : FlowFailureCategory.Validation);
        var failure = error as InvalidDataException ?? Invalid();
        // This adapter submits inference only, never tools or delivery.
        failure.Data["failureCategory"] = "read_only_model_response_rejected";
        return failure;
    }

    private static void UniqueObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!names.Add(property.Name)) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("Invalid OpenJev decision contract.");
}
