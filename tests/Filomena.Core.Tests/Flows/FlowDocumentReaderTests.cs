using System.Text;
using System.Text.Json;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class FlowDocumentReaderTests
{
    [Fact]
    public void ReadsGraphDocumentWithSeparateEdgeCollectionsAndOwnedConfig()
    {
        const string json = """
            {
              "id": "flow-1",
              "formatVersion": 1,
              "role": "character",
              "characterId": "filomena",
              "entryNodeIds": ["input-dm", "input-group"],
              "nodes": [
                {"id":"input-dm","kind":"input","blockVersion":1,"config":{"prompt":"hello"}},
                {"id":"reply","kind":"llm","blockVersion":2,"config":{}}
              ],
              "dataEdges": [
                {"source":{"nodeId":"input-dm","port":"text"},"fieldPath":["message","text"],"target":{"nodeId":"reply","port":"input"}}
              ],
              "controlEdges": [
                {"source":{"nodeId":"input-dm","port":"next"},"target":{"nodeId":"reply","port":"run"}}
              ]
            }
            """;

        var result = FlowDocumentReader.Read(json);

        Assert.Empty(result.Issues);
        Assert.NotNull(result.Document);
        var document = result.Document!;
        Assert.Equal("flow-1", document.Id);
        Assert.Equal(1, document.FormatVersion);
        Assert.Equal(GraphRole.Character, document.Role);
        Assert.Equal("filomena", document.CharacterId);
        Assert.Equal(["input-dm", "input-group"], document.EntryNodeIds);
        Assert.Equal(2, document.Nodes.Count);
        Assert.Equal("llm", document.Nodes[1].Kind);
        Assert.Equal(2, document.Nodes[1].BlockVersion);
        Assert.Single(document.DataEdges);
        Assert.Equal(["message", "text"], document.DataEdges[0].FieldPath);
        Assert.Single(document.ControlEdges);
        Assert.Equal("run", document.ControlEdges[0].Target.Port);
        Assert.Equal(JsonValueKind.Object, document.Nodes[0].Config.ValueKind);
        Assert.Equal("hello", document.Nodes[0].Config.GetProperty("prompt").GetString());
    }

    [Fact]
    public void RejectsMalformedJsonWithoutReturningDocument()
    {
        var result = FlowDocumentReader.Read("{\"id\":");

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "document.json_malformed");
    }

    [Fact]
    public void RejectsDuplicateJsonPropertiesBeforeDeserialization()
    {
        var result = FlowDocumentReader.Read("""
            {"id":"flow-1","id":"flow-2","formatVersion":1,"role":"ingress","nodes":[],"dataEdges":[],"controlEdges":[]}
            """);

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "document.property_duplicate");
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains("flow-2", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnknownFormatVersion()
    {
        var result = FlowDocumentReader.Read("{\"formatVersion\":99}");

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "document.version_unsupported");
    }

    [Fact]
    public void RejectsUnknownTopLevelAndNodeFields()
    {
        var result = FlowDocumentReader.Read("""
            {
              "id":"flow-1",
              "formatVersion":1,
              "role":"ingress",
              "future":true,
              "nodes":[{"id":"n1","kind":"input","blockVersion":1,"config":{},"future":true}],
              "dataEdges":[],
              "controlEdges":[]
            }
            """);

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "document.field_unknown" && issue.Path == "/future");
        Assert.Contains(result.Issues, issue => issue.Code == "node.field_unknown" && issue.Path == "/nodes/0/future");
    }

    [Fact]
    public void RejectsUnknownEdgeAndPortFields()
    {
        var result = FlowDocumentReader.Read("""
            {
              "id":"flow-1","formatVersion":1,"role":"ingress",
              "nodes":[],
              "dataEdges":[{"source":{"nodeId":"n1","port":"out","extra":1},"fieldPath":[],"target":{"nodeId":"n2","port":"in"},"kind":"data"}],
              "controlEdges":[]
            }
            """);

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "edge.field_unknown");
        Assert.Contains(result.Issues, issue => issue.Code == "port.field_unknown");
    }

    [Fact]
    public void RejectsJsonDepthOverflow()
    {
        var nested = new string('[', FlowLimits.MaxJsonParseDepth + 1) + "0" + new string(']', FlowLimits.MaxJsonParseDepth + 1);
        var json = "{\"id\":\"flow-1\",\"formatVersion\":1,\"role\":\"ingress\",\"nodes\":[{\"id\":\"n1\",\"kind\":\"input\",\"blockVersion\":1,\"config\":{\"value\":" + nested + "}}],\"dataEdges\":[],\"controlEdges\":[]}";
        var result = FlowDocumentReader.Read(json);

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "document.json_depth_exceeded");
    }

    [Fact]
    public void RejectsInputWhenUtf8ByteLimitIsExceeded()
    {
        var repeated = new string('é', FlowLimits.MaxInputUtf8Bytes / 2);
        var json = "{\"id\":\"" + repeated + "\",\"formatVersion\":1,\"role\":\"ingress\",\"nodes\":[],\"dataEdges\":[],\"controlEdges\":[]}";

        Assert.True(Encoding.UTF8.GetByteCount(json) > FlowLimits.MaxInputUtf8Bytes);
        var result = FlowDocumentReader.Read(json);

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "document.input_too_large");
    }

    [Fact]
    public void RejectsNodeAndEdgeLimits()
    {
        var nodes = string.Join(',', Enumerable.Range(0, FlowLimits.MaxNodes + 1)
            .Select(index => $"{{\"id\":\"n{index}\",\"kind\":\"input\",\"blockVersion\":1,\"config\":{{}}}}"));
        var json = $"{{\"id\":\"flow-1\",\"formatVersion\":1,\"role\":\"ingress\",\"nodes\":[{nodes}],\"dataEdges\":[],\"controlEdges\":[]}}";

        var result = FlowDocumentReader.Read(json);

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "document.nodes_limit");
    }

    [Fact]
    public void RejectsTotalEdgeLimit()
    {
        var edges = string.Join(',', Enumerable.Range(0, FlowLimits.MaxEdges + 1)
            .Select(_ => "{\"source\":{\"nodeId\":\"n1\",\"port\":\"out\"},\"target\":{\"nodeId\":\"n2\",\"port\":\"in\"}}"));
        var json = $"{{\"id\":\"flow-1\",\"formatVersion\":1,\"role\":\"ingress\",\"nodes\":[],\"dataEdges\":[],\"controlEdges\":[{edges}]}}";

        var result = FlowDocumentReader.Read(json);

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "document.edges_limit");
    }

    [Fact]
    public void RejectsPromptByteLimitWithoutEchoingPrompt()
    {
        var prompt = new string('x', FlowLimits.MaxPromptUtf8Bytes + 1);
        var json = $"{{\"id\":\"flow-1\",\"formatVersion\":1,\"role\":\"ingress\",\"nodes\":[{{\"id\":\"n1\",\"kind\":\"input\",\"blockVersion\":1,\"config\":{{\"prompt\":\"{prompt}\"}}}}],\"dataEdges\":[],\"controlEdges\":[]}}";

        var result = FlowDocumentReader.Read(json);

        Assert.Null(result.Document);
        Assert.Contains(result.Issues, issue => issue.Code == "node.prompt_too_large");
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(prompt, StringComparison.Ordinal));
    }

    [Fact]
    public void ClonesConfigJsonElementForDocumentOwnership()
    {
        var result = FlowDocumentReader.Read("""
            {"id":"flow-1","formatVersion":1,"role":"ingress","nodes":[{"id":"n1","kind":"input","blockVersion":1,"config":{"value":1}}],"dataEdges":[],"controlEdges":[]}
            """);

        Assert.NotNull(result.Document);
        var document = result.Document!;

        Assert.Equal(1, document.Nodes[0].Config.GetProperty("value").GetInt32());
    }
}
