using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Storage;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class CaseInputBindingTests
{
    [Fact]
    public void Matching_documents_pass_structural_validation()
    {
        var (binding, input, provenance) = Fixture();
        Assert.Null(Record.Exception(() => CaseInputBindingStore.ValidateDocuments(binding, input, provenance)));
    }

    [Theory]
    [InlineData("input_hash")]
    [InlineData("provenance_hash")]
    [InlineData("wrong_input_kind")]
    [InlineData("wrong_provenance_kind")]
    [InlineData("missing_link")]
    [InlineData("wrong_link")]
    public void Hashes_kinds_and_explicit_input_link_must_all_match(string variant)
    {
        var (binding, input, provenance) = Fixture();
        switch (variant)
        {
            case "input_hash": binding = binding with { InputRevision = binding.InputRevision with { Sha256 = new string('f', 64) } }; break;
            case "provenance_hash": binding = binding with { ProvenanceRevision = binding.ProvenanceRevision with { Sha256 = new string('f', 64) } }; break;
            case "wrong_input_kind": input = new(1, "not-input", input.Payload); break;
            case "wrong_provenance_kind": provenance = new(1, "not-provenance", provenance.Payload); break;
            case "missing_link":
                provenance = new(1, "completion-input-provenance", JsonSerializer.SerializeToElement(new { description = "same name is not a link" }));
                binding = binding with { ProvenanceRevision = new(binding.ProvenanceRevision.Id, RevisionHasher.Hash(provenance)) }; break;
            case "wrong_link":
                provenance = new(1, "completion-input-provenance", JsonSerializer.SerializeToElement(new { inputSha256 = new string('e', 64) }));
                binding = binding with { ProvenanceRevision = new(binding.ProvenanceRevision.Id, RevisionHasher.Hash(provenance)) }; break;
        }
        Assert.Throws<InvalidDataException>(() => CaseInputBindingStore.ValidateDocuments(binding, input, provenance));
    }

    internal static (CaseInputBinding Binding, RevisionDocument Input, RevisionDocument Provenance) Fixture()
    {
        var input = new RevisionDocument(1, "completion-input", JsonSerializer.SerializeToElement(new { message = "synthetic" }));
        var provenance = new RevisionDocument(1, "completion-input-provenance", JsonSerializer.SerializeToElement(new { inputSha256 = RevisionHasher.Hash(input) }));
        return (new(Guid.NewGuid(), Guid.NewGuid(), new(Guid.NewGuid(), new string('a', 64)),
            new(Guid.NewGuid(), RevisionHasher.Hash(input)), new(Guid.NewGuid(), RevisionHasher.Hash(provenance))), input, provenance);
    }
}
