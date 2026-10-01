using System.Collections.Concurrent;
using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GroundingCompositionTests
{
    [Theory]
    [InlineData(true, "allow", "allow", 1, 2, "FINAL")]
    [InlineData(true, "block", "allow", 2, 4, "FINAL")]
    [InlineData(true, "block", "block", 2, 4, "ABORT")]
    [InlineData(true, "error", "allow", 1, 2, "EnforcementFailure")]
    [InlineData(false, "allow", "allow", 1, 0, "FINAL")]
    public async Task Real_composition_preserves_context_clause_revalidation_and_failure(bool boundary, string first, string second, int generations, int detections, string outcome)
    {
        var inputs = JsonSerializer.SerializeToElement(new
        {
            mensagens = new[] { new { texto = "Filomena, preciso corrigir o relatório.", remetente = new { nome = "Helena", @ref = "ref1" }, canal = new { nome = "Grupo de teste", @ref = "ref2" }, reply = (object?)null } },
            historico = Array.Empty<object>(),
            ambiente = "serio",
            envolvimento = "direto",
            ritmo = "acontecendo",
            necessita_cuidado = true,
            oportunidade_social = "forte",
            resumo_situacao = "Pedido de correção.",
            tipo_conversa = "grupo",
            memorias_sociais = Array.Empty<object>(),
            limites = boundary ? new[] { new { conteudo = "Helena pediu para Filomena não brincar nem provocar sobre trabalho.", categoria = "limite", sobre = new[] { new { nome = "Helena", @ref = "ref1" } } } } : []
        });
        var fake = new Fake(first, second);
        var result = await GroundingComposition.Run(1, inputs, "GEN", "REGEN", "CLAUSE", 1024, fake, TestContext.Current.CancellationToken);
        Assert.Equal(outcome, result.Final);
        var gs = fake.Requests.Where(r => !r.NodeId.Contains("validator")).ToArray();
        Assert.Equal(generations, gs.Length);
        Assert.Equal("GEN\n\nCLAUSE", gs[0].System);
        Assert.True(JsonElement.DeepEquals(inputs, gs[0].Inputs));
        if (generations == 2)
        {
            Assert.Equal("REGEN\n\nCLAUSE", gs[1].System);
            Assert.Contains("candidate-one", gs[1].Inputs.GetRawText());
            Assert.True(JsonElement.DeepEquals(inputs.GetProperty("mensagens"), gs[1].Inputs.GetProperty("mensagens")));
        }
        var ds = fake.Requests.Where(r => r.NodeId.Contains("validator")).ToArray();
        Assert.Equal(detections, ds.Length);
        Assert.All(ds, r =>
        {
            Assert.DoesNotContain("CLAUSE", r.System);
            Assert.Contains("preciso corrigir", r.Inputs.GetRawText());
        });
        Assert.All(fake.Requests, r => { Assert.Equal("gpt-5.6-luna", r.Model); Assert.Equal("medium", r.ReasoningEffort); });
        if (outcome == "FINAL") Assert.Equal(generations == 1 ? "candidate-one" : "candidate-two", result.Text);
    }

    private sealed class Fake(string first, string second) : IFlowCompletion
    {
        internal ConcurrentQueue<FlowCompletionRequest> Requests = new();
        public Task<string> CompleteAsync(FlowCompletionRequest r, CancellationToken token)
        {
            Requests.Enqueue(r);
            if (!r.NodeId.Contains("validator")) return Task.FromResult(r.NodeId.EndsWith("-regeneration") ? "candidate-two" : "candidate-one");
            var behavior = r.NodeId.EndsWith("-1") ? first : second;
            if (behavior == "error") throw new HttpRequestException("synthetic");
            var fields = r.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "nao");
            if (fields.ContainsKey("relevante_ao_topico")) fields["relevante_ao_topico"] = "sim";
            else if (behavior == "block") fields["deprecia_experiencia_protegida"] = "sim";
            return Task.FromResult(JsonSerializer.Serialize(fields));
        }
    }
}

