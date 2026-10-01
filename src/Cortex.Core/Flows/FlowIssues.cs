namespace Cortex.Core.Flows;

public static class FlowLimits
{
    public const int MaxInputUtf8Bytes = 1 * 1024 * 1024;
    public const int MaxJsonParseDepth = 64;
    public const int MaxSchemaDepth = 16;
    public const int MaxSchemaFields = 256;
    public const int MaxNodes = 256;
    public const int MaxEdges = 1024;
    public const int MaxPromptUtf8Bytes = 32 * 1024;
}

public enum FlowIssueSeverity
{
    Error,
    Warning,
}

public sealed record FlowIssue(
    string Code,
    string Path,
    string Message,
    FlowIssueSeverity Severity = FlowIssueSeverity.Error);

public sealed class FlowIssues : IReadOnlyList<FlowIssue>
{
    private readonly List<FlowIssue> _items = [];

    public int Count => _items.Count;

    public FlowIssue this[int index] => _items[index];

    internal void Add(string code, string path, string message) => _items.Add(new FlowIssue(code, path, message));

    public IEnumerator<FlowIssue> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
