using System.Text;
using System.Text.Json;

namespace Cortex.Core.Plugins;

public sealed class CortexSettingsStore
{
    private readonly SemaphoreSlim _writer = new(1, 1);
    public CortexSettingsStore(string? path = null) => FilePath = Path.GetFullPath(path ?? DefaultPath);
    public string FilePath { get; }
    public string DirectoryPath => Path.GetDirectoryName(FilePath)!;
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cortex", "editor");
    public static string DefaultPath => Path.Combine(DefaultDirectory, "settings.json");
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, PropertyNameCaseInsensitive = false };

    public async Task<CortexSettingsReadResult> ReadAsync(CancellationToken token)
    {
        string? text = null;
        try
        {
            if (!File.Exists(FilePath)) return new(new());
            if (new FileInfo(FilePath).Length > 1048576) throw new InvalidDataException("Settings excedem 1 MiB.");
            text = await File.ReadAllTextAsync(FilePath, token).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<CortexEditorSettings>(text, JsonOptions) ?? throw new InvalidDataException("Settings vazias.");
            settings.Validate();
            return new(settings, OriginalText: text);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        { return new(new(), "Não foi possível ler as settings: " + error.Message, text); }
    }

    public async Task SaveAsync(CortexEditorSettings settings, CancellationToken token)
    {
        settings.Validate();
        var content = JsonSerializer.Serialize(settings, JsonOptions);
        if (Encoding.UTF8.GetByteCount(content) > 1048576) throw new InvalidDataException("Settings excedem 1 MiB.");
        await _writer.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var old = await ReadAsync(token).ConfigureAwait(false);
            if (old.Error is not null && File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".invalid-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json", overwrite: false);
            temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), token).ConfigureAwait(false);
            File.Move(temporary, FilePath, overwrite: true);
            temporary = null;
        }
        finally
        {
            if (temporary is not null) File.Delete(temporary);
            _writer.Release();
        }
    }
}
