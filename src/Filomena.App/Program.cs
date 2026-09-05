using Filomena.App;
using Filomena.Core.Arbitration;
using Filomena.Core.Conversations;
using Filomena.Llm.Arbitration;
using Filomena.Llm.LmStudio;
using Filomena.OpenClaw.Bridge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = FilomenaHostConfiguration.CreateBuilder(args);
builder.Services
    .AddOptions<ProbeOptions>()
    .Bind(builder.Configuration.GetSection(ProbeOptions.SectionName))
    .Validate(
        options => ProbeOptions.IsSupportedMode(options.Mode),
        "Probe:Mode must be inspect, list, wait, milestone-a, or phase-b.")
    .Validate(
        options => options.WaitTimeoutSeconds is >= 1 and <= 300,
        "Probe:WaitTimeoutSeconds must be between 1 and 300.")
    .Validate(
        options => options.ReconnectDelaySeconds is >= 1 and <= 60,
        "Probe:ReconnectDelaySeconds must be between 1 and 60.")
    .ValidateOnStart();

var mode = builder.Configuration[$"{ProbeOptions.SectionName}:Mode"] ?? "milestone-a";
if (string.Equals(mode, "phase-b", StringComparison.Ordinal))
{
    builder.Services
        .AddOptions<InboundBridgeOptions>()
        .Bind(builder.Configuration.GetSection(InboundBridgeOptions.SectionName))
        .Validate(
            options => IsValid(options.Validate),
            "InboundBridge must use a valid loopback HTTP endpoint and absolute path.")
        .ValidateOnStart();
    builder.Services
        .AddOptions<PhaseBOptions>()
        .Bind(builder.Configuration.GetSection(PhaseBOptions.SectionName))
        .Validate(
            options => IsValid(() => options.Turns.Validate()),
            "PhaseB:Turns contains invalid scheduling values.")
        .ValidateOnStart();
    builder.Services
        .AddOptions<LmStudioOptions>()
        .Bind(builder.Configuration.GetSection(LmStudioOptions.SectionName))
        .Validate(
            options => IsValid(options.Validate),
            "LmStudio contains invalid endpoint, model, or arbitration values.")
        .ValidateOnStart();
    builder.Services.AddSingleton(serviceProvider =>
        serviceProvider.GetRequiredService<IOptions<LmStudioOptions>>().Value);
    builder.Services.AddSingleton<HttpClient>();
    builder.Services.AddSingleton<ILmStudioChatClient, LmStudioClient>();
    builder.Services.AddSingleton<ITurnArbiter, GemmaTurnArbiter>();
    builder.Services.AddSingleton<OpenClawInboundBridgeReceiver>();
    builder.Services.AddSingleton(serviceProvider => new ConversationTurnManager(
        serviceProvider.GetRequiredService<ITurnArbiter>(),
        serviceProvider.GetRequiredService<IOptions<PhaseBOptions>>().Value.Turns));
    builder.Services.AddHostedService<InboundBridgeHttpServer>();
    builder.Services.AddHostedService<FilomenaTurnWorker>();
}
else
{
    builder.Services.AddHostedService<OpenClawProbeWorker>();
}

await builder.Build().RunAsync();

static bool IsValid(Action validation)
{
    try
    {
        validation();
        return true;
    }
    catch (Exception exception) when (exception is
        ArgumentException or
        InvalidOperationException)
    {
        return false;
    }
}
