using System.Text.Json;

namespace Cortex.Core.Flows.Durable;

/// <summary>Private, immutable artifacts. References never authorize host effects.</summary>
public interface IExecutionArtifactStore
{
    ValueTask<ArtifactRef> PutOnceAsync(ArtifactScope scope, string key, JsonElement payload, CancellationToken token);
    ValueTask<JsonElement> ReadAsync(ArtifactRef reference, CancellationToken token);
    ValueTask<ArtifactRef?> FindAsync(ArtifactScope scope, string key, CancellationToken token);
}
