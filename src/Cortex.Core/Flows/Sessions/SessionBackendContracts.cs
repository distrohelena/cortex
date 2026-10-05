using System.Text.Json;
using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Sessions;

internal sealed record LocalPageCursor(int Version, string SessionId, string Collection, int Offset, int Through, string FirstItem);
internal static class LocalBackendPaging
{
    internal static ArtifactPage<T> Page<T>(SessionKey session, string collection, IReadOnlyList<T> items, PageRequest page, Func<T, string> identity)
    {
        page.Validate();
        var sessionId = DurableIdentity.SessionWorkflowId("local-harness", session);
        var first = items.Count == 0 ? "" : identity(items[0]);
        LocalPageCursor cursor;
        try { cursor = page.Cursor is null ? new(1, sessionId, collection, 0, items.Count, first)
            : JsonSerializer.Deserialize<LocalPageCursor>(Convert.FromBase64String(page.Cursor)) ?? throw new InvalidDataException("Missing local cursor."); }
        catch (Exception error) when (error is FormatException or JsonException) { throw new InvalidDataException("Invalid local cursor.", error); }
        if (cursor.Version != 1 || cursor.SessionId != sessionId || cursor.Collection != collection || cursor.Offset < 0 || cursor.Through < cursor.Offset
            || cursor.Through > items.Count || cursor.FirstItem != first) throw new InvalidDataException("Foreign or stale local cursor.");
        var count = Math.Min(page.Limit, cursor.Through - cursor.Offset);
        var result = items.Skip(cursor.Offset).Take(count).ToArray();
        var nextOffset = cursor.Offset + count;
        var next = nextOffset < cursor.Through ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor with { Offset = nextOffset })) : null;
        return new(result, next);
    }
}
