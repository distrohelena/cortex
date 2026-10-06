using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Video.Editing;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class EditingSerializationTests
{
    public static IEnumerable<object[]> Fixtures => new[] { "script.json", "edit_plan.json", "recording_tasks.json", "resolved_edit_plan.json", "after-pickups/edit_plan.json", "after-pickups/recording_tasks.json" }.Select(path => new object[] { path });

    [Theory, MemberData(nameof(Fixtures))]
    public void AllDocumentsRoundTripWithoutDroppingIntentOrEvidence(string name)
    {
        var source = Fixture(name);
        var model = EditingJson.Parse(source);
        var result = EditingJson.Serialize(model);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source.GetRawText()), JsonNode.Parse(result.GetRawText())), name);
    }

    [Fact]
    public void AuthoredContentAndArbitraryAnnotationsSurvive()
    {
        var value = JsonNode.Parse(Fixture("script.json").GetRawText())!;
        value["metadata"]!["custom"] = JsonNode.Parse("{\"mixed\":[1,\"nota\",null,{\"x\":true}]}");
        var model = Assert.IsType<EditScriptDocument>(EditingJson.Parse(JsonSerializer.SerializeToElement(value)));
        Assert.Equal(7, model.Beats.Count);
        Assert.Contains("saúde pública", model.Beats[3].Audio.Fallback!.Script);
        Assert.True(JsonNode.DeepEquals(value, JsonNode.Parse(EditingJson.Serialize(model).GetRawText())));
    }

    [Theory]
    [InlineData("{\"schema\":\"unknown\"}")]
    [InlineData("{\"schema\":\"cortex.edit.v1\",\"schema\":\"cortex.edit.v1\"}")]
    [InlineData("{\"schema\":\"cortex.edit.v1\",\"revision\":\"one\"}")]
    public void InvalidDiscriminantsDuplicatesAndScalarTypesFail(string json)
        => Assert.Throws<InvalidDataException>(() => EditingJson.Parse(JsonDocument.Parse(json).RootElement));

    [Fact]
    public void MisspelledFieldsAndNoncanonicalEnumCodesFail()
    {
        var value = JsonNode.Parse(Fixture("script.json").GetRawText())!;
        value["project"]!["format"] = "VERTICAL";
        Assert.Throws<InvalidDataException>(() => EditingJson.Parse(JsonSerializer.SerializeToElement(value)));
        value["project"]!["format"] = "vertical";
        value["revisionn"] = 1;
        Assert.Throws<InvalidDataException>(() => EditingJson.Parse(JsonSerializer.SerializeToElement(value)));
    }

    internal static JsonElement Fixture(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Repository(), "docs/examples/cortex-edit-v1", name)));
        return document.RootElement.Clone();
    }

    internal static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cortex.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
