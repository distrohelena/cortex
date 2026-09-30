using System.Security.Cryptography;
using System.Text;

namespace Filomena.App.Playground;

internal sealed class PlaygroundAssets(string? sourceDirectory)
{
    public static readonly (string Route, string File, string Type)[] Files =
    [
        ("/", "index.html", "text/html"), ("/flow", "flow.html", "text/html"),
        ("/flow.css", "flow.css", "text/css"), ("/flow.js", "flow.js", "text/javascript"),
        ("/flow-model.mjs", "flow-model.mjs", "text/javascript"),
        ("/flow-contract.mjs", "flow-contract.mjs", "text/javascript"),
        ("/flow-typed-panel.mjs", "flow-typed-panel.mjs", "text/javascript"),
        ("/flow-layout.mjs", "flow-layout.mjs", "text/javascript"),
        ("/flow-camera.mjs", "flow-camera.mjs", "text/javascript"),
        ("/flow-catalog.mjs", "flow-catalog.mjs", "text/javascript"),
        ("/flow-graph/index.js", "flow-graph/index.js", "text/javascript"),
        ("/flow-graph/camera.js", "flow-graph/camera.js", "text/javascript"),
        ("/flow-graph/editor.css", "flow-graph/editor.css", "text/css")
    ];

    public string Read(string file, bool html = false)
    {
        // Callers use the explicit route table only. No request path becomes a filesystem path.
        var revision = html && sourceDirectory is not null ? Revision() : null;
        string text;
        if (sourceDirectory is not null)
            text = File.ReadAllText(Path.Combine(sourceDirectory, file));
        else
        {
            using var stream = typeof(PlaygroundAssets).Assembly.GetManifestResourceStream($"Filomena.App.Playground.{file.Replace('/', '.')}")!;
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        return revision is null ? text : text + $"\n<script src=\"/__playground/live-reload.js?revision={revision}\"></script>";
    }

    public string Revision()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Files.Select(f => f.File).Append("live-reload.js").Append("model-catalog.json"))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file + "\0"));
            var path = Path.Combine(sourceDirectory!, file);
            if (File.Exists(path)) hash.AppendData(File.ReadAllBytes(path));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string FindDevelopmentDirectory()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var source = Path.Combine(current.FullName, "Playground");
            if (File.Exists(Path.Combine(current.FullName, "Filomena.App.csproj")) && File.Exists(Path.Combine(source, "flow.html")))
                return source;
        }
        throw new InvalidOperationException("--live-reload requires a build inside the Filomena.App source tree. Published executables should omit this flag.");
    }
}
