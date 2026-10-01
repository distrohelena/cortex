using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Cortex.Evaluation.Storage;
using Npgsql;
using Xunit;
using Cortex.Inference.LmStudio;
using Cortex.Evaluation.Cli;
using Cortex.Evaluation.Tools;
using Cortex.Evaluation.Comparison;

namespace Cortex.Evaluation.Tests;

public sealed class CompletionTrialAcceptanceTests
{
    [Fact]
    [Trait("Category", "LocalSocket")]
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
                Endpoint = endpoint,
                Model = "controlled-local",
                RequestTimeoutSeconds = 10
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Postgres")]
    public async Task Bound_cli_and_real_executor_complete_or_stop_controlled_pipeline_without_replay(bool cancelAfterAdmission)
    {
        var connection = Environment.GetEnvironmentVariable("EVALUATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Requires disposable PostgreSQL through migration 009 and a controlled loopback HTTP fixture.");
        await using var source = NpgsqlDataSource.Create(connection!);
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var proposer = EvaluationAccess.FromHost("acceptance", scope, EvaluationRole.Proposer);
        var evaluator = EvaluationAccess.FromHost("acceptance", scope, EvaluationRole.Evaluator);
        var store = new PostgresEvaluationStore(source);
        var token = TestContext.Current.CancellationToken;
        var tools = new PersistedEvaluationTools(source);
        var application = new EvaluationApplication(tools);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        async Task<JsonElement> Call(EvaluationAccess hostAccess, string operation, object payload)
        {
            using var text = new StringReader(JsonSerializer.Serialize(new { contractVersion = 1, operation, payload }, jsonOptions));
            using var output = new StringWriter();
            var exit = await new CommandDispatcher(application, hostAccess).RunAsync(["persisted"], text, output, token);
            var line = Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var response = JsonDocument.Parse(line);
            Assert.Equal(0, exit);
            Assert.True(response.RootElement.GetProperty("success").GetBoolean(), response.RootElement.GetRawText());
            return response.RootElement.GetProperty("result").Clone();
        }
        async Task<ScopedRevisionRef> Save(RevisionDocument document) =>
            new(scope, (await Call(proposer, "revision.import", document)).Deserialize<RevisionRef>(jsonOptions)!);
        var target = await Save(new(1, "completion-target", Json(new { nodeId = "controlled-step" })));
        var input = await Save(new(1, "completion-input", Json(new { message = "controlled input" })));
        var profile = new ConfigurationProfile(1, "completion", "lmstudio", "controlled-local", "controlled-host",
            "controlled-local", DateTimeOffset.UnixEpoch,
            Json(new
            {
                arbitrationTemperature = 0.1,
                arbitrationMaxTokens = 256,
                generationTemperature = 0.7,
                generationMaxTokens = 256,
                requestTimeoutSeconds = 10
            }), null, [],
            new(1, "schema", Json(new { contractVersion = 1, responseSchema = (object?)null })),
            new(1, "prompt", Json(new { system = "Controlled instruction." })), null);
        var candidate = await Save(profile.ToRevisionDocument());
        var metadata = await Save(new(1, "test-metadata", Json(new { synthetic = true })));
        var manifest = new ExperimentManifest(scope, target, candidate, metadata, metadata, metadata,
            [candidate], 1, 1, CandidateOrder.Interleaved, new(1, 1000, 1000, 1), new(1, 1, null));
        var experiment = Guid.NewGuid();
        var plan = (await Call(evaluator, "experiment.plan", new { experimentId = experiment, manifest, cases = new[] { input } }))
            .Deserialize<ExperimentPlanReceipt>(jsonOptions)!;
        var operatorAccess = EvaluationAccess.FromHost("acceptance-operator", scope, EvaluationRole.Operator);
        await Call(operatorAccess, "experiment.budget.approve",
            new MonetaryBudgetApprovalRequest(1, Guid.NewGuid(), experiment, plan.PlanSha256, "BRL", manifest.Budgets.MaxCost));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var cancelRun = CancellationTokenSource.CreateLinkedTokenSource(token);
        var withheldResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            if (cancelAfterAdmission)
            {
                await cancelRun.CancelAsync();
                // No response is emitted; cleanup cancels this controlled wait.
                await withheldResponse.Task.WaitAsync(stop.Token);
            }
        }, stop.Token);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            EvaluationEndpointSettings[] endpoints = [new("controlled-host", "lmstudio",
                new($"http://127.0.0.1:{port}/v1/"), () => null, new(100, 100, 0.01m), TimeSpan.FromSeconds(10))
                { ReservationCurrency = "BRL" }];
            application = new EvaluationApplication(source, endpoints);
            if (cancelAfterAdmission)
            {
                var requestJson = JsonSerializer.Serialize(new
                {
                    contractVersion = 1,
                    operation = "experiment.run",
                    payload = new { experimentId = experiment }
                });
                using var cancelledOutput = new StringWriter();
                var exit = await new CommandDispatcher(application, evaluator).RunAsync(["persisted"],
                    new StringReader(requestJson), cancelledOutput, cancelRun.Token);
                var line = Assert.Single(cancelledOutput.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
                using var rejected = JsonDocument.Parse(line);
                Assert.Equal(2, exit);
                Assert.Equal("unconfirmed", rejected.RootElement.GetProperty("persistenceStatus").GetString());
                Assert.Equal("execution_cancelled", rejected.RootElement.GetProperty("errorCode").GetString());
                Assert.True(rejected.RootElement.GetProperty("result").GetProperty("stopRecorded").GetBoolean());
                Assert.True(rejected.RootElement.GetProperty("result").GetProperty("reconciliationRequired").GetBoolean());
                // A fresh application/token must still observe durable quarantine, not retry.
                var reopened = new EvaluationApplication(source, endpoints);
                var repeat = await reopened.InvokeAsync(evaluator, requestJson, token);
                Assert.False(repeat.Success);
                await using var persisted = source.CreateCommand("""
                    SELECT e.execution_state,e.cancel_requested,e.reserved_calls,e.reserved_cost,t.state,a.state,
                      a.outcome->>'status',a.outcome->>'errorCode',t.result_artifact_id
                    FROM evaluation.experiments e JOIN evaluation.trials t
                      ON t.workspace_id=e.workspace_id AND t.project_id=e.project_id AND t.experiment_id=e.id
                    JOIN evaluation.attempts a
                      ON a.workspace_id=t.workspace_id AND a.project_id=t.project_id AND a.trial_id=t.id
                    WHERE e.workspace_id=@workspace AND e.project_id=@project AND e.id=@experiment
                    """);
                persisted.Parameters.AddWithValue("workspace", scope.WorkspaceId);
                persisted.Parameters.AddWithValue("project", scope.ProjectId);
                persisted.Parameters.AddWithValue("experiment", experiment);
                await using var rows = await persisted.ExecuteReaderAsync(token);
                Assert.True(await rows.ReadAsync(token));
                Assert.Equal("indeterminate", rows.GetString(0));
                Assert.True(rows.GetBoolean(1));
                Assert.Equal(1L, rows.GetInt64(2));
                Assert.Equal(0.01m, rows.GetDecimal(3));
                Assert.Equal("indeterminate", rows.GetString(4));
                Assert.Equal("indeterminate", rows.GetString(5));
                Assert.Equal("indeterminate", rows.GetString(6));
                Assert.Equal("request_cancelled", rows.GetString(7));
                Assert.True(rows.IsDBNull(8));
                Assert.False(await rows.ReadAsync(token));
                return;
            }
            var run = await Call(evaluator, "experiment.run", new { experimentId = experiment });
            Assert.Equal("trial_completed", run.GetProperty("status").GetString());
            var result = run.GetProperty("trial").Deserialize<ExecutedCompletionTrial>(jsonOptions);
            Assert.NotNull(result);
            var request = await serving;
            Assert.Equal("controlled-local", request.GetProperty("model").GetString());
            var stored = await store.GetArtifactAsync(evaluator, result.Result, token);
            using var output = JsonDocument.Parse(stored.Document.Content);
            Assert.Equal("controlled output", output.RootElement.GetProperty("output").GetString());
            Assert.Equal(candidate.Revision.Sha256, output.RootElement.GetProperty("profileSha256").GetString());
            Assert.Equal(input.Revision.Sha256, output.RootElement.GetProperty("inputSha256").GetString());
            var caseArtifact = (await Call(evaluator, "artifact.import",
                new { kind = "case", isHoldout = false, content = Encoding.UTF8.GetBytes("synthetic acceptance input") }))
                .Deserialize<EvidenceArtifactRef>(jsonOptions)!;
            var caseId = (await Call(evaluator, "case.import", new EvidenceCaseDocument("synthetic", caseArtifact)))
                .GetProperty("caseId").GetGuid();
            var provenance = await Save(new(1, "completion-input-provenance", Json(new { inputSha256 = input.Revision.Sha256 })));
            var binding = new CaseInputBinding(Guid.NewGuid(), caseId, caseArtifact, input.Revision, provenance.Revision);
            await Call(evaluator, "case.bind_input", binding);
            await Call(evaluator, "assessment.add", new Assessment(caseId, result.TrialId, "correctness", metadata.Revision,
                "human", AssessmentOutcome.Pass, null, [result.Result], null));
            var comparisonPolicy = await Save(new TrialComparisonPolicy(1, metadata.Revision, [new("correctness", true)]).ToRevisionDocument());
            var comparison = new ExperimentComparisonRequest(experiment, comparisonPolicy.Revision, [new(input.Revision, binding.BindingId)]);
            var report = new ComparisonReportSaveRequest(Guid.NewGuid(), comparison);
            await Call(evaluator, "comparison.save", report);
            var shown = await Call(evaluator, "comparison.show", new { reportId = report.ReportId });
            Assert.Equal("available", shown.GetProperty("status").GetString());
            Assert.True(shown.GetProperty("snapshot").GetProperty("report").GetProperty("statistics")
                .GetProperty("candidates")[0].GetProperty("eligible").GetBoolean());
            await Call(evaluator, "decision.add", new InvestigationDecision(1, Guid.NewGuid(), experiment, "no_change",
                "Controlled fixture validates plumbing, not model quality.", "Run approved acceptance corpus.", [result.Result]));
            var export = await Call(evaluator, "bundle.export", new { reportId = report.ReportId });
            Assert.Equal("available", export.GetProperty("status").GetString());
            Assert.DoesNotContain("controlled output", export.GetRawText());
            var accounting = await Call(operatorAccess, "experiment.accounting", new { experimentId = experiment });
            Assert.Equal(JsonValueKind.Null, accounting.GetProperty("billing").GetProperty("declaredTotal").ValueKind);
            Assert.Equal(1, accounting.GetProperty("accounting").GetProperty("parentReservations").GetProperty("calls").GetInt64());
            var repeated = await application.InvokeAsync(evaluator,
                JsonSerializer.Serialize(new { contractVersion = 1, operation = "experiment.run", payload = new { experimentId = experiment } }), token);
            Assert.False(repeated.Success);
            Assert.Equal("operation_unconfirmed", repeated.ErrorCode);
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
