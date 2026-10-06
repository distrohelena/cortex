using System.Diagnostics;
using System.Text.Json;
using Cortex.Core.Plugins;
using Cortex.Editor;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
var flags = new HashSet<string>(StringComparer.Ordinal);
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--no-open" or "--diagnostics" or "--use") { flags.Add(args[i]); continue; }
    if (args[i] == "--help") { Console.WriteLine("Cortex Editor: --graph <json> --entry <id> --settings <json> --installation <pasta> --port <n> --no-open --diagnostics --use"); return; }
    if (args[i] is not ("--graph" or "--entry" or "--settings" or "--installation" or "--port" or "--application-root") || i + 1 >= args.Length || !arguments.TryAdd(args[i], args[++i]))
        throw new ArgumentException("Invalid or duplicate argument.");
}
var options = new EditorLaunchOptions(arguments.GetValueOrDefault("--graph"), arguments.GetValueOrDefault("--entry"), arguments.GetValueOrDefault("--settings"),
    arguments.GetValueOrDefault("--installation"), int.Parse(arguments.GetValueOrDefault("--port", "0")), !flags.Contains("--no-open"), flags.Contains("--diagnostics"), flags.Contains("--use"), arguments.GetValueOrDefault("--application-root"));
await using var app = await EditorHost.BuildAsync(options);
if (options.Diagnostics)
{
    var runtime = app.Services.GetRequiredService<EditorRuntime>();
    Console.WriteLine(JsonSerializer.Serialize(new { loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Order(), runtime.Plugins.Reports, runtime.SettingsError }, CortexSettingsStore.JsonOptions));
    runtime.Plugins.Dispose();
    return;
}
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var url = app.Services.GetRequiredService<EditorSession>().BootstrapUrl(address);
Console.WriteLine("Cortex Editor: " + url);
if (options.OpenBrowser) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
await app.WaitForShutdownAsync();
