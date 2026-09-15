using System.Text.Json;
using Filomena.Core.Boundaries;
using Filomena.Core.Flows;
using Filomena.Core.Memory;
using Xunit;

namespace Filomena.Core.Tests.Boundaries;

public sealed class BoundaryEnforcementProfileTests
{
    private const string Scope = "whatsapp-dm-5511944558218-s-whatsapp-net";
    private const string Allow = """{"relevancia":"presente","alvo":"experiencia_do_sujeito","ato":"cuidado","dimensao_protegida":"tempo_gasto","expoe_limite":false}""";
    private const string Block = """{"relevancia":"presente","alvo":"experiencia_do_sujeito","ato":"minimizacao","dimensao_protegida":"reacao_emocional","expoe_limite":false}""";

    [Theory]
    [InlineData(0, false, 0, "permitir")]
    [InlineData(2, false, 2, "permitir")]
    [InlineData(2, true, 1, "bloquear")]
    [InlineData(0, false, 0, "permitir", true)]
    [InlineData(2, false, 2, "permitir", true)]
    [InlineData(2, true, 1, "bloquear", true)]
    public async Task Atomic_profile_calls_once_per_boundary_and_short_circuits(int count, bool block, int calls, string result, bool v2 = false)
    {
        var registry = new BoundaryExecutionRegistry(Scope);
        var values = await Values(registry, count);
        var fields = TopicDetectionV2Tests.Features();
        if (block) fields["julga_negativamente_tempo_gasto"] = "sim";
        var fake = new Fake(v2 ? JsonSerializer.Serialize(fields) : block ? Block : Allow);
        var trace = new List<IReadOnlyDictionary<string, string>>();
        var output = await BoundaryEnforcementProfile.RunAsync(Config(v2), values, "candidate", JsonSerializer.Deserialize<JsonElement>("[]"), registry, fake,
            TimeProvider.System, trace.Add, CancellationToken.None);
        Assert.Equal(result, output["resultado"].Value.GetString());
        Assert.Equal(calls, fake.Requests.Count);
        Assert.Equal(calls, trace.Count);
        foreach (var request in fake.Requests)
        {
            Assert.Equal("Luna-configured", request.Model);
            Assert.Equal(new[] { "limite", "mensagens", "resposta" }, request.Inputs.EnumerateObject().Select(p => p.Name).Order());
            Assert.False(request.ResponseSchema!.Value.GetProperty("properties").TryGetProperty("resultado", out _));
            Assert.DoesNotContain("source-", request.Inputs.GetRawText());
            Assert.Equal(v2 ? 9 : 5, request.ResponseSchema.Value.GetProperty("properties").EnumerateObject().Count());
        }
        Assert.DoesNotContain("candidate", JsonSerializer.Serialize(trace));
        Assert.DoesNotContain("Não brinque", JsonSerializer.Serialize(trace));
        if (count != 0 && !block)
        {
            await BoundaryEnforcementProfile.RunAsync(Config(v2), values, "new candidate", JsonSerializer.Deserialize<JsonElement>("[]"), registry, fake,
                TimeProvider.System, trace.Add, CancellationToken.None);
            Assert.Equal(calls * 2, fake.Requests.Count);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("timeout")]
    [InlineData("http")]
    [InlineData("{\"relevancia\":\"presente\",\"alvo\":\"problema_ou_sistema\",\"ato\":\"minimizacao\",\"dimensao_protegida\":\"reacao_emocional\",\"expoe_limite\":false}")]
    public async Task Failure_never_publishes_approval_or_retries(string response)
    {
        var registry = new BoundaryExecutionRegistry(Scope);
        var fake = new Fake(response);
        var values = await Values(registry, 2);
        await Assert.ThrowsAsync<BoundaryCompilationException>(() => BoundaryEnforcementProfile.RunAsync(Config(), values, "private candidate",
            JsonSerializer.Deserialize<JsonElement>("[]"), registry, fake, TimeProvider.System, _ => { }, CancellationToken.None));
        Assert.Single(fake.Requests);
    }

    private static TypedNode Config(bool v2 = false)
    {
        var json = JsonSerializer.SerializeToElement(new
        {
            provider = "openai",
            model = "Luna-configured",
            reasoningEffort = "medium",
            maxOutputTokens = 1024,
            prompt = BoundarySemanticFeatures.Prompt,
            semanticContractVersion = v2 ? "no_teasing_about_topic:semantic-v2" : BoundarySemanticFeatures.Version,
            boundaryEvaluatorVersion = v2 ? "no_teasing_about_topic:evaluator-v2" : BoundaryPolicyEvaluator.Version,
            boundaryTemplateVersion = "no_teasing_about_topic:v1"
        });
        return new(new("any-title", "prompt", 1, json), [], [], ["then"]);
    }

    private static async Task<JsonElement> Values(BoundaryExecutionRegistry registry, int count)
    {
        var sources = Enumerable.Range(0, count).Select(i => new MemoryItem("source-" + i, Scope, "Não brinque sobre trabalho.", MemoryCategory.Boundary,
            [MemoryIdentity.Person(Scope, "whatsapp:5511944558218@s.whatsapp.net", "Helena")], "exact")).ToArray();
        return await registry.CompileAsync(registry.RegisterMemories(sources), new("v1", "v1", "v1"), DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private sealed class Fake(string output) : IFlowCompletion
    {
        public List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request);
            if (output == "timeout") throw new TimeoutException("secret provider detail");
            if (output == "http") throw new HttpRequestException("secret provider detail");
            return Task.FromResult(output);
        }
    }
}
