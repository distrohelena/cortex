using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Storage;
using Npgsql;
using Xunit;
using Filomena.Llm.LmStudio;

namespace Filomena.Evaluation.Tests;

public sealed class CompletionTrialAcceptanceTests
{
    [Fact]
    public async Task Controlled_loopback_fixture_serves_actual_adapter_without_a_model()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = ServeOnceAsync(listener, () => Task.CompletedTask, stop.Token);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var endpoint = new Uri($"http://127.0.0.1:{port}/v1/");
            using var http = new HttpClient(new EvaluationHttpHandler("lmstudio", endpoint, new ControlledJournal())
            { InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false } });
            var adapter = new LmStudioClient(http, new LmStudioOptions
            {
                Endpoint = endpoint, Model = "controlled-local", RequestTimeoutSeconds = 10
            });
            Assert.Equal("controlled output", await adapter.CompleteResponseAsync("Instruction", "Input", stop.Token));
            Assert.Equal("controlled-local", (await serving).GetProperty("model").GetString());
        }
        finally
        {
            await stop.CancelAsync();
            listener.Stop();
            try { await serving; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Complete_trial_reserves_before_wire_persists_output_and_cannot_run_again()
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires migrated isolated PostgreSQL.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var proposer = EvaluationAccess.FromHost("acceptance", scope, EvaluationRole.Proposer);
        var evaluator = EvaluationAccess.FromHost("acceptance", scope, EvaluationRole.Evaluator);
        var store = new PostgresEvaluationStore(source);
        var token = TestContext.Current.CancellationToken;
        async Task<ScopedRevisionRef> Save(RevisionDocument document) =>
            new(scope, await store.PutRevisionAsync(proposer, document, token));
        var target = await Save(new(1, "completion-target", Json(new { nodeId = "controlled-step" })));
        var input = await Save(new(1, "completion-input", Json(new { message = "controlled input" })));
        var profile = new ConfigurationProfile(1, "completion", "lmstudio", "controlled-local", "controlled-host",
            "controlled-local", DateTimeOffset.UnixEpoch,
            Json(new { arbitrationTemperature = 0.1, arbitrationMaxTokens = 256, generationTemperature = 0.7,
                generationMaxTokens = 256, requestTimeoutSeconds = 10 }), null, [],
            new(1, "schema", Json(new { contractVersion = 1, responseSchema = (object?)null })),
            new(1, "prompt", Json(new { system = "Controlled instruction." })), null);
        var candidate = await Save(profile.ToRevisionDocument());
        var metadata = await Save(new(1, "test-metadata", Json(new { synthetic = true })));
        var manifest = new ExperimentManifest(scope, target, candidate, metadata, metadata, metadata,
            [candidate], 1, 1, CandidateOrder.Interleaved, new(1, 1000, 1000, 1), new(1, 1, null));
        var experiment = Guid.NewGuid();
        await new ExperimentCoordinator(source).PlanAsync(evaluator, experiment, manifest, [input], token);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var serving = ServeOnceAsync(listener, async () =>
        {
            // Another connection sees a committed reservation before the fake provider answers.
            await using var check = source.CreateCommand("""
                SELECT e.reserved_calls,a.state,t.state FROM evaluation.experiments e
                JOIN evaluation.trials t USING (workspace_id,project_id)
                JOIN evaluation.attempts a ON a.workspace_id=t.workspace_id AND a.project_id=t.project_id AND a.trial_id=t.id
                WHERE e.workspace_id=@workspace AND e.id=@experiment AND t.experiment_id=e.id
                """);
            check.Parameters.AddWithValue("workspace", scope.WorkspaceId);
            check.Parameters.AddWithValue("experiment", experiment);
            await using var reader = await check.ExecuteReaderAsync(stop.Token);
            Assert.True(await reader.ReadAsync(stop.Token));
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal("started", reader.GetString(1));
            Assert.Equal("running", reader.GetString(2));
            Assert.False(await reader.ReadAsync(stop.Token));
        }, stop.Token);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            EvaluationEndpointSettings[] endpoints = [new("controlled-host", "lmstudio",
                new($"http://127.0.0.1:{port}/v1/"), () => null, new(100, 100, 0), TimeSpan.FromSeconds(10))];
            var result = await new CompletionTrialExecutor(source).ExecuteNextAsync(evaluator, experiment, endpoints, token);
            Assert.NotNull(result);
            var request = await serving;
            Assert.Equal("controlled-local", request.GetProperty("model").GetString());
            var stored = await store.GetArtifactAsync(evaluator, result.Result, token);
            using var output = JsonDocument.Parse(stored.Document.Content);
            Assert.Equal("controlled output", output.RootElement.GetProperty("output").GetString());
            Assert.Equal(candidate.Revision.Sha256, output.RootElement.GetProperty("profileSha256").GetString());
            Assert.Equal(input.Revision.Sha256, output.RootElement.GetProperty("inputSha256").GetString());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CompletionTrialExecutor(source).ExecuteNextAsync(evaluator, experiment, endpoints, token));
            await using var completed = source.CreateCommand("""
                SELECT execution_state,reserved_calls,
                  (SELECT count(*) FROM evaluation.attempts WHERE workspace_id=@workspace AND state='observed')
                FROM evaluation.experiments WHERE workspace_id=@workspace AND id=@experiment
                """);
            completed.Parameters.AddWithValue("workspace", scope.WorkspaceId);
            completed.Parameters.AddWithValue("experiment", experiment);
            await using var final = await completed.ExecuteReaderAsync(token);
            Assert.True(await final.ReadAsync(token));
            Assert.Equal("completed", final.GetString(0));
            Assert.Equal(1L, final.GetInt64(1));
            Assert.Equal(1L, final.GetInt64(2));
        }
        finally
        {
            await stop.CancelAsync();
            listener.Stop();
            try { await serving; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    // A one-request HTTP/1.1 fixture; never loads a model, accepts secrets or contacts external hosts.
    private static async Task<JsonElement> ServeOnceAsync(TcpListener listener, Func<Task> beforeResponse,
        CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        await using var stream = client.GetStream();
        var header = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(one, token);
            header.Add(one[0]);
            if (header.Count > 16_384) throw new InvalidDataException("Test request headers too large.");
            if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
        }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        Assert.Equal("POST /v1/chat/completions HTTP/1.1", lines[0]);
        var length = int.Parse(Assert.Single(lines, line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(length, 1, 1_048_576);
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        using var request = JsonDocument.Parse(body);
        await beforeResponse();
        var response = Encoding.UTF8.GetBytes("""{"model":"controlled-local","choices":[{"message":{"content":"controlled output"}}],"usage":{"prompt_tokens":12,"completion_tokens":3}}""");
        var responseHeader = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseHeader, token);
        await stream.WriteAsync(response, token);
        return request.RootElement.Clone();
    }

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    // Only the fixture smoke uses this journal. PostgreSQL acceptance uses AttemptLedger.
    private sealed class ControlledJournal : IEvaluationCallJournal
    {
        public Task<Guid> BeginAsync(PhysicalCallIntent intent, CancellationToken token) => Task.FromResult(Guid.NewGuid());
        public Task FinishAsync(Guid attempt, PhysicalCallOutcome outcome, CancellationToken token) => Task.CompletedTask;
    }
}
