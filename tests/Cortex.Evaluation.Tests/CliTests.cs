using System.Text.Json;
using Cortex.Evaluation.Cli;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class CliTests
{
    private const string HashRequest = """{"contractVersion":1,"operation":"revision.hash","payload":{"contractVersion":1,"kind":"test","payload":{"a":1}}}""";

    [Fact]
    public async Task Offline_cli_returns_one_json_line_and_the_real_tool_result()
    {
        var (code, json) = await Run(new(), ["offline"], HashRequest, TestContext.Current.CancellationToken);
        Assert.Equal(0, code);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(OfflineEvaluationTools.Invoke(HashRequest).Result!.Value.GetProperty("sha256").GetString(),
            json.GetProperty("result").GetProperty("sha256").GetString());
    }

    [Theory]
    [InlineData("persisted")]
    [InlineData("--role")]
    public async Task Arguments_never_bootstrap_privileged_access(string arg)
    {
        var (code, json) = await Run(new(), [arg], "secret input not an authority", TestContext.Current.CancellationToken);
        Assert.Equal(2, code);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(arg == "persisted" ? "host_binding_required" : "usage_offline_json_stdin_required",
            json.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("secret", json.GetRawText());
    }

    [Fact]
    public async Task Bound_persisted_mode_uses_host_role_not_request_role()
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("cli-proposer", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Proposer);
        var dispatcher = new CommandDispatcher(new PersistedEvaluationTools(source), access);
        var (_, json) = await Run(dispatcher, ["persisted"],
            """{"contractVersion":1,"operation":"experiment.budget.approve","payload":{}}""", TestContext.Current.CancellationToken);
        Assert.Equal("unauthorized", json.GetProperty("errorCode").GetString());
        Assert.Equal("not_attempted", json.GetProperty("persistenceStatus").GetString());
    }

    [Fact]
    public async Task Cancelled_input_returns_sanitized_json_without_dispatch()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var (_, json) = await Run(new(), ["offline"], HashRequest, cancelled.Token);
        Assert.Equal("cancelled", json.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData(EvaluationRole.Evaluator, "execution_binding_required")]
    [InlineData(EvaluationRole.Proposer, "unauthorized")]
    [InlineData(EvaluationRole.Operator, "unauthorized")]
    public async Task Run_requires_host_execution_binding_and_evaluate_authority(EvaluationRole role, string error)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("cli", new(Guid.NewGuid(), Guid.NewGuid()), role);
        var request = JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            operation = "experiment.run",
            payload = new { experimentId = Guid.NewGuid() }
        });
        var (code, json) = await Run(new(new PersistedEvaluationTools(source), access), ["persisted"], request,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, code);
        Assert.Equal(error, json.GetProperty("errorCode").GetString());
        Assert.Equal("not_attempted", json.GetProperty("persistenceStatus").GetString());
    }

    [Fact]
    public async Task Oversized_input_is_rejected_without_echoing_it()
    {
        var (_, json) = await Run(new(), ["offline"], new string('x', 1_048_577), TestContext.Current.CancellationToken);
        Assert.Equal("request_too_large", json.GetProperty("errorCode").GetString());
        Assert.True(json.GetRawText().Length < 512);
    }

    private static async Task<(int Code, JsonElement Json)> Run(CommandDispatcher dispatcher, string[] args, string request,
        CancellationToken token)
    {
        using var output = new StringWriter();
        var code = await dispatcher.RunAsync(args, new StringReader(request), output, token);
        var line = Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var json = JsonDocument.Parse(line);
        return (code, json.RootElement.Clone());
    }
}
