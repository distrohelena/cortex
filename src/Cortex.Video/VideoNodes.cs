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
    public static ObjectType TranscriptType { get; } = new(new Dictionary<string, FlowField>
    {
        ["text"] = new(new StringType(), true),
        ["segments"] = new(new ArrayType(new ObjectType(new Dictionary<string, FlowField>
        {
            ["start"] = new(new NumberType(), true),
            ["end"] = new(new NumberType(), true),
            ["text"] = new(new StringType(), true)
        })), true)
    });

    public static FlowNodeHandler[] CreateHandlers(IVideoTranscriber transcriber) =>
    [
        new(new NodeDefinition("videoFile", 1, FlowEffect.Pure,
            dataOutputs: [new("video", VideoType)], controlOutputs: [new("then")],
            configFields: [new("path", new StringType(), Required: true) { Label = "Caminho do vídeo" }]),
            (config, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(config.GetProperty("path").GetString()!);
                if (!File.Exists(path)) throw new InvalidDataException("Vídeo não encontrado: " + path);
                return Task.FromResult(Output("video", JsonSerializer.SerializeToElement(new { path })));
            }, isEntry: true),
        new(new NodeDefinition("transcribe", 1, FlowEffect.Model,
            dataInputs: [new("video", VideoType, Required: true)], dataOutputs: [new("transcript", TranscriptType)],
            controlInputs: [new("exec")], controlOutputs: [new("then")]),
            async (_, inputs, token) =>
            {
                var result = await transcriber.TranscribeAsync(inputs["video"].Value.GetProperty("path").GetString()!, token);
                return Output("transcript", JsonSerializer.SerializeToElement(result, JsonOptions));
            }),
        new(new NodeDefinition("transcript", 1, FlowEffect.Pure,
            dataInputs: [new("transcript", TranscriptType, Required: true)], dataOutputs: [new("transcript", TranscriptType)],
            controlInputs: [new("exec")]),
            (_, inputs, _) => Task.FromResult(Output("transcript", inputs["transcript"].Value)))
    ];

    private static IReadOnlyDictionary<string, JsonElement> Output(string port, JsonElement value) =>
        new Dictionary<string, JsonElement> { [port] = value };
}
