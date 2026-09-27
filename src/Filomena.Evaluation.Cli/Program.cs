using System.Text;
using System.Text.Json;
using Filomena.Evaluation.Tools;

namespace Filomena.Evaluation.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        OfflineToolResponse response;
        if (args is not ["offline"])
        {
            response = OfflineEvaluationTools.Error("usage_offline_json_stdin_required");
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            try
            {
                var text = new StringBuilder();
                var chunk = new char[4096];
                var oversized = false;
                while (true)
                {
                    var read = await Console.In.ReadAsync(chunk.AsMemory(), cancellation.Token);
                    if (read == 0) break;
                    if (text.Length + read > 1_048_576) { oversized = true; break; }
                    text.Append(chunk, 0, read);
                }
                response = oversized ? OfflineEvaluationTools.Error("request_too_large") : OfflineEvaluationTools.Invoke(text.ToString());
            }
            catch (OperationCanceledException) { response = OfflineEvaluationTools.Error("cancelled"); }
            catch (IOException) { response = OfflineEvaluationTools.Error("stdin_unavailable"); }
        }
        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return response.Success ? 0 : 2;
    }
}
