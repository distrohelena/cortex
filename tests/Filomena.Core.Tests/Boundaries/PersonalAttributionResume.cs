namespace Filomena.Core.Tests.Boundaries;

internal static class PersonalAttributionResume
{
    internal static bool Technical(string status) => status is "technical_error" or "transport_failure" or "provider_failure" or "timeout";
    internal static string[] Pending(IReadOnlyList<PersonalAttributionSpikeLive.Row> rows, string id, int repetition)
    {
        var pending = new List<string>();
        foreach (var strategy in repetition % 2 == 0 ? new[] { "C", "A" } : new[] { "A", "C" })
        {
            if (rows.Any(r => r.Id == id && r.Strategy == strategy && r.Repetition == repetition && r.Status == "valid")) continue;
            if (rows.Count(r => r.Id == id && r.Strategy == strategy && Technical(r.Status)) >= 3)
                throw new InvalidOperationException($"Three technical failures: {id}/{strategy}; stop spike.");
            pending.Add(strategy);
        }
        return pending.ToArray();
    }
}
