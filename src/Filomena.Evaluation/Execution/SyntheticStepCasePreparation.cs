using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Evaluation.Contracts;

namespace Filomena.Evaluation.Execution;

public sealed record PreparedSyntheticStepCase(RevisionDocument Input, RevisionDocument Provenance);

public static class SyntheticStepCasePreparation
{
    public static PreparedSyntheticStepCase Prepare(string caseId, JsonElement context, string selfActorRef,
        FlowInstructionSnapshot instruction, JsonElement supplementalFixtures)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (string.IsNullOrWhiteSpace(instruction.ProjectRevision) || string.IsNullOrWhiteSpace(instruction.StateId) ||
            string.IsNullOrWhiteSpace(instruction.NodeId) || instruction.InputNames is null ||
            instruction.InputNames.Count == 0 || instruction.InputNames.Any(string.IsNullOrWhiteSpace) ||
            instruction.InputNames.Distinct(StringComparer.Ordinal).Count() != instruction.InputNames.Count ||
            supplementalFixtures.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid synthetic step contract.");
        var conversation = ConversationCaseInput.Build(caseId, context, selfActorRef);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in conversation.EnumerateObject())
            if (instruction.InputNames.Contains(field.Name, StringComparer.Ordinal)) values.Add(field.Name, field.Value);
        var supplements = new List<string>();
        foreach (var field in supplementalFixtures.EnumerateObject())
        {
            if (!instruction.InputNames.Contains(field.Name, StringComparer.Ordinal) ||
                conversation.TryGetProperty(field.Name, out _) || !values.TryAdd(field.Name, field.Value))
                throw new InvalidDataException("Supplemental fixture cannot override context or add undeclared/duplicate fields.");
            supplements.Add(field.Name);
        }
        if (values.Count != instruction.InputNames.Count)
            throw new InvalidDataException("Missing step inputs require explicit fixtures; upstream outputs cannot be inferred.");
        var input = new RevisionDocument(1, "completion-input", JsonSerializer.SerializeToElement(values));
        var provenance = new RevisionDocument(1, "completion-input-provenance", JsonSerializer.SerializeToElement(new
        {
            caseId,
            projectRevision = instruction.ProjectRevision,
            stateId = instruction.StateId,
            nodeId = instruction.NodeId,
            sourceKind = "synthetic_fixture",
            runtimeReplay = false,
            inputSha256 = RevisionHasher.Hash(input),
            supplementalFixtureFields = supplements.Order(StringComparer.Ordinal).ToArray()
        }));
        return new(input, provenance);
    }
}
