using Microsoft.Extensions.Hosting;

namespace Filomena.App;

public static class FilomenaHostConfiguration
{
    public static HostApplicationBuilder CreateBuilder(string[] args) =>
        Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
}
