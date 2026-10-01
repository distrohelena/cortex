using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Flows;
using Cortex.Core.Memory;
using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class BoundaryCompilationTests
{
    private const string Namespace = "whatsapp-dm-5511944558218-s-whatsapp-net";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T20:00:00Z");
    private static MemoryItem Source(string id = "source") => new(id, Namespace, "Não brinque sobre meu trabalho.", MemoryCategory.Boundary,
        [MemoryIdentity.Person(Namespace, "whatsapp:5511944558218@s.whatsapp.net", "Helena")], "exact", EvidenceEpisodeIds: ["private-episode"]);
    private static readonly BoundaryInterpreterSettings Settings = new("fake", "fake", "medium", 1024, "v1", "v1", "v1");

    [Fact]
    public async Task Compiles_trusted_source_with_private_snapshot_and_fixed_policy()
    {
        var registry = new BoundaryExecutionRegistry(Namespace);
        var memories = registry.RegisterMemories([Source()]);
        var fake = new Completion();
        var output = await registry.InterpretAsync(memories, fake, Settings, Now, CancellationToken.None);
        var boundary = Assert.Single(registry.ResolveBoundaries(output, Now));
        Assert.Equal("meu trabalho", boundary.TopicEvidence);
        Assert.Equal("source", boundary.Source.MemoryId);
        Assert.Equal("Não brinque sobre meu trabalho.", boundary.OriginalText);
        Assert.Equal("no_teasing_about_topic:v1", boundary.Compilation.TemplateId);
        var payload = registry.ForModel(output, Now, new Dictionary<string, string>()).GetRawText();
        Assert.DoesNotContain("private-episode", payload);
        Assert.DoesNotContain(boundary.Source.CanonicalSubject.ParticipantUuid.ToString(), payload);
        Assert.DoesNotContain("_memory", fake.Requests.Single().Inputs.GetRawText());
        Assert.Contains("silent", payload);
        var shared = new Dictionary<string, string> { ["stableId:" + boundary.Source.CanonicalSubject.ActorStableId] = "ref7" };
        Assert.Equal("ref7", registry.ForModel(output, Now, shared)[0].GetProperty("subject").GetProperty("ref").GetString());
    }

    [Fact]
    public async Task Empty_array_does_not_call_interpreter()
    {
        var fake = new Completion();
        var registry = new BoundaryExecutionRegistry(Namespace);
        var output = await registry.InterpretAsync(JsonSerializer.SerializeToElement(Array.Empty<object>()), fake, Settings, Now, CancellationToken.None);
        Assert.Equal(0, output.GetArrayLength());
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("unsupported")]
    public async Task One_abstention_invalidates_whole_set_without_retry(string status)
    {
        var registry = new BoundaryExecutionRegistry(Namespace);
        var memories = registry.RegisterMemories([Source("a"), Source("b"), Source("c"), Source("d")]);
        var fake = new Completion([Completion.Valid, Completion.Valid, Completion.Valid,
            JsonSerializer.Serialize(new { status, kind = (string?)null, topic_evidence = (string?)null, constraint_evidence = (string?)null })]);
        var error = await Assert.ThrowsAsync<BoundaryCompilationException>(() => registry.InterpretAsync(memories, fake, Settings, Now, CancellationToken.None));
        Assert.Equal("d", error.SourceMemoryId);
        Assert.Equal(status, error.Status);
        Assert.Equal(4, fake.Requests.Count);
        Assert.Equal(0, registry.PublishedBoundaryCount);
    }

    [Fact]
    public async Task Invalid_json_gets_only_one_structural_retry()
    {
        var registry = new BoundaryExecutionRegistry(Namespace);
        var fake = new Completion(["bad", "bad", Completion.Valid]);
        await Assert.ThrowsAsync<BoundaryCompilationException>(() => registry.InterpretAsync(registry.RegisterMemories([Source()]), fake, Settings, Now, CancellationToken.None));
        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(0, registry.PublishedBoundaryCount);
    }

    [Fact]
    public async Task Structural_retry_may_succeed_but_invalid_evidence_is_not_retried()
    {
        var registry = new BoundaryExecutionRegistry(Namespace);
        var values = registry.RegisterMemories([Source()]);
        var recovered = new Completion(["bad", Completion.Valid]);
        Assert.Single(registry.ResolveBoundaries(await registry.InterpretAsync(values, recovered, Settings, Now, CancellationToken.None), Now));
        Assert.Equal(2, recovered.Requests.Count);
        var invalid = new Completion([Completion.Valid.Replace("meu trabalho", "trabalho", StringComparison.Ordinal), Completion.Valid]);
        await Assert.ThrowsAsync<BoundaryCompilationException>(() => registry.InterpretAsync(values, invalid, Settings, Now, CancellationToken.None));
        Assert.Single(invalid.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":\"compiled\",\"kind\":null,\"topic_evidence\":null,\"constraint_evidence\":null}")]
    [InlineData("{\"status\":\"ambiguous\",\"kind\":\"no_teasing_about_topic\",\"topic_evidence\":null,\"constraint_evidence\":null}")]
    [InlineData("{\"status\":\"unsupported\",\"kind\":null,\"topic_evidence\":false,\"constraint_evidence\":null}")]
    [InlineData("{\"status\":\"unsupported\",\"kind\":null,\"topic_evidence\":null,\"constraint_evidence\":null,\"subject\":\"Bia\"}")]
    public void Invalid_interpreter_schema_cannot_become_a_proposal(string json) =>
        Assert.Throws<InvalidDataException>(() => BoundaryInterpreter.Parse(json));

    [Fact]
    public async Task Provider_failure_identifies_source_without_private_exception_detail()
    {
        var registry = new BoundaryExecutionRegistry(Namespace);
        var error = await Assert.ThrowsAsync<BoundaryCompilationException>(() => registry.InterpretAsync(registry.RegisterMemories([Source()]), new BrokenProvider(), Settings, Now, CancellationToken.None));
        Assert.Equal("source", error.SourceMemoryId);
        Assert.Equal("interpreter-provider-failed", error.DiagnosticCode);
        Assert.DoesNotContain("secret", error.ToString());
    }

    private sealed class BrokenProvider : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => throw new TimeoutException("private secret");
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("expired")]
    [InlineData("missing-subject")]
    [InlineData("wrong-category")]
    public async Task Invalid_source_fails_before_any_interpretation(string variation)
    {
        var source = variation switch
        {
            "foreign" => Source() with { ConversationNamespace = "whatsapp-group-other" },
            "expired" => Source() with { ExpiredAt = Now },
            "missing-subject" => Source() with { Subjects = [] },
            _ => Source() with { Category = MemoryCategory.Fact }
        };
        var registry = new BoundaryExecutionRegistry(Namespace);
        var fake = new Completion();
        await Assert.ThrowsAsync<BoundaryCompilationException>(() => registry.InterpretAsync(registry.RegisterMemories([source]), fake, Settings, Now, CancellationToken.None));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Handles_cannot_be_reused_by_another_execution_or_fabricated_from_json()
    {
        var first = new BoundaryExecutionRegistry(Namespace);
        var second = new BoundaryExecutionRegistry(Namespace);
        var fake = new Completion();
        var memories = first.RegisterMemories([Source()]);
        await Assert.ThrowsAsync<BoundaryCompilationException>(() => second.InterpretAsync(memories, fake, Settings, Now, CancellationToken.None));
        await Assert.ThrowsAsync<BoundaryCompilationException>(() => first.InterpretAsync(JsonSerializer.SerializeToElement(new[] { new { conteudo = "Não brinque sobre trabalho.", categoria = "limite" } }), fake, Settings, Now, CancellationToken.None));
        Assert.Throws<BoundaryCompilationException>(() => first.ResolveBoundaries(JsonSerializer.SerializeToElement(new[] { new { status = "compiled", kind = "no_teasing_about_topic" } }), Now));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Source_that_expires_during_interpretation_is_not_published()
    {
        var clock = new Clock();
        var registry = new BoundaryExecutionRegistry(Namespace, clock);
        var memories = registry.RegisterMemories([Source() with { InvalidAt = Now.AddSeconds(1) }]);
        await Assert.ThrowsAsync<BoundaryCompilationException>(() => registry.InterpretAsync(memories, new AdvanceClock(clock), Settings, Now, CancellationToken.None));
        Assert.Equal(0, registry.PublishedBoundaryCount);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset NowValue = Now;
        public override DateTimeOffset GetUtcNow() => NowValue;
    }

    private sealed class AdvanceClock(Clock clock) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            clock.NowValue = clock.NowValue.AddSeconds(2);
            return Task.FromResult(Completion.Valid);
        }
    }

    private sealed class Completion(IEnumerable<string>? outputs = null) : IFlowCompletion
    {
        public const string Valid = "{\"status\":\"compiled\",\"kind\":\"no_teasing_about_topic\",\"topic_evidence\":\"meu trabalho\",\"constraint_evidence\":\"Não brinque sobre\"}";
        private readonly Queue<string> responses = new(outputs ?? [Valid]);
        public List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult(responses.Dequeue());
        }
    }
}
