using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cortex.Video;

/// <summary>Groups actual Whisper token intervals into whitespace-delimited Portuguese words.</summary>
public static class WhisperWordTimestamps
{
    public const string Method = "whisper_cpp_token_timestamps";
    private sealed record TokenSpan(int Offset, int Length, double? Start, double? End, bool Lexical);

    public static IReadOnlyList<TranscriptWord> Read(JsonElement tokens, TranscriptSegment segment)
    {
        if (tokens.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Whisper tokens must be an array.");
        var text = new StringBuilder();
        var spans = new List<TokenSpan>();
        double previous = segment.Start;
        foreach (var token in tokens.EnumerateArray())
        {
            if (!token.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Invalid Whisper token text.");
            var part = value.GetString()!;
            if (part.StartsWith("[_", StringComparison.Ordinal) && part.EndsWith(']')) continue;
            var lexical = Regex.IsMatch(part, @"[\p{L}\p{N}]");
            double? start = null, end = null;
            // Punctuation has no spoken interval of its own. It remains attached to the word.
            if (lexical && token.TryGetProperty("offsets", out var offsets))
            {
                if (offsets.ValueKind != JsonValueKind.Object || !offsets.TryGetProperty("from", out var from) || !from.TryGetDouble(out var startMs)
                    || !offsets.TryGetProperty("to", out var to) || !to.TryGetDouble(out var endMs))
                    throw new InvalidDataException("Invalid Whisper token offsets.");
                start = startMs / 1000; end = endMs / 1000;
                if (!double.IsFinite(start.Value) || !double.IsFinite(end.Value) || start < segment.Start || end > segment.End
                    || end < start || start < previous)
                    throw new InvalidDataException("Invalid Whisper word timestamps.");
                previous = start.Value;
            }
            spans.Add(new(text.Length, part.Length, start, end, lexical));
            text.Append(part);
        }
        var decoded = text.ToString();
        static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
        if (Normalize(decoded) != Normalize(segment.Text))
            throw new InvalidDataException("Whisper token text does not match its segment.");
        var words = new List<TranscriptWord>();
        foreach (Match word in Regex.Matches(decoded, @"\S+"))
        {
            var lexical = spans.Where(span => span.Lexical && span.Offset < word.Index + word.Length && span.Offset + span.Length > word.Index).ToArray();
            // Never spread segment time over a word whose lexical tokens were not aligned.
            if (lexical.Length == 0 || lexical.Any(span => span.Start is null || span.End is null)) continue;
            words.Add(new(lexical[0].Start!.Value, lexical.Max(span => span.End!.Value), word.Value));
        }
        return words;
    }
}
