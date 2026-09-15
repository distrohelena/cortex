namespace Filomena.Core.Boundaries;

/// <summary>Closed grammar extraction; offsets always refer to the unchanged source.</summary>
public static class BoundarySourceParserV1
{
    public const string Version = "v1";

    public static IReadOnlyList<string> SupportedForms { get; } = Array.AsReadOnly(new[]
    {
        "Não brinque sobre <tópico>.",
        "Não faça piada sobre <tópico>.",
        "Não provoque sobre <tópico>.",
        "Helena pediu para Filomena não <brincar nem provocar | brincar | fazer piada | provocar> sobre <tópico>."
    });

    public const string SupportedTopics = "Prefixo opcional: meu|minha|seu|sua|o|a. Núcleo: trabalho|emprego|carreira|salário|família|aparência|saúde|projeto|vida pessoal|vida profissional|episódio profissional. Qualificador opcional: de ontem|de hoje|atual|recente. Outros tópicos e cláusulas não compilam nesta V1.";

    public static BoundaryAdmissionResult Parse(string source) => BoundaryAdmission.Analyze(source);

    internal static BoundaryProposal Proposal(string source)
    {
        var parsed = Parse(source);
        return parsed.Status == "compiled"
            ? new("compiled", BoundaryAdmission.Kind, parsed.Topic!.Value.Extract(source), parsed.Constraint!.Value.Extract(source))
            : new(parsed.Status, null, null, null);
    }
}
