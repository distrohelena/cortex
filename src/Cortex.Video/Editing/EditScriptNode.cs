using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Video.Editing;

public static class EditScriptNode
{
    public static NodeDefinition Definition() => new("editScript", 1, FlowEffect.Pure,
            dataOutputs: [new("script", EditingFlowTypes.Script), new("scriptJson", new StringType()), new("scriptRef", EditingFlowTypes.ScriptReference)],
            controlOutputs: [new("then")], configFields: [new("path", new StringType(), Required: true) { Label = "Script file" }]) { DisplayName = "Edit Script", ExecutionMode = FlowNodeExecutionMode.Entry };

    public static FlowNodeHandler Create() => new(
        Definition(),
        async (config, _, token) =>
        {
            var source = await EditScriptReader.ReadAsync(config.GetProperty("path").GetString()!, token);
            return new Dictionary<string, JsonElement>
            {
                ["script"] = EditingFlowTypes.ExecutionProjection(source.Document),
                ["scriptJson"] = JsonSerializer.SerializeToElement(source.OriginalJson),
                ["scriptRef"] = JsonSerializer.SerializeToElement(source.Reference, EditingJson.Options)
            };
        });
}
