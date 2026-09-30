using Filomena.Core.Conversations;
using Filomena.Llm.LmStudio;
using Filomena.OpenClaw.Bridge;
using Filomena.OpenClaw.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filomena.App.FlowRuntime;

public static class DmFlowLiveHosting
{
    public static void Configure(IServiceCollection services, IConfiguration configuration, HttpClient http)
    {
        var live = configuration.GetSection("FlowLive").Get<DmFlowLiveOptions>() ?? new();live.Validate();
        var bridge = configuration.GetSection(InboundBridgeOptions.SectionName).Get<InboundBridgeOptions>() ?? new();bridge.Validate();
        var probe = configuration.GetSection(ProbeOptions.SectionName).Get<ProbeOptions>() ?? new();
        var model = configuration.GetSection(LmStudioOptions.SectionName).Get<LmStudioOptions>() ?? new();model.Validate();
        services.AddSingleton(live);
        services.AddSingleton<IOptions<InboundBridgeOptions>>(Options.Create(bridge));
        services.AddSingleton<IModelResponseChatClient>(new LmStudioClient(http, model));
        services.AddSingleton<OpenClawMcpSender>(sp => new OpenClawMcpSender(
            async token => await OpenClawMcpClient.ConnectAsync(
                new OpenClawMcpClientOptions(probe.OpenClawCommand, probe.EffectiveOpenClawArguments),
                sp.GetRequiredService<ILoggerFactory>(), token), sp.GetRequiredService<ILogger<OpenClawMcpSender>>()));
        services.AddSingleton<IConversationSender>(sp => sp.GetRequiredService<OpenClawMcpSender>());
        services.AddSingleton<DmFlowExecutor>();
        services.AddSingleton<DmFlowLiveSession>();
        services.AddSingleton(sp => new OpenClawInboundBridgeReceiver(sp.GetRequiredService<DmFlowLiveSession>().PrepareInbound));
        services.AddHostedService<OpenClawMcpWarmupService>();
        services.AddHostedService<InboundBridgeHttpServer>();
        services.AddHostedService<DmFlowLiveWorker>();
    }
}
