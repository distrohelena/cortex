using System.Text.Json;
using Xunit;

namespace Filomena.Core.Tests.ResponsePlans;

public sealed class ResponsePlanRendererTests
{
    [Fact]
    public void Keeps_authorized_plan_separate_and_does_not_forward_review_or_constraints()
    {
        using var corpus = JsonDocument.Parse("""{"targets":[{"id":"person_0","description":"destinatária"}]}""");
        using var item = JsonDocument.Parse("""
            {"plan":{"acts":[]},"context":"contexto sintético","expected":"private-gold","constraints":["do-not-forward"],"sourceId":"private"}
            """);
        var inputs = ResponsePlanRenderer.Inputs(corpus.RootElement, item.RootElement);
        Assert.Equal(new[] { "authorized_plan", "context", "targets" }, inputs.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Array, inputs.GetProperty("authorized_plan").GetProperty("acts").ValueKind);
        Assert.DoesNotContain("private-gold", inputs.GetRawText());
        Assert.DoesNotContain("do-not-forward", inputs.GetRawText());
    }

    [Fact]
    public void Injects_only_explicit_voice_references_into_system()
    {
        var system = ResponsePlanRenderer.SystemPrompt("unique-core", "unique-guidance");
        Assert.Contains("unique-core", system);
        Assert.Contains("unique-guidance", system);
    }
}
