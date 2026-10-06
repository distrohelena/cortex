namespace Cortex.Video.Editing;

public sealed record LoadedEditScript(EditScriptDocument Document, string OriginalJson, EditingDocumentReference Reference)
{
    public string BaseDirectory => Path.GetDirectoryName(Reference.Path)!;
    public string ResolveLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://", StringComparison.Ordinal)) throw new InvalidDataException("Use a local filesystem path.");
        return Path.GetFullPath(path, BaseDirectory);
    }
}
