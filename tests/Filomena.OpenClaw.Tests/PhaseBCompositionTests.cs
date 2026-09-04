using Filomena.App;
using Filomena.Core.Conversations;
using Filomena.Llm.LmStudio;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class PhaseBCompositionTests
{
    [Fact]
    public void DefaultsMatchApprovedBurstAndContextPolicy()
    {
        var options = new PhaseBOptions();

        Assert.Equal(TimeSpan.FromSeconds(1.8), options.Turns.AllowlistedDirectMessageQuietWindow);
        Assert.Equal(TimeSpan.FromSeconds(6), options.Turns.AllowlistedDirectMessageMaxBurst);
        Assert.Equal(TimeSpan.FromSeconds(1.5), options.Turns.DirectAddressGroupQuietWindow);
        Assert.Equal(TimeSpan.FromSeconds(8), options.Turns.DirectAddressGroupMaxBurst);
        Assert.Equal(TimeSpan.FromSeconds(3.5), options.Turns.GroupQuietWindow);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Turns.GroupMaxBurst);
        Assert.Equal(16, options.Turns.RecentMessageLimit);
        Assert.Equal(3000, options.Turns.RecentCharacterLimit);
        Assert.Equal(TimeSpan.FromMinutes(10), options.Turns.RecentWindow);
    }

    [Fact]
    public void UsesTheExistingGemmaModelByDefault()
    {
        var options = new LmStudioOptions();

        Assert.Equal("google/gemma-4-e4b", options.Model);
        Assert.Equal(new Uri("http://localhost:1234/v1/"), options.Endpoint);
    }

    [Fact]
    public void PhaseBWorkerHasNoOutboundTransportDependency()
    {
        var dependencies = typeof(FilomenaTurnWorker)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(dependencies, type =>
            type.Name.Contains("Outbound", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Sender", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(typeof(ConversationTurnManager), dependencies);
    }

    [Fact]
    public void HostLoadsCopiedAppSettingsFromTheExecutableDirectory()
    {
        var builder = FilomenaHostConfiguration.CreateBuilder([]);

        Assert.Equal(
            Path.GetFullPath(AppContext.BaseDirectory),
            Path.GetFullPath(builder.Environment.ContentRootPath));
        Assert.Equal("phase-b", builder.Configuration["Probe:Mode"]);
    }
}
