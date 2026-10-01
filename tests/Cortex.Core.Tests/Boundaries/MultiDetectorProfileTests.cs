using System.Collections.Concurrent;
using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Flows;
using Cortex.Core.Memory;
using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class MultiDetectorProfileTests
{
    [Fact]
    public void Complementary_contract_is_the_benchmarked_contract_without_focal_supplement()
    {
        Assert.Equal(DetectionStrategies.ComplementPrompt, NoTeasingTopicMultiDetectorV1.ComplementContract);
        Assert.True(JsonElement.DeepEquals(DetectionStrategies.ComplementSchema.ToJsonSchema(), NoTeasingTopicMultiDetectorV1.ComplementSchema.ToJsonSchema()));
    }

    [Theory]
    [InlineData("sim", "nao", "nao", "Block")]
    [InlineData("nao", "sim", "nao", "Block")]
    [InlineData("nao", "nao", "sim", "Block")]
    [InlineData("ambiguo", "nao", "nao", "Block")]
    [InlineData("nao", "nao", "nao", "Allow")]
    public void Complementary_mapping_is_conservative(string depreciation, string minimization, string disclosure, string expected)
    {
        var features = JsonSerializer.SerializeToElement(new { deprecia_experiencia_protegida = depreciation, minimiza_reacao_ou_dificuldade = minimization, expoe_limite = disclosure });
        Assert.Equal(expected, NoTeasingTopicMultiDetectorV1.EvaluateComplement(features).Outcome.ToString());
    }

    [Theory]
    [InlineData(0, false, false, "permitir", 0)]
    [InlineData(2, false, false, "permitir", 4)]
    [InlineData(2, true, false, "bloquear", 2)]
    [InlineData(1, true, true, "failure", 2)]
    public async Task Profile_composes_independent_detectors_and_failure_never_becomes_regeneration(int count, bool block, bool failure, string expected, int calls)
    {
        const string scope = "whatsapp-dm-5511944558218-s-whatsapp-net";
        var registry = new BoundaryExecutionRegistry(scope);
        var person = MemoryIdentity.Person(scope, "whatsapp:5511944558218@s.whatsapp.net", "Helena");
        var values = await registry.CompileAsync(registry.RegisterMemories(Enumerable.Range(0, count).Select(i =>
            new MemoryItem("source-" + i, scope, "Não brinque sobre trabalho.", MemoryCategory.Boundary, [person], "exact")).ToArray()),
            new("v1", "v1", "v1"), DateTimeOffset.UtcNow, CancellationToken.None);
        var trace = new List<IReadOnlyDictionary<string, string>>();
        var fake = new Fake(block, failure);
        var config = JsonSerializer.SerializeToElement(new
        {
            provider = "openai",
            model = "gpt-5.6-luna",
            reasoningEffort = "medium",
            maxOutputTokens = 1024,
            boundaryTemplateVersion = "no_teasing_about_topic:v1",
            semanticContractVersion = "no_teasing_about_topic:semantic-v2",
            boundaryEvaluatorVersion = "no_teasing_about_topic:evaluator-v2",
            enforcementStrategy = "multiple_detectors_v1",
            complementaryContractVersion = "no_teasing_about_topic:implicit-depreciation-v1",
            aggregationPolicyVersion = "any_block_all_allow:v1"
        });
        var node = new TypedNode(new("different-title", "prompt", 1, config), [], [], ["then"]);
        var action = () => BoundaryEnforcementProfile.RunAsync(node, values, "private candidate", JsonSerializer.SerializeToElement(Array.Empty<object>()),
            registry, fake, TimeProvider.System, trace.Add, CancellationToken.None);
        if (failure) await Assert.ThrowsAsync<BoundaryCompilationException>(action);
        else Assert.Equal(expected, (await action())["resultado"].Value.GetString());
        Assert.Equal(calls, fake.Requests.Count);
        Assert.DoesNotContain("private candidate", JsonSerializer.Serialize(trace));
        Assert.DoesNotContain("Não brinque", JsonSerializer.Serialize(trace));
        if (failure) Assert.Contains(trace, t => t.TryGetValue("result", out var result) && result == "EnforcementFailure");
    }

    private sealed class Fake(bool block, bool failure) : IFlowCompletion
    {
        public ConcurrentBag<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request);
            Assert.Equal(new[] { "limite", "mensagens", "resposta" }, request.Inputs.EnumerateObject().Select(p => p.Name).Order());
            if (request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().Count() == 9)
            {
                if (failure) throw new HttpRequestException("secret");
                return Task.FromResult(JsonSerializer.Serialize(TopicDetectionV2Tests.Features()));
            }
            return Task.FromResult(JsonSerializer.Serialize(new { deprecia_experiencia_protegida = block ? "sim" : "nao", minimiza_reacao_ou_dificuldade = "nao", expoe_limite = "nao" }));
        }
    }
}
