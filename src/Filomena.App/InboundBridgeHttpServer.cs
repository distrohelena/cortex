using System.Text.Json;
using Cortex.OpenClaw.Bridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filomena.App;

public sealed class InboundBridgeHttpServer(
    IOptions<InboundBridgeOptions> configuredOptions,
    OpenClawInboundBridgeReceiver receiver,
    ILogger<InboundBridgeHttpServer> logger) : BackgroundService
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => ready.Task;
    public string StartupPhase { get; private set; } = "created";

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken);
        await Ready.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            StartupPhase = "validating_options";
            var options = configuredOptions.Value;
            options.Validate();

            StartupPhase = "creating_web_builder";
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(InboundBridgeHttpServer).Assembly.FullName,
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(options.ListenUrl.ToString());
            builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 262144);

            StartupPhase = "building_web_application";
            await using var application = builder.Build();
            application.MapPost(options.Path, async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                if (!request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                try
                {
                    using var document = await JsonDocument.ParseAsync(
                        request.Body,
                        cancellationToken: cancellationToken);
                    var acceptance = await receiver.AcceptAsync(document.RootElement, cancellationToken);
                    return acceptance == InboundBridgeAcceptance.Invalid
                        ? Results.BadRequest(new { status = "invalid" })
                        : Results.Json(
                            new { status = acceptance.ToString().ToLowerInvariant() },
                            statusCode: StatusCodes.Status202Accepted);
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { status = "invalid_json" });
                }
            });

            StartupPhase = "starting_web_application";
            await application.StartAsync(stoppingToken);
            StartupPhase = "ready";
            logger.LogInformation(
                "Filomena inbound event bridge listening at {Endpoint}",
                new Uri(options.ListenUrl, options.Path));
            ready.TrySetResult();
            try
            {
                await application.WaitForShutdownAsync(stoppingToken);
            }
            finally
            {
                await application.StopAsync(CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            ready.TrySetException(exception);
            throw;
        }
    }

}
