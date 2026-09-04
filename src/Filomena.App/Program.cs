using Filomena.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddOptions<ProbeOptions>()
    .Bind(builder.Configuration.GetSection(ProbeOptions.SectionName))
    .Validate(
        options => options.Mode is "inspect" or "list" or "wait" or "milestone-a",
        "Probe:Mode must be inspect, list, wait, or milestone-a.")
    .Validate(
        options => options.WaitTimeoutSeconds is >= 1 and <= 300,
        "Probe:WaitTimeoutSeconds must be between 1 and 300.")
    .Validate(
        options => options.ReconnectDelaySeconds is >= 1 and <= 60,
        "Probe:ReconnectDelaySeconds must be between 1 and 60.")
    .ValidateOnStart();
builder.Services.AddHostedService<OpenClawProbeWorker>();

await builder.Build().RunAsync();
