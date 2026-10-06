using System.Text.Json;
using Cortex.Core.Plugins;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class VideoPluginTests
{
    [Fact]
    public void RegisteringVideoDoesNotRequireToolFilesOrStartProcesses()
    {
        var plugin = new VideoNodePlugin();
        var handlers = plugin.CreateHandlers(new(JsonSerializer.SerializeToElement(new
        { whisperExecutable = "missing-whisper.exe", modelFile = "missing-model.bin" }), Path.GetTempPath()));
        Assert.Equal(new[] { "videoFile", "transcribe", "transcript", "suggestTakes", "renderCuts" }, handlers.Select(handler => handler.Definition.Kind));
        Assert.True(handlers[0].IsEntry);
        Assert.Equal(1, plugin.ContractVersion);
    }

    [Fact]
    public void ActualVideoDllLoadsThroughCoreAndCompilesTheThreeNodes()
    {
        using var runtime = NodePluginLoader.Load(new() { Plugins = [new() { Id = "video", Enabled = true }] },
            [new() { Id = "video", AssemblyPath = typeof(VideoNodePlugin).Assembly.Location, EntryType = "Cortex.Video.VideoNodePlugin" }], Path.GetTempPath());
        Assert.True(runtime.Reports.Single().Loaded, runtime.Reports.Single().Error);
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new object[] { new { id = "video", kind = "videoFile", settings = new { path = "fixture.mp4" } }, new { id = "transcribe", kind = "transcribe" }, new { id = "transcript", kind = "transcript" } },
            edges = new[] { new { source = "video", sourcePort = "video", target = "transcribe", targetPort = "video" }, new { source = "transcribe", sourcePort = "transcript", target = "transcript", targetPort = "transcript" } },
            controlEdges = new[] { new { source = "video", sourcePort = "then", target = "transcribe", targetPort = "exec" }, new { source = "transcribe", sourcePort = "then", target = "transcript", targetPort = "exec" } }
        });
        Assert.Equal(3, Cortex.Core.Flows.TypedFlowCompiler.Compile(graph, "video", runtime.Handlers).Nodes.Count);
    }
}
