using Xunit;

namespace Cortex.Personas.Tests;

public sealed class PersonaRelocationCompatibilityTests
{
    [Fact]
    public void FrozenSmokeArtifactHashIsPreserved()
    {
        Assert.Equal(
            "a466eb8e6e95ed859ced9ddc7c841f898b89e856b292a9f6f024190219698481",
            new PersonaRevisionCodec().Encode(PersonaFixtures.SmokeBundle()).ArtifactSha256);
    }
}
