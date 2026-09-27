using System.Net;
using System.Text;
using Filomena.Evaluation.Execution;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class EvaluationHttpHandlerTests
{
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");

    [Fact]
    public async Task Reservation_failure_prevents_physical_dispatch()
    {
        var journal = new Journal { Refuse = true };
        using var transport = new Transport();
        using var client = Client(journal, transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(client));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Each_physical_call_has_its_own_reservation_before_send_and_safe_observation()
    {
        var journal = new Journal();
        using var transport = new Transport(() => Assert.Single(journal.Intents));
        using var client = Client(journal, transport);
        using var response = await Send(client);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(Transport.Body, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var intent = Assert.Single(journal.Intents);
        Assert.Equal(64, intent.RequestSha256.Length);
        Assert.Equal(2, intent.RequestBytes);
        var outcome = Assert.Single(journal.Outcomes);
        Assert.Equal("response_received", outcome.Status);
        Assert.Equal(12, outcome.Observation!.Usage!.InputTokens);
        Assert.Null(outcome.ErrorCode);
    }

    [Fact]
    public async Task Unknown_endpoint_is_rejected_before_reservation()
    {
        var journal = new Journal();
        using var transport = new Transport();
        using var client = Client(journal, transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.PostAsync("https://other.example/", new StringContent("{}"), TestContext.Current.CancellationToken));
        Assert.Empty(journal.Intents);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Network_failure_is_recorded_indeterminate_without_retry_or_exception_text()
    {
        var journal = new Journal();
        using var transport = new Transport { Fail = true };
        using var client = Client(journal, transport);
        await Assert.ThrowsAsync<HttpRequestException>(() => Send(client));
        Assert.Equal(1, transport.Calls);
        var outcome = Assert.Single(journal.Outcomes);
        Assert.Equal("indeterminate", outcome.Status);
        Assert.Equal("transport_failure", outcome.ErrorCode);
        Assert.Null(outcome.Observation);
    }

    [Fact]
    public async Task Persistence_failure_after_response_does_not_retry_send_or_claim_completion()
    {
        var journal = new Journal { FailFinish = true };
        using var transport = new Transport();
        using var client = Client(journal, transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(client));
        Assert.Equal(1, transport.Calls);
        Assert.Equal(1, journal.FinishCalls);
    }

    [Fact]
    public async Task Missing_usage_remains_unavailable_and_body_still_reaches_real_adapter()
    {
        var journal = new Journal();
        using var transport = new Transport { ResponseBody = "{\"status\":\"completed\"}" };
        using var client = Client(journal, transport);
        using var response = await Send(client);
        Assert.Equal(transport.ResponseBody, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("unavailable", Assert.Single(journal.Outcomes).Observation!.UsageStatus);
    }

    [Fact]
    public async Task Oversized_response_is_recorded_without_retry()
    {
        var journal = new Journal();
        using var transport = new Transport { ResponseBody = new string('x', 1_048_577) };
        using var client = Client(journal, transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(client));
        Assert.Equal(1, transport.Calls);
        Assert.Equal("response_too_large", Assert.Single(journal.Outcomes).ErrorCode);
    }

    private static HttpClient Client(Journal journal, Transport transport) =>
        new(new EvaluationHttpHandler("openai", Endpoint, journal) { InnerHandler = transport });

    private static Task<HttpResponseMessage> Send(HttpClient client) =>
        client.PostAsync(Endpoint, new StringContent("{}", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    private sealed class Journal : IEvaluationCallJournal
    {
        public bool Refuse { get; init; }
        public bool FailFinish { get; init; }
        public int FinishCalls { get; private set; }
        public List<PhysicalCallIntent> Intents { get; } = [];
        public List<PhysicalCallOutcome> Outcomes { get; } = [];
        public Task<Guid> BeginAsync(PhysicalCallIntent intent, CancellationToken cancellationToken)
        {
            if (Refuse) throw new InvalidOperationException("budget_or_store_unavailable");
            Intents.Add(intent);
            return Task.FromResult(Guid.NewGuid());
        }
        public Task FinishAsync(Guid attempt, PhysicalCallOutcome outcome, CancellationToken cancellationToken)
        {
            FinishCalls++;
            if (FailFinish) throw new InvalidOperationException("store_unavailable");
            Outcomes.Add(outcome);
            return Task.CompletedTask;
        }
    }

    private sealed class Transport(Action? before = null) : HttpMessageHandler
    {
        public const string Body = "{\"model\":\"gpt-6-luna\",\"status\":\"completed\",\"usage\":{\"input_tokens\":12,\"output_tokens\":3}}";
        public string ResponseBody { get; init; } = Body;
        public bool Fail { get; init; }
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            before?.Invoke();
            Calls++;
            if (Fail) throw new HttpRequestException("sensitive-remote-error-must-not-be-recorded");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseBody) });
        }
    }
}
