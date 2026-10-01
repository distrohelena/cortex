using System.Text.Json;
using Filomena.Llm.SystemOne;
using Cortex.Inference.SystemOne;

namespace Filomena.App;

public static class SystemOneCommand
{
    public static Task<int> RunAsync(string[] args, TextWriter output, CancellationToken token,
        HttpClient? http = null, Func<string, string?>? environment = null) => RunCoreAsync(args, output, token, http, environment ?? Environment.GetEnvironmentVariable);

    private static async Task<int> RunCoreAsync(string[] args, TextWriter output, CancellationToken token, HttpClient? supplied,
        Func<string, string?> environment)
    {
        using var owned = supplied is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : null;
        var http = supplied ?? owned!;
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--system-one") continue;
                if (args[i] is not ("--provider" or "--model" or "--input") || i + 1 >= args.Length || !values.TryAdd(args[i], args[++i]))
                    throw new InvalidDataException("Invalid command arguments.");
            }
            if (!values.TryGetValue("--provider", out var provider) || provider is not ("jev" or "laya") ||
                !values.TryGetValue("--model", out var model) || !values.TryGetValue("--input", out var file))
                throw new InvalidDataException("Required: --provider jev|laya --model MODEL --input JSON_FILE.");
            if (new FileInfo(file).Length > 1048576) throw new InvalidDataException("Decision input exceeds 1 MiB.");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file, token));
            var endpoint = new Uri(environment(provider == "jev" ? "FILOMENA_JEV_ENDPOINT" : "FILOMENA_LAYA_ENDPOINT") ??
                (provider == "jev" ? "https://api.typesafe.ai/v1/systemone" : "http://127.0.0.1:8000/v1/systemone"));
            if (provider == "laya" && !endpoint.IsLoopback) throw new InvalidDataException("Local Laya requires a loopback endpoint.");
            var policy = provider == "jev" ? environment("FILOMENA_JEV_PROBABILITY_POLICY") switch
            {
                null => SystemOneProbabilityPolicy.LayaFourDecimals,
                "jev-1.13-two-decimal-v1" => SystemOneProbabilityPolicy.Jev113TwoDecimals,
                _ => throw new InvalidDataException("Unknown probability policy.")
            } : SystemOneProbabilityPolicy.LayaFourDecimals;
            var client = new SystemOneClient(http, endpoint, () => environment(provider == "jev" ? "TYPESAFE_API_KEY" : "LAYA_API_KEY"), provider == "jev", policy);
            var result = await client.DecideAsync(model, document.RootElement.GetProperty("state"), document.RootElement.GetProperty("questions"), token);
            await output.WriteLineAsync(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or HttpRequestException or UriFormatException)
        {
            // Do not echo payloads, file paths, credentials or remote error bodies.
            await output.WriteLineAsync(JsonSerializer.Serialize(new { status = "failed", diagnostic = error is HttpRequestException ? "systemone_http_failure" : "systemone_configuration_or_contract_invalid" }));
            return 2;
        }
    }
}
