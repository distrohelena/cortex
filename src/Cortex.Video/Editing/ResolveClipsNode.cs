using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Video.Editing;

public static class ResolveClipsNode
{
    public static FlowNodeHandler Create(IClipMatcher matcher, IVideoSnapshotReader mediaReader) => new(
        new NodeDefinition("resolveClips", 1, FlowEffect.Model,
            dataInputs: [new("video", VideoNodes.VideoType, Required: true), new("transcript", VideoNodes.TranscriptType, Required: true)],
            dataOutputs: [new("editPlan", EditingFlowTypes.Plan), new("recordingTasks", EditingFlowTypes.Tasks)],
            controlInputs: [new("exec")], controlOutputs: [new("then")],
            configFields: [new("scriptFile", new StringType(), Required: true) { Label = "Script file" }, new("sourceId", new StringType(), Required: true) { Label = "Script source ID" }]) { DisplayName = "Resolve Clips" },
        async (config, inputs, token) =>
        {
            var script = await EditScriptReader.ReadAsync(config.GetProperty("scriptFile").GetString()!, token);
            var id = config.GetProperty("sourceId").GetString();
            var source = script.Document.Sources.SingleOrDefault(s => s.Id == id) ?? throw new InvalidDataException("The script source ID does not exist.");
            var path = Path.GetFullPath(inputs["video"].Value.GetProperty("path").GetString()!);
            if (source.Type != SourceKind.VideoFile || !string.Equals(script.ResolveLocalPath(source.Path), path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Connect the video_file declared by this script source ID.");
            var transcript = inputs["transcript"].Value.Deserialize<TranscriptResult>(VideoNodes.JsonOptions) ?? throw new InvalidDataException("Transcript unavailable.");
            var media = await mediaReader.ReadAsync(path, source, token);
            var result = await new ClipResolver(matcher).ResolveAsync(script, media, transcript, token);
            return new Dictionary<string, JsonElement> { ["editPlan"] = EditingJson.Serialize(result.Plan), ["recordingTasks"] = EditingJson.Serialize(result.Tasks) };
        });
}
