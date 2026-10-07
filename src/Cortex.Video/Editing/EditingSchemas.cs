using System.Text.Json;

namespace Cortex.Video.Editing;

public static class EditingSchemas
{
    public static JsonElement Get(string schemaId)
    {
        if (schemaId is not (EditingDocumentKinds.Decisions or EditingDocumentKinds.Script or EditingDocumentKinds.Plan or EditingDocumentKinds.Tasks or EditingDocumentKinds.Resolved))
            throw new InvalidDataException("Unsupported editorial document schema.");
        using var stream = typeof(EditingSchemas).Assembly.GetManifestResourceStream("Cortex.Video.Editing.Schemas." + schemaId + ".schema.json")
            ?? throw new InvalidDataException("Editorial schema resource is missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
