using System.Security.Cryptography;
using System.Text;
namespace Cortex.Personas.Tests;

internal static class PersonaFixtures
{
    internal static readonly PersonaScope Scope = new("project-a", "fictional-world", "character-a");
    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static PersonaSituation Situation(string act = "frustration", string? relationship = "close",
        string? audience = "private", string? state = "calm") => new(Scope, "situation-a",
            new(act, "message-a"), relationship is null ? null : new(relationship, "relation-a"),
            audience is null ? null : new(audience, "host-environment-a"),
            state is null ? null : new(state, "context-state-a"));
    internal static PersonaRevisionBundle Bundle()
    {
        const string sourceText = "Mira: você pode mudar o caminho sem desistir do objetivo. Depois eu te ajudo.";
        const string counterText = "Mira: hoje eu só ouvi, sem provocar nem dar conselho.";
        var source = new PersonaSource("source-a", "episode-a", Hash(sourceText), sourceText,
            PersonaSourceKind.FictionalFixture, "Synthetic test material");
        var counter = new PersonaSource("source-b", "episode-b", Hash(counterText), counterText,
            PersonaSourceKind.FictionalFixture, "Synthetic test material");
        var evidence = Evidence("evidence-a", source);
        var contrary = Evidence("evidence-b", counter) with { ObservedBehavior = "Ouviu sem oferecer conselho." };
        var pattern = new BehaviorPattern("intimate_provocation", new("frustration", "close", "private"),
            [new(DynamicState: "overwhelmed")], "Pode questionar a premissa antes de oferecer apoio.",
            ["evidence-a"], ["evidence-b"]);
        var spec = new PersonaSpec(Scope, "R1", "Mira, personagem fictícia desta fixture.",
            [new("Autonomia importa.", PersonaAssertionBasis.EvidenceBacked, ["evidence-a"])],
            [new("Voz direta, sem exigir provocação.", PersonaAssertionBasis.Authorial, [], "fixture-author")],
            [pattern.PatternId]);
        return new(1, spec, [source, counter], [evidence, contrary], [pattern]);
    }
    internal static PersonaEvidence Evidence(string id, PersonaSource source) => new(id, Scope, source.SourceId,
        "represented-mira", [new("represented-mira", "speaker"), new("represented-friend", "addressee")],
        new(0, source.Text.Length, TextOffsetUnit.Utf16), "Uma pessoa próxima estava frustrada.",
        "Questionou a forma de execução e ofereceu apoio depois.",
        new("Pode valorizar autonomia sem abandonar o apoio.", ["Pode ser uma reação excepcional."],
            new("manual-fixture", "v1", "fixture-author"), "controlled_fixture"), new(null, null, null));

    internal static PersonaRevisionBundle SmokeBundle()
    {
        var b = Bundle();
        var sources = b.Sources.ToList();
        var evidence = b.Evidence.ToList();
        var patterns = b.Patterns.ToList();
        Add("technical_help", "technical_question", "Mira: manda o erro e o trecho relevante; eu olho com você.",
            "Pediu o erro e o trecho antes de sugerir uma solução.");
        Add("admit_error", "correction", "Mira: eu errei nessa conta; vou corrigir.", "Admitiu o próprio erro.");
        Add("simple_support", "support_request", "Mira: pode falar, eu fico aqui com você.", "Ofereceu companhia quando pediram apoio.");
        return b with
        {
            Sources = sources,
            Evidence = evidence,
            Patterns = patterns,
            Spec = b.Spec with { SelectedPatternIds = patterns.Select(p => p.PatternId).ToArray() }
        };

        void Add(string id, string act, string text, string behavior)
        {
            var source = new PersonaSource("source-" + id, "episode-" + id, Hash(text), text,
                PersonaSourceKind.FictionalFixture, "Synthetic test material");
            var item = Evidence("evidence-" + id, source) with
            {
                ObservedContext = "Fixture de " + act,
                ObservedBehavior = behavior,
                Interpretation = new("Uma tendência possível, não uma regra universal.", [],
                    new("manual-fixture", "v1", "fixture-author"), "controlled_fixture")
            };
            sources.Add(source);
            evidence.Add(item);
            patterns.Add(new(id, new(CommunicativeAct: act), [], behavior, [item.EvidenceId], []));
        }
    }
}
