using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Video;

public static class VideoNodes
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static ObjectType VideoType { get; } = new(new Dictionary<string, FlowField>
    {
        ["path"] = new(new StringType(), true)
    });
    public static ObjectType TranscriptWordType { get; } = new(new Dictionary<string, FlowField>
    {
        ["start"] = new(new NumberType(), true),
        ["end"] = new(new NumberType(), true),
        ["text"] = new(new StringType(), true)
    });
    public static ObjectType TranscriptType { get; } = new(new Dictionary<string, FlowField>
    {
        ["text"] = new(new StringType(), true),
        ["segments"] = new(new ArrayType(new ObjectType(new Dictionary<string, FlowField>
        {
            ["start"] = new(new NumberType(), true),
            ["end"] = new(new NumberType(), true),
            ["text"] = new(new StringType(), true)
        })), true),
        ["words"] = new(new ArrayType(TranscriptWordType), false),
        ["wordTimingMethod"] = new(new NullableType(new EnumType([WhisperWordTimestamps.Method])), false)
    });

    public static IReadOnlyList<NodeDefinition> Describe() => [
        VideoFileDefinition(), TranscribeDefinition(), TranscriptDefinition(),
        TakeSuggestionNode.Definition(), RenderCutsNode.Definition(),
        Editing.EditScriptNode.Definition(), Editing.ResolveClipsNode.Definition()
    ];

    private static NodeDefinition VideoFileDefinition() => new("videoFile", 1, FlowEffect.Pure,
        dataOutputs: [new("video", VideoType)], controlOutputs: [new("then")],
        configFields: [new("path", new StringType(), Required: true) { Label = "Video path" }])
    { DisplayName = "Video File", ExecutionMode = FlowNodeExecutionMode.Entry };

    private static NodeDefinition TranscribeDefinition() => new("transcribe", 1, FlowEffect.Model,
        dataInputs: [new("video", VideoType, Required: true)], dataOutputs: [new("transcript", TranscriptType)],
        controlInputs: [new("exec")], controlOutputs: [new("then")]) { DisplayName = "Transcribe" };

    private static NodeDefinition TranscriptDefinition() => new("transcript", 1, FlowEffect.Pure,
        dataInputs: [new("transcript", TranscriptType, Required: true)], dataOutputs: [new("transcript", TranscriptType)],
        controlInputs: [new("exec")], controlOutputs: [new("then")]) { DisplayName = "Transcript" };

    public static FlowNodeHandler[] CreateHandlers(IVideoTranscriber transcriber, ITakeSuggester? takes = null, IVideoCutRenderer? renderer = null, Editing.IClipMatcher? clipMatcher = null, Editing.IVideoSnapshotReader? mediaReader = null)
    {
        var handlers = new List<FlowNodeHandler>
        {
        new(VideoFileDefinition(),
            (config, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(config.GetProperty("path").GetString()!);
                if (!File.Exists(path)) throw new InvalidDataException("Video not found: " + path);
                return Task.FromResult(Output("video", JsonSerializer.SerializeToElement(new { path })));
            }, isEntry: true),
        new(TranscribeDefinition(),
            async (_, inputs, token) =>
            {
                var result = await transcriber.TranscribeAsync(inputs["video"].Value.GetProperty("path").GetString()!, token);
                return Output("transcript", JsonSerializer.SerializeToElement(result, JsonOptions));
            }),
        new(TranscriptDefinition(),
            (_, inputs, _) => Task.FromResult(Output("transcript", inputs["transcript"].Value)))
        };
        if (takes is not null) handlers.Add(TakeSuggestionNode.Create(takes));
        if (renderer is not null) handlers.Add(RenderCutsNode.Create(renderer));
        handlers.Add(Editing.EditScriptNode.Create());
        if (clipMatcher is not null && mediaReader is not null) handlers.Add(Editing.ResolveClipsNode.Create(clipMatcher, mediaReader));
        return handlers.ToArray();
    }

    private static IReadOnlyDictionary<string, JsonElement> Output(string port, JsonElement value) =>
        new Dictionary<string, JsonElement> { [port] = value };
}
