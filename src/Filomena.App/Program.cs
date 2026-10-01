using Filomena.App;
using Cortex.Core.Arbitration;
using Cortex.Core.Conversations;
using Filomena.Llm.Arbitration;
using Filomena.Llm.Agent;
using Cortex.Inference.LmStudio;
using Cortex.OpenClaw.Bridge;
using Cortex.OpenClaw.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

if (args.Contains("--system-one", StringComparer.Ordinal))
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Environment.ExitCode = await SystemOneCommand.RunAsync(args, Console.Out, cancellation.Token);
    return;
}

if (args.Contains("--native-conversation-host", StringComparer.Ordinal))
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Environment.ExitCode = await NativeConversationHostCommand.RunAsync(args, Console.In, Console.Out, cancellation.Token);
    return;
}

if (args.Contains("--native-live", StringComparer.Ordinal))
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Environment.ExitCode = await NativeLiveCommand.RunAsync(args, Console.In, Console.Out, cancellation.Token);
    return;
}

if (args.Contains("--native-shadow", StringComparer.Ordinal))
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Environment.ExitCode = await NativeShadowCommand.RunAsync(args, Console.In, Console.Out, cancellation.Token);
    return;
}

if (args.Contains("--manual-shadow", StringComparer.Ordinal))
{
    Environment.ExitCode = await ManualShadowCommand.RunAsync(args, Console.In, Console.Out, CancellationToken.None);
    return;
}

if (args.Contains("--shadow-preflight", StringComparer.Ordinal))
{
    Environment.ExitCode = await ShadowPreflightCommand.RunAsync(args, Console.Out, CancellationToken.None);
    return;
}

if (args.Contains("--playground", StringComparer.Ordinal))
{
    await LocalPromptPlayground.RunAsync(args.Contains("--live-reload", StringComparer.Ordinal), args.Contains("--flow-live", StringComparer.Ordinal));
    return;
}
if (args.Contains("--flow-live", StringComparer.Ordinal))
    throw new ArgumentException("Use --flow-live junto com --playground.");

var builder = FilomenaHostConfiguration.CreateBuilder(args);
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ");
builder.Services
    .AddOptions<ProbeOptions>()
    .Bind(builder.Configuration.GetSection(ProbeOptions.SectionName))
    .Validate(
        options => ProbeOptions.IsSupportedMode(options.Mode),
        "Probe:Mode must be inspect, list, wait, milestone-a, phase-b, or phase-c.")
    .Validate(
        options => options.WaitTimeoutSeconds is >= 1 and <= 300,
        "Probe:WaitTimeoutSeconds must be between 1 and 300.")
    .Validate(
        options => options.ReconnectDelaySeconds is >= 1 and <= 60,
        "Probe:ReconnectDelaySeconds must be between 1 and 60.")
    .ValidateOnStart();

var mode = builder.Configuration[$"{ProbeOptions.SectionName}:Mode"] ?? "milestone-a";
if (mode is "phase-b" or "phase-c")
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
    builder.Services.AddSingleton<LmStudioClient>();
    builder.Services.AddSingleton(sp => new TimedLmStudioClient(
        sp.GetRequiredService<LmStudioClient>(), sp.GetRequiredService<LmStudioClient>(),
        sp.GetRequiredService<ILogger<TimedLmStudioClient>>()));
    builder.Services.AddSingleton<ILmStudioChatClient>(sp => sp.GetRequiredService<TimedLmStudioClient>());
    if (mode == "phase-c")
    {
        builder.Services.AddOptions<FilomenaAgentOptions>()
            .Bind(builder.Configuration.GetSection(FilomenaAgentOptions.SectionName))
            .Validate(options => IsValid(options.Validate), "Invalid PhaseC generation settings.")
            .ValidateOnStart();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<FilomenaAgentOptions>>().Value);
        builder.Services.AddSingleton<IResponseChatClient>(sp => sp.GetRequiredService<TimedLmStudioClient>());
        builder.Services.AddSingleton<OpenClawMcpSender>(sp =>
        {
            var probe = sp.GetRequiredService<IOptions<ProbeOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new OpenClawMcpSender(async token => await OpenClawMcpClient.ConnectAsync(
                new OpenClawMcpClientOptions(probe.OpenClawCommand, probe.EffectiveOpenClawArguments),
                loggerFactory, token), sp.GetRequiredService<ILogger<OpenClawMcpSender>>());
        });
        builder.Services.AddSingleton<IConversationSender>(sp => sp.GetRequiredService<OpenClawMcpSender>());
        builder.Services.AddHostedService<OpenClawMcpWarmupService>();
        builder.Services.AddSingleton<IConversationTurnExecutor, FilomenaAgent>();
    }
    builder.Services.AddSingleton<ITurnArbiter>(sp => new GemmaTurnArbiter(
        sp.GetRequiredService<ILmStudioChatClient>(),
        (attempt, reason) => sp.GetRequiredService<ILogger<GemmaTurnArbiter>>()
            .LogWarning("Arbiter output rejected: attempt={Attempt} validation={Validation}", attempt, reason)));
    builder.Services.AddSingleton<OpenClawInboundBridgeReceiver>();
    builder.Services.AddSingleton(serviceProvider => new ConversationTurnManager(
        serviceProvider.GetRequiredService<ITurnArbiter>(),
        serviceProvider.GetRequiredService<IOptions<PhaseBOptions>>().Value.Turns,
        executor: serviceProvider.GetService<IConversationTurnExecutor>()));
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
