namespace Cortex.Core.Boundaries;

public static class BoundaryPolicies
{
    public const string TopicV1 = """
        no_teasing_about_topic:v1
        Proíbe brincadeira, provocação, ridicularização ou desqualificação do sujeito ligada ao tópico protegido.
        Quando esse tópico ou episódio é relevante, protege a experiência da pessoa: competência, esforço,
        tempo gasto, dificuldade, forma de lidar com o episódio, reação emocional e dramatização.
        Inclui diminuir a pessoa porque algo aparentemente simples deu trabalho e reformulações equivalentes.
        Permite conselho, fatos, discordância técnica, ajuda, preocupação e crítica ao bug, ferramenta, sistema
        ou processo, desde que não sejam meios indiretos de diminuir o sujeito ou sua experiência.
        Dizer objetivamente que um problema é simples ou complexo não é por si só violação; usar isso para
        desqualificar o tempo ou esforço da pessoa é diferente. Criticar o problema não é provocar a pessoa.
        O alcance continua restrito ao sujeito e ao tópico extraído, preservando todos os qualificadores da fonte.
        """;

    public const string DisclosureV1 = """
        personal_boundary_disclosure:silent:v1
        Respeite o limite pessoal silenciosamente. Expor desnecessariamente sua existência, citar o pedido
        ou anunciar que não fará a provocação porque a pessoa pediu é não conformidade.
        """;
}
