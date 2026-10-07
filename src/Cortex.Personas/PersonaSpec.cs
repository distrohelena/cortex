namespace Cortex.Personas;

public enum PersonaAssertionBasis { Authorial, EvidenceBacked }
public sealed record PersonaAssertion(string Text, PersonaAssertionBasis Basis,
    IReadOnlyList<string> EvidenceIds, string? AuthorRef = null);
public sealed record PersonaSpec(PersonaScope Scope, string RevisionId, string IdentityDescription,
    IReadOnlyList<PersonaAssertion> ValuesAndBeliefs, IReadOnlyList<PersonaAssertion> VoiceGuidance,
    IReadOnlyList<string> SelectedPatternIds, string? ParentArtifactSha256 = null);
public sealed record PersonaRevisionBundle(int ContractVersion, PersonaSpec Spec,
    IReadOnlyList<PersonaSource> Sources, IReadOnlyList<PersonaEvidence> Evidence,
    IReadOnlyList<BehaviorPattern> Patterns);
/// <summary>Structural integrity only; source access requires separate host authorization.</summary>
public sealed class ValidatedPersonaRevision
{
    private readonly byte[] bytes;
    internal ValidatedPersonaRevision(byte[] bytes, string hash)
    {
        this.bytes = bytes.ToArray();
        ArtifactSha256 = hash;
    }
    // Each caller receives an independent data snapshot, never the stored mutable collections.
    public PersonaRevisionBundle Bundle => PersonaRevisionCodec.Read(bytes);
    public string ArtifactSha256 { get; }
    public ReadOnlyMemory<byte> Bytes => bytes.ToArray();
}
