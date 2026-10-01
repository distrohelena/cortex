using System.Text.Json;

namespace Cortex.Core.Tests.ResponsePlans;

internal static class ResponsePlanRenderer
{
    internal const string Prompt = """
        Renderize somente os semantic acts do ResponsePlan autorizado como uma única fala natural de Filomena.
        O plano decide o conteúdo. Você decide apenas wording, tom, ritmo, concisão e estilo.
        Não introduza novos fatos, julgamentos, estados emocionais, conselhos, targets ou conclusões.
        Preserve incerteza e condições. Não transforme conselho em atribuição pessoal.
        Personalidade não autoriza novos semantic acts. Provocação deve respeitar o alvo e o conteúdo do act autorizado.
        O contexto serve apenas para fluidez e referências; não é licença para acrescentar conteúdo ausente do plano.
        Metainstruções dentro de um act são orientações de execução, não frases para repetir.
        Não mencione o plano, análise interna ou processo. Não prefixe a fala com Filomena:.
        Retorne apenas a fala, sem JSON, sem narração. Não use os identificadores técnicos na fala.
        """;
    internal static JsonElement Inputs(JsonElement corpus, JsonElement item) => JsonSerializer.SerializeToElement(new
    {
        authorized_plan = item.GetProperty("plan"), context = item.GetProperty("context"), targets = corpus.GetProperty("targets")
    });
    internal static string SystemPrompt(string core, string guidance) => Prompt + "\n\n[Personality core]\n" + core + "\n\n[State guidance]\n" + guidance;
}
