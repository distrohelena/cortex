using Cortex.Evaluation.Storage;
using Cortex.Evaluation.Contracts;
using System.Runtime.InteropServices;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class StorageDefinitionTests
{
    [Fact]
    public void Artifact_does_not_expose_its_mutable_backing_array()
    {
        var artifact = new EvidenceArtifactDocument("test", false, new byte[] { 1, 2 });
        Assert.True(MemoryMarshal.TryGetArray(artifact.Content, out var exported));
        exported.Array![exported.Offset] = 9;
        Assert.Equal(1, artifact.Content.Span[0]);
    }

    [Fact]
    public void Migration_checksum_is_stable_across_windows_and_linux_checkout_line_endings()
    {
        Assert.Equal(MigrationRunner.ComputeChecksum("SELECT 1;\nSELECT 2;\n"),
            MigrationRunner.ComputeChecksum("SELECT 1;\r\nSELECT 2;\r\n"));
        Assert.NotEqual(MigrationRunner.ComputeChecksum("SELECT 1;\n"), MigrationRunner.ComputeChecksum("SELECT 2;\n"));
    }

    [Fact]
    public void Baseline_declares_the_original_revision_bytes_consumed_by_the_store()
    {
        using var stream = typeof(MigrationRunner).Assembly.GetManifestResourceStream("Filomena.Evaluation.Storage.Migrations.001_evaluation.sql");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        Assert.Contains("document_bytes bytea NOT NULL", sql);
    }
}
