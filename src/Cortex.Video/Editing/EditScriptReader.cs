using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cortex.Video.Editing;

public static class EditScriptReader
{
    public const int MaxBytes = 4194304;

    public static async Task<LoadedEditScript> ReadAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaxBytes) throw new InvalidDataException("The script exceeds 4 MiB.");
        using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (bytes.Length + read > MaxBytes) throw new InvalidDataException("The script exceeds 4 MiB.");
            bytes.Write(buffer, 0, read);
        }
        token.ThrowIfCancellationRequested();
        var source = bytes.ToArray();
        try
        {
            var text = new UTF8Encoding(false, true).GetString(source);
            if (text.StartsWith('\uFEFF')) text = text[1..];
            using var json = JsonDocument.Parse(text);
            var document = EditingJson.Parse(json.RootElement) as EditScriptDocument ?? throw new InvalidDataException("Edit Script requires cortex.edit.v1 authored intent, not a generated plan.");
            EditingContractValidator.ThrowIfInvalid(document);
            token.ThrowIfCancellationRequested();
            return new(document, text, new() { Path = full, Revision = document.Revision, Sha256 = Convert.ToHexStringLower(SHA256.HashData(source)) });
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException)
        { throw new InvalidDataException("Invalid UTF-8 editorial script: " + error.Message, error); }
    }
}
