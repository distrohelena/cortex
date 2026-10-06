using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Video.Editing;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class EditScriptNodeTests
{
    [Fact]
    public async Task ScriptEntryPublishesSemanticIntentOriginalJsonAndOriginWithoutTools()
    {
        var path = Path.Combine(EditingSerializationTests.Repository(), "docs/examples/cortex-edit-v1/script.json");
        var graph = JsonSerializer.SerializeToElement(new { version = 3, nodes = new[] { new { id = "script", kind = "editScript", settings = new { path } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(graph, "script", [EditScriptNode.Create()]), TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("; ", result.Steps.Select(step => step.Error)));
        var outputs = result.Outputs["script"];
        Assert.Equal(7, outputs["script"].Value.GetProperty("beats").GetArrayLength());
        Assert.False(outputs["script"].Value.TryGetProperty("metadata", out _));
        Assert.True(JsonDocument.Parse(outputs["scriptJson"].Value.GetString()!).RootElement.GetProperty("metadata").GetProperty("example").GetBoolean());
        Assert.Equal(Path.GetFullPath(path), outputs["scriptRef"].Value.GetProperty("path").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), outputs["scriptRef"].Value.GetProperty("sha256").GetString());
        FlowDomain.Check(outputs["script"].Value, EditingFlowTypes.Script);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnicodePathBomAndAnnotationsKeepOriginalBytesAndStableBase(bool bom)
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex script ç " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var value = EditingContractValidationTests.Node("script.json"); value["metadata"]!["custom"] = JsonNode.Parse("[1,null,\"anotação\"]");
            var json = value.ToJsonString(); var utf8 = new UTF8Encoding(bom, true);
            var bytes = utf8.GetPreamble().Concat(utf8.GetBytes(json)).ToArray(); var path = Path.Combine(root, "roteiro.json"); File.WriteAllBytes(path, bytes);
            var loaded = await EditScriptReader.ReadAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), loaded.Reference.Sha256);
            Assert.Equal(Path.Combine(root, "raw"), loaded.ResolveLocalPath("raw"));
            Assert.Equal("anotação", JsonDocument.Parse(loaded.OriginalJson).RootElement.GetProperty("metadata").GetProperty("custom")[2].GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("invalid_utf8")]
    [InlineData("too_large")]
    [InlineData("generated_plan")]
    [InlineData("malformed")]
    public async Task InvalidFilesCannotBecomePublishedIntent(string mode)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, mode switch
            {
                "invalid_utf8" => new byte[] { 0xff, 0xfe },
                "too_large" => new byte[4194305],
                "generated_plan" => Encoding.UTF8.GetBytes(EditingSerializationTests.Fixture("edit_plan.json").GetRawText()),
                _ => Encoding.UTF8.GetBytes("{broken")
            });
            await Assert.ThrowsAsync<InvalidDataException>(() => EditScriptReader.ReadAsync(path, TestContext.Current.CancellationToken));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExactAndIntentModesLoadWithoutSearchingDeclaredMedia()
    {
        var path = Path.GetTempFileName();
        try
        {
            var value = EditingContractValidationTests.Node("script.json");
            value["beats"]![1]!["audio"]!["search"] = JsonNode.Parse("{\"speaker\":\"helena\",\"match\":\"exact\",\"text\":\"Essa frase.\"}");
            value["beats"]![2]!["audio"]!["search"]!["match"] = "intent";
            File.WriteAllText(path, value.ToJsonString());
            var loaded = await EditScriptReader.ReadAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(MatchMode.Exact, loaded.Document.Beats[1].Audio.Search!.Match);
            Assert.Equal(MatchMode.Intent, loaded.Document.Beats[2].Audio.Search!.Match);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CancelledReadPublishesNothing()
        => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EditScriptReader.ReadAsync("unused", new(true)));
}
