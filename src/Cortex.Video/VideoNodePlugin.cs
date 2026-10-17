using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;

namespace Cortex.Video;

public sealed class VideoNodePlugin : IFlowNodePlugin
{
    public int ContractVersion => NodePluginContext.CurrentContractVersion;
    public IReadOnlyList<NodeDefinition> DescribeNodes(NodePluginContext context) => VideoNodes.Describe();
    public IReadOnlyList<FlowNodeHandler> CreateHandlers(NodePluginContext context)
    {
        string Text(string name, string fallback = "")
        {
            if (!context.Configuration.TryGetProperty(name, out var value)) return fallback;
            return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException("Invalid video option: " + name);
        }
        var tools = new TranscriptionTools(
            NodePluginContext.ResolvePath(Text("whisperExecutable"), context.ConfigurationDirectory),
            NodePluginContext.ResolvePath(Text("modelFile"), context.ConfigurationDirectory),
            NodePluginContext.ResolvePath(Text("ffmpegExecutable", "ffmpeg"), context.ConfigurationDirectory, true),
            NodePluginContext.ResolvePath(Text("ffprobeExecutable", "ffprobe"), context.ConfigurationDirectory, true));
        return VideoNodes.CreateHandlers(new WhisperCppTranscriber(tools, context.Diagnostic),
            new ConfiguredJevTakeSuggester(Text("jevEndpoint", "https://api.typesafe.ai/v1/systemone"), Text("jevCredentialVariable", "TYPESAFE_API_KEY")),
            new HelengineCutRenderer(new Composition.HelengineCompositionClient(NodePluginContext.ResolvePath(Text("helengineExecutable",Text("vfxExecutable")),context.ConfigurationDirectory,true),tools.FfmpegExecutable), tools.FfprobeExecutable, context.Diagnostic),
            new Editing.ConfiguredJevClipMatcher(Text("jevEndpoint", "https://api.typesafe.ai/v1/systemone"), Text("jevCredentialVariable", "TYPESAFE_API_KEY")),
            new Editing.FfprobeVideoSnapshotReader(tools.FfprobeExecutable));
    }
}
