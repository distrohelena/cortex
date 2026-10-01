using Cortex.Inference;
using Xunit;

namespace Cortex.Inference.Tests;

public sealed class InferenceAssemblyTests
{
    [Fact]
    public void GenericProviderCanBeLoadedWithoutFilomenaApplication()
    {
        var assembly = typeof(ProviderFlowCompletion).Assembly;
        Assert.Equal("Cortex.Inference", assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), r =>
            r.Name?.StartsWith("Filomena.", StringComparison.Ordinal) == true);
    }
}
