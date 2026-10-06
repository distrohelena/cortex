using System.Text.RegularExpressions;

namespace Cortex.Video;

public static class TakeCandidates
{
    public const int MaxCandidates = 32;
    public static IReadOnlyList<TakeCandidate> Build(TranscriptResult transcript)
    {
        if (transcript.Segments is null || transcript.Segments.Count > 512) throw new InvalidDataException("Use uma transcrição com até 512 segmentos neste recorte.");
        var candidates = new List<TakeCandidate>();
        var parts = new List<string>(); var ids = new List<int>();
        double start = 0, end = 0, previousEnd = 0;
        void Flush()
        {
            if (parts.Count == 0) return;
            candidates.Add(new("take-" + (candidates.Count + 1).ToString("D4"), new(start, end, ids.ToArray()), string.Join(" ", parts)));
            parts.Clear(); ids.Clear();
            if (candidates.Count > MaxCandidates) throw new InvalidDataException("A transcrição gera mais de 32 candidatos; reduza o trecho antes de avaliar.");
        }
        for (var index = 0; index < transcript.Segments.Count; index++)
        {
            var segment = transcript.Segments[index];
            if (segment is null || segment.Text is null || !double.IsFinite(segment.Start) || !double.IsFinite(segment.End) || segment.Start < previousEnd || segment.End <= segment.Start)
                throw new InvalidDataException("Segmentos precisam de texto e tempos crescentes válidos.");
            previousEnd = segment.End;
            var speech = Regex.Replace(segment.Text, @"\[[^\]]{1,64}\]", " ").Trim();
            if (!Regex.IsMatch(speech, @"[\p{L}\p{N}]")) { Flush(); continue; }
            if (parts.Count > 0 && (segment.Start - end > .7 || segment.End - start > 30)) Flush();
            if (parts.Count == 0) start = segment.Start;
            end = segment.End; parts.Add(segment.Text.Trim()); ids.Add(index);
            if (Regex.IsMatch(speech, "[.!?…][\\p{P}\\p{Zs}]*$")) Flush();
        }
        Flush(); return candidates;
    }

    public static string Normalize(string text) => string.Join(" ", Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(match => match.Value));
    public static bool PotentialRepeat(TakeCandidate first, TakeCandidate second)
    {
        var a = Normalize(first.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var b = Normalize(second.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var shared = a.Intersect(b).Count(); var union = a.Union(b).Count();
        return shared >= 3 && union > 0 && (double)shared / union >= .65;
    }
}
