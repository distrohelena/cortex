using Cortex.Video.Editing;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class EditingSchemaTests
{
    [Theory]
    [InlineData("cortex.edit.v1")]
    [InlineData("cortex.edit.plan.v1")]
    [InlineData("cortex.edit.recording_tasks.v1")]
    [InlineData("cortex.edit.resolved.v1")]
    public void PortableSchemasAreEmbeddedVersionedAndClosedAtTheirRoot(string id)
    {
        var schema = EditingSchemas.Get(id);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.GetProperty("$schema").GetString());
        Assert.Equal(id, schema.GetProperty("properties").GetProperty("schema").GetProperty("const").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
    }
}
