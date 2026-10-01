using Filomena.Core.Identity;
using Xunit;

namespace Cortex.Core.Tests;

public sealed class FoundationAssemblyTests
{
    [Fact]
    public void LegacyIdentityLivesInCortexWithoutApplicationDependencies()
    {
        var assembly = typeof(FilomenaIdentity).Assembly;
        Assert.Equal("Cortex.Core", assembly.GetName().Name);
        Assert.Equal("Filomena.Core.Identity.FilomenaIdentity", typeof(FilomenaIdentity).FullName);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name is not null && (reference.Name.StartsWith("Filomena.", StringComparison.Ordinal)
                || reference.Name.StartsWith("Cortex.", StringComparison.Ordinal)));
    }
}
