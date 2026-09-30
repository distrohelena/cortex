using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Tools;
using System.Text;
using System.Text.Json;

namespace Filomena.Evaluation.Cli;

public sealed class CommandDispatcher
{
    private readonly EvaluationApplication? application;
    private readonly EvaluationAccess? access;

    public CommandDispatcher(PersistedEvaluationTools? tools = null, EvaluationAccess? access = null)
    {
        application = tools is null ? null : new EvaluationApplication(tools);
        this.access = access;
    }

    public CommandDispatcher(EvaluationApplication application, EvaluationAccess access)
    {
        this.application = application ?? throw new ArgumentNullException(nameof(application));
        this.access = access ?? throw new ArgumentNullException(nameof(access));
    }

    public async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var persisted = args is ["persisted"];
        var invocationStarted = false;
        object response;
        if (args is not ["offline"] && !persisted)
            response = OfflineEvaluationTools.Error("usage_offline_json_stdin_required");
        else if (persisted && (application is null || access is null))
            response = Error("host_binding_required");
        else
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var text = new StringBuilder();
                var chunk = new char[4096];
                var oversized = false;
                while (true)
                {
                    var read = await input.ReadAsync(chunk.AsMemory(), token);
                    if (read == 0) break;
                    if (text.Length + read > 1_048_576) { oversized = true; break; }
                    text.Append(chunk, 0, read);
                }
                if (oversized || Encoding.UTF8.GetByteCount(text.ToString()) > 1_048_576)
                    response = Error("request_too_large");
                else if (persisted)
                {
                    invocationStarted = true;
                    response = await application!.InvokeAsync(access!, text.ToString(), token);
                }
                else
                    response = OfflineEvaluationTools.Invoke(text.ToString());
            }
            catch (OperationCanceledException) { response = Error("cancelled"); }
            catch (IOException) { response = Error(invocationStarted ? "operation_unconfirmed" : "stdin_unavailable"); }
        }
        // A cancelled input still gets its one final JSON response. No prompt,
        // credential, exception message or arbitrary command output is printed.
        await output.WriteLineAsync(JsonSerializer.Serialize(response, response.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return response switch
        {
            OfflineToolResponse offline when offline.Success => 0,
            PersistedToolResponse stored when stored.Success => 0,
            _ => 2
        };

        object Error(string code) => persisted
            ? new PersistedToolResponse(1, false, invocationStarted ? "unconfirmed" : "not_attempted", null, code)
            : OfflineEvaluationTools.Error(code);
    }
}
