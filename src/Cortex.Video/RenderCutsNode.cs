using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Video;

public static class RenderCutsNode
{
    public static ObjectType RenderedVideoType { get; } = new(new Dictionary<string, FlowField>
    {
        ["path"] = new(new StringType(), true),
        ["duration"] = new(new NumberType(), true),
        ["timeline"] = new(TakeSuggestionNode.EditPlanType.Fields["proposedTimeline"].Type, true)
    });

    public static NodeDefinition Definition() => new("renderCuts", 1, FlowEffect.WriteState,
            dataInputs: [new("video", VideoNodes.VideoType, Required: true), new("editPlan", TakeSuggestionNode.EditPlanType)],
            dataOutputs: [new("video", RenderedVideoType)], controlInputs: [new("exec")], controlOutputs: [new("then")],
            configFields: [new("outputDirectory", new StringType(), Required: true) { Label = "Output directory" },
                new("editPlanFile", new StringType()) { Label = "Edit plan file (when not connected)" }]) { DisplayName = "Render Cuts" };

    public static FlowNodeHandler Create(IVideoCutRenderer renderer) => new(
        Definition(),
        async (config, inputs, token) =>
        {
            var file = config.TryGetProperty("editPlanFile", out var configured) ? configured.GetString() : null;
            JsonElement source;
            if (inputs.TryGetValue("editPlan", out var wired))
            {
                if (!string.IsNullOrWhiteSpace(file)) throw new InvalidDataException("Connect editPlan or select an edit plan file, not both.");
                source = wired.Value;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(file)) throw new InvalidDataException("Connect editPlan or select an edit plan file.");
                var path = Path.GetFullPath(file);
                if (new FileInfo(path).Length > 4194304) throw new InvalidDataException("The edit plan exceeds 4 MiB.");
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
                source = document.RootElement.Clone();
            }
            FlowDomain.Check(source, TakeSuggestionNode.EditPlanType);
            var plan = source.Deserialize<TakeEditPlan>(VideoNodes.JsonOptions) ?? throw new InvalidDataException("Edit plan unavailable.");
            var result = await renderer.RenderAsync(inputs["video"].Value.GetProperty("path").GetString()!, plan,
                config.GetProperty("outputDirectory").GetString()!, token);
            return new Dictionary<string, JsonElement> { ["video"] = JsonSerializer.SerializeToElement(result, VideoNodes.JsonOptions) };
        });
}
