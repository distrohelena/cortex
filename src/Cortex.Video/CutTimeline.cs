namespace Cortex.Video;

public static class CutTimeline
{
    public static IReadOnlyList<ProposedTakeClip> Build(TakeEditPlan plan, double sourceDuration)
    {
        if (plan.Version != 1 || plan.Timebase != "sourceSeconds" || plan.Candidates is null || plan.Candidates.Count > 32)
            throw new InvalidDataException("Use a version 1 edit plan with sourceSeconds and at most 32 candidates.");
        if (!double.IsFinite(sourceDuration) || sourceDuration <= 0)
            throw new InvalidDataException("Source duration must be finite and positive.");
        var timeline = new List<ProposedTakeClip>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        double cursor = 0;
        foreach (var candidate in plan.Candidates)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.Id) || !ids.Add(candidate.Id))
                throw new InvalidDataException("Candidates require unique nonempty IDs.");
            if (!candidate.Keep) continue;
            var source = candidate.Source;
            if (source is null || !double.IsFinite(source.Start) || !double.IsFinite(source.End) ||
                source.Start < 0 || source.End <= source.Start || source.End > sourceDuration)
                throw new InvalidDataException("Selected intervals must be finite, positive and within the source video.");
            var end = cursor + source.End - source.Start;
            timeline.Add(new(candidate.Id, source.Start, source.End, cursor, end));
            cursor = end;
        }
        if (timeline.Count == 0) throw new InvalidDataException("Select at least one candidate with keep=true before rendering.");
        return timeline;
    }
}
