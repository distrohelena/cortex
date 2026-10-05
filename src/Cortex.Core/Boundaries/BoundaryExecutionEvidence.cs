using System.Text.Json;
using Cortex.Core.Memory;

namespace Cortex.Core.Boundaries;

internal sealed record MemoryExecutionEvidence(string Handle, MemoryItem Snapshot, JsonElement Visual);
internal sealed record CompiledBoundaryEvidence(string Handle, MemoryItem Source, string SourceHash,
    BoundaryAdmissionResult Admission, JsonElement Visual);
internal sealed record BoundaryExecutionEvidence(int Version, string ConversationNamespace,
    IReadOnlyList<MemoryExecutionEvidence> Memories, IReadOnlyList<CompiledBoundaryEvidence> Boundaries);
