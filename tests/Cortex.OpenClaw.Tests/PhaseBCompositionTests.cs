using Filomena.App;
using Cortex.Core.Conversations;
using Cortex.Inference.LmStudio;
using Cortex.OpenClaw.Bridge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cortex.OpenClaw.Tests;

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
        Assert.Contains(typeof(OpenClawInboundBridgeReceiver), dependencies);
        Assert.DoesNotContain(typeof(ILoggerFactory), dependencies);
    }

    [Fact]
    public void InboundBridgeDefaultsToTheApprovedLoopbackEndpoint()
    {
        var options = new InboundBridgeOptions();

        Assert.Equal(new Uri("http://127.0.0.1:5147/"), options.ListenUrl);
        Assert.Equal("/v1/openclaw/events/inbound", options.Path);
        options.Validate();
    }

    [Fact]
    public void HostLoadsCopiedAppSettingsFromTheExecutableDirectory()
    {
        var builder = FilomenaHostConfiguration.CreateBuilder([]);

        Assert.Equal(
            Path.GetFullPath(AppContext.BaseDirectory),
            Path.GetFullPath(builder.Environment.ContentRootPath));
        Assert.Equal("phase-c", builder.Configuration["Probe:Mode"]);
    }

    [Fact]
    public void PhaseCOnlyAllowsHelenasPrivateConversationAndVerifiedTestGroup()
    {
        Assert.True(ProbeOptions.IsSupportedMode("phase-c"));
        var builder = FilomenaHostConfiguration.CreateBuilder([]);
        Assert.Equal("whatsapp-dm-5511944558218-s-whatsapp-net", builder.Configuration["PhaseC:AllowedConversationNamespaces:0"]);
        var allowed = builder.Configuration.GetSection("PhaseC:AllowedConversationNamespaces")
            .GetChildren().Select(item => item.Value).ToArray();
        Assert.Equal(new[] { "whatsapp-dm-5511944558218-s-whatsapp-net", "whatsapp-group-120363413037344295-g-us" }, allowed);
        Assert.DoesNotContain("whatsapp-group-5511981042361-1589823314-g-us", allowed);
        Assert.DoesNotContain("whatsapp-group-120363318973251043-g-us", allowed);
        Assert.Equal("8192", builder.Configuration["LmStudio:ArbitrationMaxTokens"]);
        Assert.Equal("8192", builder.Configuration["LmStudio:GenerationMaxTokens"]);
        Assert.Equal("00:00:03", builder.Configuration["PhaseB:Turns:AllowlistedDirectMessageQuietWindow"]);
        Assert.Equal("00:01:00", builder.Configuration["PhaseB:Turns:AllowlistedDirectMessageMaxBurst"]);
        Assert.Equal("50", builder.Configuration["PhaseB:Turns:MaxBurstMessages"]);
    }

    [Fact]
    public void RuntimeBindingDoesNotDuplicateOpenClawArguments()
    {
        var builder = FilomenaHostConfiguration.CreateBuilder([]);
        builder.Services
            .AddOptions<ProbeOptions>()
            .Bind(builder.Configuration.GetSection(ProbeOptions.SectionName));
        using var services = builder.Services.BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<ProbeOptions>>().Value;

        Assert.Equal(
            ["mcp", "serve", "--claude-channel-mode", "off"],
            options.OpenClawArguments);
    }
}
