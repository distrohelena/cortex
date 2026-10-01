using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class EvaluationApplicationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"experimentId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"experimentId\":\"11111111-1111-1111-1111-111111111111\",\"endpoint\":\"https://unauthorized.invalid\"}")]
    [InlineData("{\"experimentId\":\"11111111-1111-1111-1111-111111111111\",\"role\":\"operator\"}")]
    [InlineData("{\"experimentId\":\"11111111-1111-1111-1111-111111111111\",\"credential\":\"not-a-secret\"}")]
    [InlineData("{\"experimentId\":\"11111111-1111-1111-1111-111111111111\",\"maxCost\":100}")]
    [InlineData("{\"experimentId\":\"11111111-1111-1111-1111-111111111111\",\"experimentId\":\"22222222-2222-2222-2222-222222222222\"}")]
    public async Task Bound_run_rejects_request_authority_and_invalid_identity_before_credentials_or_storage(string payload)
    {
        using var source = Source();
        var reads = 0;
        var app = new EvaluationApplication(source, Endpoints(() => { reads++; return null; }));
        var response = await app.InvokeAsync(Access(),
            "{\"contractVersion\":1,\"operation\":\"experiment.run\",\"payload\":" + payload + "}",
            TestContext.Current.CancellationToken);
        Assert.False(response.Success);
        Assert.Equal("invalid_request", response.ErrorCode);
        Assert.Equal("not_attempted", response.PersistenceStatus);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task Cancellation_before_bound_run_does_not_enter_executor()
    {
        using var source = Source();
        var app = new EvaluationApplication(source, Endpoints(() => throw new InvalidOperationException("Must not read credentials.")));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var response = await app.InvokeAsync(Access(),
            """{"contractVersion":1,"operation":"experiment.run","payload":{"experimentId":"11111111-1111-1111-1111-111111111111"}}""",
            cancelled.Token);
        Assert.Equal("cancelled", response.ErrorCode);
        Assert.Equal("not_attempted", response.PersistenceStatus);
    }

    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Execution_binding_does_not_grant_evaluate(EvaluationRole role)
    {
        using var source = Source();
        var app = new EvaluationApplication(source, Endpoints(() => throw new InvalidOperationException("Must not read credentials.")));
        var response = await app.InvokeAsync(Access(role),
            """{"contractVersion":1,"operation":"experiment.run","payload":{"experimentId":"11111111-1111-1111-1111-111111111111"}}""",
            TestContext.Current.CancellationToken);
        Assert.Equal("unauthorized", response.ErrorCode);
        Assert.Equal("not_attempted", response.PersistenceStatus);
    }

    private static NpgsqlDataSource Source() =>
        NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
    private static EvaluationAccess Access(EvaluationRole role = EvaluationRole.Evaluator) =>
        EvaluationAccess.FromHost("application-tests", new(Guid.NewGuid(), Guid.NewGuid()), role);
    private static EvaluationEndpointSettings[] Endpoints(Func<string?> credential) =>
        [new("controlled", "lmstudio", new("http://127.0.0.1:1/v1/"), credential, new(100, 100, 0.01m), TimeSpan.FromSeconds(1))
            { ReservationCurrency = "BRL" }];
}
