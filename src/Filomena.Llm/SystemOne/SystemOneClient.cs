using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Filomena.Llm.SystemOne;

public enum SystemOneProbabilityPolicy { LayaFourDecimals, Jev113TwoDecimals }

public sealed record SystemOneResult(string Model, JsonElement Answers, int InputTokens, int OutputTokens, TimeSpan Elapsed)
{
    public string ProbabilityPolicy { get; init; } = nameof(SystemOneProbabilityPolicy.LayaFourDecimals);
}

/// <summary>Host-configured transport for the Jev/Laya typed-decision protocol.</summary>
public sealed class SystemOneClient(HttpClient http, Uri endpoint, Func<string?> credential, bool requireCredential,
    SystemOneProbabilityPolicy probabilityPolicy = SystemOneProbabilityPolicy.LayaFourDecimals)
{
    public async Task<SystemOneResult> DecideAsync(string model, JsonElement state, JsonElement questions, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(probabilityPolicy)) throw Invalid();
        if (!endpoint.IsAbsoluteUri || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !(endpoint.Scheme == Uri.UriSchemeHttps || endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback))
            throw new InvalidDataException("System 1 requires HTTPS or a loopback HTTP endpoint.");
        if (string.IsNullOrWhiteSpace(model) || state.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
            throw new InvalidDataException("System 1 model and structured/text state required.");
        var expected = ReadQuestions(questions);
        var key = credential();
        if (requireCredential && string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("System 1 credential unavailable.");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(key)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = JsonContent.Create(new { model, state, questions });
        var started = Stopwatch.GetTimestamp();
        using var response = await http.SendAsync(message, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"System 1 HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root = document.RootElement;
            UniqueObject(root);
            var returnedModel = root.GetProperty("model").GetString();
            if (string.IsNullOrWhiteSpace(returnedModel)) throw Invalid();
            if (probabilityPolicy == SystemOneProbabilityPolicy.Jev113TwoDecimals && returnedModel != "jev-1.13.0")
                throw new InvalidDataException("Invalid System 1 decision contract: probability_policy_model_mismatch.");
            var answers = root.GetProperty("answers");
            ExactKeys(answers, expected.Keys);
            foreach (var (id, question) in expected) ValidateAnswer(answers.GetProperty(id), question, probabilityPolicy);
            var usage = root.GetProperty("usage");
            UniqueObject(usage);
            var input = usage.GetProperty("input_tokens").GetInt32();
            var output = usage.GetProperty("output_tokens").GetInt32();
            if (input < 0 || output < 0) throw Invalid();
            return new(returnedModel, answers.Clone(), input, output, Stopwatch.GetElapsedTime(started))
                { ProbabilityPolicy = probabilityPolicy.ToString() };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            // Neither malformed payloads nor provider error bodies belong in normal logs.
            throw Invalid();
        }
    }

    private static Dictionary<string, JsonElement> ReadQuestions(JsonElement questions)
    {
        try
        {
            UniqueObject(questions);
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in questions.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name)) throw Invalid();
                var question = property.Value;
                UniqueObject(question);
                Description(question.GetProperty("instructions"));
                switch (question.GetProperty("type").GetString())
                {
                    case "choice":
                        var choices = question.GetProperty("criteria");
                        UniqueObject(choices);
                        var options = choices.EnumerateObject().ToArray();
                        if (options.Length is < 2 or > 255) throw Invalid();
                        foreach (var option in options)
                        {
                            if (string.IsNullOrWhiteSpace(option.Name)) throw Invalid();
                            if (option.Value.ValueKind != JsonValueKind.Null) Description(option.Value);
                        }
                        break;
                    case "score":
                        var levels = question.GetProperty("criteria");
                        if (levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() is < 2 or > 10) throw Invalid();
                        foreach (var level in levels.EnumerateArray()) Description(level);
                        break;
                    case "noul":
                        if (question.TryGetProperty("criteria", out var criteria))
                        {
                            ExactKeys(criteria, ["true", "false"]);
                            foreach (var description in criteria.EnumerateObject()) Description(description.Value);
                        }
                        break;
                    default: throw Invalid();
                }
                result.Add(property.Name, question);
            }
            if (result.Count == 0) throw Invalid();
            return result;
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw Invalid(); }
    }

    private static void ValidateAnswer(JsonElement answer, JsonElement question, SystemOneProbabilityPolicy policy)
    {
        UniqueObject(answer);
        var type = question.GetProperty("type").GetString();
        if (answer.GetProperty("type").GetString() != type) throw Invalid();
        if (answer.TryGetProperty("confidence", out var confidence)) Probability(confidence);
        if (type == "noul") { Probability(answer.GetProperty("noul")); return; }
        Probability(answer.GetProperty("confidence"));
        var criteria = question.GetProperty("criteria");
        var keys = type == "choice" ? criteria.EnumerateObject().Select(p => p.Name).ToArray()
            : Enumerable.Range(0, criteria.GetArrayLength()).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var probabilities = answer.GetProperty("probabilities");
        ExactKeys(probabilities, keys);
        var sum = 0d;
        foreach (var p in probabilities.EnumerateObject()) sum += Probability(p.Value);
        // Laya rounds each probability to four decimals; permit only its aggregate rounding budget.
        if (Math.Abs(sum - 1) > keys.Length * 0.000051 + 0.000001 &&
            !(policy == SystemOneProbabilityPolicy.Jev113TwoDecimals && CompatibleWithTwoDecimalRounding(probabilities, sum)))
            throw new InvalidDataException("Invalid System 1 decision contract: probability_mass_mismatch.");
        if (type == "choice")
        {
            var choice = answer.GetProperty("choice").GetString();
            if (!keys.Contains(choice, StringComparer.Ordinal)) throw Invalid();
            var selected = probabilities.GetProperty(choice!).GetDouble();
            if (probabilities.EnumerateObject().Any(p => p.Value.GetDouble() > selected))
                throw new InvalidDataException("Invalid System 1 decision contract: choice_probability_mismatch.");
        }
        else
        {
            var score = answer.GetProperty("score").GetDouble();
            if (!double.IsFinite(score) || score < 0 || score > keys.Length - 1) throw Invalid();
            var legend = answer.GetProperty("legend");
            ExactKeys(legend, keys);
            for (var i = 0; i < keys.Length; i++)
                if (!JsonElement.DeepEquals(legend.GetProperty(keys[i]), criteria[i])) throw Invalid();
        }
    }

    private static bool CompatibleWithTwoDecimalRounding(JsonElement probabilities, double sum)
    {
        // Explicit local compatibility policy, not normalization or a provider precision guarantee.
        // SDK documents approximate mass; captured Jev 1.13 responses use two decimals.
        // Bound both the per-value rounding intervals and total deviation, even for 255 options.
        const double epsilon = 1e-9;
        if (Math.Abs(sum - 1) > 0.02 + epsilon) return false;
        double lower = 0, upper = 0;
        foreach (var property in probabilities.EnumerateObject())
        {
            var value = property.Value.GetDouble();
            if (Math.Abs(value * 100 - Math.Round(value * 100)) > epsilon) return false;
            lower += Math.Max(0, value - 0.005);
            upper += Math.Min(1, value + 0.005);
        }
        return lower <= 1 + epsilon && upper >= 1 - epsilon;
    }

    private static void Description(JsonElement element)
    {
        if (element.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array)) throw Invalid();
    }
    private static double Probability(JsonElement element)
    {
        var p = element.GetDouble();
        if (!double.IsFinite(p) || p is < 0 or > 1) throw Invalid();
        return p;
    }
    private static void UniqueObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!names.Add(property.Name)) throw Invalid();
    }
    private static void ExactKeys(JsonElement element, IEnumerable<string> expected)
    {
        UniqueObject(element);
        if (!expected.ToHashSet(StringComparer.Ordinal).SetEquals(element.EnumerateObject().Select(p => p.Name))) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("Invalid System 1 decision contract.");
}
