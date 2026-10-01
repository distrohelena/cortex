using Cortex.Memory;
using Cortex.OpenClaw.Mcp;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class AdapterAssemblyTests
{
    [Fact]
    public void MemoryAndTransportRemainOptionalCortexAdapters()
    {
        Assert.Equal("Cortex.Memory", typeof(GraphitiMemoryReader).Assembly.GetName().Name);
        Assert.Equal("Cortex.OpenClaw", typeof(OpenClawMcpSender).Assembly.GetName().Name);
        foreach (var assembly in new[] { typeof(GraphitiMemoryReader).Assembly, typeof(OpenClawMcpSender).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), r =>
                r.Name?.StartsWith("Filomena.", StringComparison.Ordinal) == true);
    }
}
