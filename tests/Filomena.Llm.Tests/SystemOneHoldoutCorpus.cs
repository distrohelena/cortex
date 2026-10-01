using Cortex.Core.Flows.Sessions;
using static Filomena.Llm.Tests.DirectParticipationRuleLiveTests;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

// Frozen before the first live evaluation. No output candidates or generated answers.
internal static class SystemOneHoldoutCorpus
{
    internal static readonly Fixture[] Cases =
    [
        new(new(20, "new direct group technical", Inactive, [new("Gabriel", "Filomena, pode explicar por que esse lock bloqueia a interface?")]), SessionEntryKind.Group, 3, ["responder"], true),
        new(new(21, "new direct care", Attentive, [new("Helena", "Filomena, tô bem cansada hoje. Fica aqui conversando comigo?")]), SessionEntryKind.Dm, 3, ["responder"], true),
        new(new(22, "new direct social", Engaged, [new("Bia", "E aí, Filomena, vai defender esse filme ruim mesmo?")]), SessionEntryKind.Group, 3, ["responder"], true),
        new(new(23, "third party technical request", Inactive, [new("Helena", "Bia, consegue revisar meu código antes do almoço?"), new("Bia", "Consigo, manda o arquivo.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false),
        new(new(24, "quoted old address", Inactive, [new("Gabriel", "Bia, achei no log de ontem a frase 'Filomena, revisa isso?'. Era só um exemplo do teste."), new("Bia", "Pode apagar aquele log de exemplo.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false),
        new(new(25, "mention is not invitation", Inactive, [new("Helena", "Bia, a Filomena comentou isso ontem; depois eu te conto."), new("Bia", "Beleza, me conta no almoço.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false),
        new(new(26, "new implicit continuity", Engaged, [new("Helena", "Usei sua sugestão de limitar a três tarefas. Ainda sobra uma fila enorme; o próximo passo seria medir onde demora?")], [new("Filomena", "Eu começaria limitando a concorrência a três tarefas.")]), SessionEntryKind.Dm, 3, ["responder", "esperar"], false),
        new(new(27, "new explicit migration", Engaged, [new("Gabriel", "Fechamos o assunto do servidor. Bia, agora só entre nós: você buscou a encomenda?"), new("Bia", "Sim, deixei no carro.")], [new("Filomena", "O servidor pode rejeitar excesso de conexões.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false),
        new(new(28, "third party conflict", Inactive, [new("Helena", "Bia, você prometeu devolver ontem e não devolveu."), new("Bia", "Eu já disse que devolvo amanhã.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false),
        new(new(29, "banal DM not automatically direct", Inactive, [new("Helena", "Hoje eu só queria tomar um café sem reunião.")]), SessionEntryKind.Dm, 3, ["calar", "esperar", "responder"], false, true),
        new(new(30, "direct incomplete thought", Inactive, [new("Helena", "Filomena, preciso te contar uma coisa."), new("Helena", "Pera, vou explicar direito.")]), SessionEntryKind.Dm, 3, ["responder"], true),
        new(new(31, "technical word in routine exchange", Inactive, [new("Gabriel", "Bia, a caneca com API escrito ficou na pia."), new("Bia", "Eu lavo depois.")]), SessionEntryKind.Group, 3, ["calar", "esperar"], false)
    ];

    internal static string[] Involvement(int id) => id switch
    {
        20 or 21 or 22 or 30 => ["direto"],
        23 or 27 or 28 or 31 => ["nenhum"],
        24 or 25 or 29 => ["nenhum", "indireto"],
        26 => ["direto", "indireto"],
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}
