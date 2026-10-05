using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Memory;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class TrustedEvidenceTests
{
    private const string Namespace = "whatsapp-dm-5511944558218-s-whatsapp-net";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-05T12:00:00Z");
    private static MemoryItem Source() => new("source", Namespace, "Não brinque sobre meu trabalho.", MemoryCategory.Boundary,
        [MemoryIdentity.Person(Namespace, "whatsapp:5511944558218@s.whatsapp.net", "Helena")], "exact", InvalidAt: Now.AddHours(1));

    [Fact]
    public async Task MemoryAndBoundaryHandlesSurviveRegistryRecreation()
    {
        var registry = new BoundaryExecutionRegistry(Namespace);
        var memories = registry.RegisterMemories([Source()]);
        var boundaries = await registry.CompileAsync(memories, new("v1", "v1", "v1"), Now, TestContext.Current.CancellationToken);
        using var directory = new ArtifactDirectory();
        await using var store = directory.Open();
        var reference = await store.PutOnceAsync(DurableIdentityTests.Scope, "evidence", JsonSerializer.SerializeToElement(registry.CaptureEvidence()), TestContext.Current.CancellationToken);
        var evidence = (await store.ReadAsync(reference, TestContext.Current.CancellationToken)).Deserialize<BoundaryExecutionEvidence>()!;
        var restored = BoundaryExecutionRegistry.RestoreEvidence(Namespace, evidence, Now);
        Assert.Equal("source", Assert.Single(restored.ResolveBoundaries(boundaries, Now)).Source.MemoryId);
        Assert.Equal(1, (await restored.CompileAsync(memories, new("v1", "v1", "v1"), Now, TestContext.Current.CancellationToken)).GetArrayLength());
        Assert.Contains(restored.CaptureEvidence().Boundaries, item => item.Handle == boundaries[0].GetProperty("_boundary").GetString());
    }

    [Fact]
    public async Task ForgedVisualForeignNamespaceAndExpiredSourceFail()
    {
        var registry = new BoundaryExecutionRegistry(Namespace);
        var memories = registry.RegisterMemories([Source()]);
        await registry.CompileAsync(memories, new("v1", "v1", "v1"), Now, TestContext.Current.CancellationToken);
        var evidence = registry.CaptureEvidence();
        Assert.Throws<BoundaryCompilationException>(() => BoundaryExecutionRegistry.RestoreEvidence("foreign", evidence, Now));
        Assert.Throws<BoundaryCompilationException>(() => BoundaryExecutionRegistry.RestoreEvidence(Namespace, evidence, Now.AddHours(2)));
        var corrupted = evidence with { Memories = [evidence.Memories.Single() with { Visual = ExecutionSnapshotCodecTests.Json("{}") }] };
        Assert.Throws<BoundaryCompilationException>(() => BoundaryExecutionRegistry.RestoreEvidence(Namespace, corrupted, Now));
    }
}
