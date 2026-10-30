using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.TestSupport.LocalModels;
using Xunit;

namespace Cortex.Inference.Tests.LocalModels;

/// <summary>
/// One typed decision asked of a local model: a <c>choice</c> over <see cref="Options"/> or a <c>boolean</c>, with an
/// unambiguous <see cref="Expected"/> answer (an option, or <c>"true"</c>/<c>"false"</c>). <see cref="Task"/> becomes
/// <c>request.System</c>; the schema has the single field <see cref="Field"/>.
/// </summary>
internal sealed record LocalDecisionCase(string Id, string Language, string State, string Task, string Field, bool Boolean,
    string[] Options, string? Description, string Expected)
{
    internal const string Yes = "Yes";
    internal const string No = "No";

    /// <summary>Cyclic rotations of the option order: n for a choice, 2 for a boolean (Yes/No and No/Yes).</summary>
    internal int Rotations => Boolean ? 2 : Options.Length;

    /// <summary>The option the expected answer maps to: the choice itself, or <c>Yes</c>/<c>No</c> for a boolean.</summary>
    internal string ExpectedOption => Boolean ? (Expected == "true" ? Yes : No) : Expected;

    /// <summary>Options in cyclic rotation <paramref name="shift"/>: position i holds original option (i + shift) mod n.</summary>
    internal string[] Rotated(int shift)
    {
        var options = Boolean ? new[] { Yes, No } : Options;
        return options.Select((_, i) => options[(i + shift) % options.Length]).ToArray();
    }

    /// <summary>The closed single-field schema; a choice's enum lists the options in rotation <paramref name="shift"/>.</summary>
    internal JsonElement Schema(int shift = 0)
    {
        var property = new JsonObject();
        if (Boolean) property["type"] = "boolean";
        else
        {
            property["type"] = "string";
            property["enum"] = new JsonArray(Rotated(shift).Select(o => (JsonNode)JsonValue.Create(o)!).ToArray());
        }
        if (Description is not null) property["description"] = Description;
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { [Field] = property },
            ["required"] = new JsonArray(JsonValue.Create(Field)),
            ["additionalProperties"] = false,
        };
        return JsonSerializer.SerializeToElement(schema);
    }

    /// <summary>The per-field question exactly as <c>OpenJevFlowCompletion</c> builds it: task, blank line, description or field name.</summary>
    internal string OpenJevQuestion => $"{Task}\n\n{(string.IsNullOrWhiteSpace(Description) ? Field : Description)}";

    /// <summary>The benchmark corpus, <c>tests/fixtures/local-decision-cases.json</c>, validated on load.</summary>
    internal static IReadOnlyList<LocalDecisionCase> LoadCorpus()
    {
        var path = Path.Combine(LocalModelManifest.RepositoryRoot(), "tests", "fixtures", "local-decision-cases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cases = new List<LocalDecisionCase>();
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            var type = item.GetProperty("type").GetString();
            var boolean = type switch
            {
                "boolean" => true,
                "choice" => false,
                _ => throw new InvalidDataException($"{path}: case '{id}' has type '{type}', expected choice or boolean."),
            };
            var options = boolean ? [] : item.GetProperty("options").EnumerateArray().Select(o => o.GetString()!).ToArray();
            var expected = item.GetProperty("expected");
            var answer = boolean
                ? expected.ValueKind switch
                {
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => throw new InvalidDataException($"{path}: boolean case '{id}' needs a JSON boolean 'expected'."),
                }
                : expected.GetString()!;
            var language = item.GetProperty("language").GetString()!;
            if (language is not ("en" or "pt")) throw new InvalidDataException($"{path}: case '{id}' has language '{language}', expected en or pt.");
            if (!boolean && (options.Length is < 2 or > 5 || options.Distinct(StringComparer.Ordinal).Count() != options.Length || !options.Contains(answer)))
                throw new InvalidDataException($"{path}: choice case '{id}' needs 2 to 5 distinct options that include the expected answer.");
            cases.Add(new(id, language, item.GetProperty("state").GetString()!, item.GetProperty("task").GetString()!,
                item.GetProperty("field").GetString()!, boolean, options,
                item.TryGetProperty("description", out var description) ? description.GetString() : null, answer));
        }
        Assert.Equal(cases.Count, cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        return cases;
    }
}
