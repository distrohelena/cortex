using Cortex.Evaluation.Storage;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class EvaluationAssemblyTests
{
    [Fact]
    public void WorkbenchDoesNotLoadTheCharacterLibrary()
    {
        Assert.DoesNotContain(typeof(MigrationCatalog).Assembly.GetReferencedAssemblies(), r =>
            r.Name?.StartsWith("Filomena.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void WorkbenchUsesCortexAssembly()
    {
        Assert.Equal("Cortex.Evaluation", typeof(MigrationCatalog).Assembly.GetName().Name);
    }
}
