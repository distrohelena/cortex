using Xunit;

namespace Cortex.Inference.Tests.LocalModels;

/// <summary>
/// Every test that starts a local GPU server shares this collection, and it never runs in parallel with any other, so
/// at most one llama-server or laya.serve holds the GPU at a time and latencies are not measured under contention.
/// </summary>
[CollectionDefinition(Collection, DisableParallelization = true)]
public sealed class LocalModelServers
{
    internal const string Collection = "Local model servers";
}
