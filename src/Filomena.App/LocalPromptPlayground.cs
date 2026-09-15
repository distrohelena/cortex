using System.Net;
using System.Text.Json;
using Filomena.Llm.LmStudio;
using Filomena.Core.Memory;
using Filomena.Memory;
using Filomena.App.Playground;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Features;

namespace Filomena.App;

// Isolated by default; live transport is enabled only through explicit composition.
public static class LocalPromptPlayground
{
    public static async Task RunAsync(bool liveReload = false)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables().Build();
        var options = configuration.GetSection("LmStudio").Get<LmStudioOptions>() ?? new();
        using var http = new HttpClient();
        var sourceDirectory = liveReload ? PlaygroundAssets.FindDevelopmentDirectory() : null;
        var memoryEndpoint = Environment.GetEnvironmentVariable("FILOMENA_GRAPHITI_MCP_ENDPOINT");
        await using var memoryClient = string.IsNullOrWhiteSpace(memoryEndpoint) ? null : new GraphitiReadClient(new Uri(memoryEndpoint), TimeSpan.FromSeconds(15));
        await using var app = Build(options, http, sourceDirectory: sourceDirectory,
            memoryReader: memoryClient is null ? null : new GraphitiMemoryReader(memoryClient));
        if (sourceDirectory is not null) Console.WriteLine($"Playground live reload: {sourceDirectory}");
        await app.RunAsync();
    }

    public static WebApplication Build(LmStudioOptions options, HttpClient httpClient, int port = 18793, string? sourceDirectory = null,
        string? projectDirectory = null, IMemoryReader? memoryReader = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, port);
            server.Limits.MaxRequestBodySize = 65536;
        });
        builder.Logging.AddSimpleConsole(logging => logging.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ");
        builder.Services.AddSingleton(new SessionFlowHarnessStore(memoryReader));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var origin = request.Headers.Origin.ToString();
            if (request.Host.Host != "127.0.0.1" || request.Host.Port != context.Connection.LocalPort
                || (origin.Length > 0 && origin != $"http://127.0.0.1:{context.Connection.LocalPort}")
                || (HttpMethods.IsPost(request.Method) && request.Headers["X-Filomena-Playground"] != "1"))
            {
                context.Response.StatusCode = 403;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if ((request.Path == "/api/flow/project" || request.Path.StartsWithSegments("/api/flow/typed")) && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
                bodySize.MaxRequestBodySize = 1048576;
            await next(context);
        });
        var projectStore = new PlaygroundProjectStore(projectDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Filomena", "playground"));
        app.MapGet("/api/flow/project", async (CancellationToken token) =>
        {
            try { var project = await projectStore.ReadAsync(token); return project is null ? Results.NoContent() : Results.Json(project.Value); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { return Results.Json(new { error = "Não foi possível carregar o projeto salvo. O arquivo foi preservado." }, statusCode: 503); }
        });
        app.MapPost("/api/flow/project", async (JsonElement document, CancellationToken token) =>
        {
            try { await projectStore.SaveAsync(document, token); return Results.Ok(new { saved = true }); }
            catch (InvalidDataException) { return Results.BadRequest(new { error = "Projeto inválido. Envie somente o desenho, até 1 MB." }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { return Results.Json(new { error = "Não foi possível salvar o projeto." }, statusCode: 503); }
        });
        TypedFlowEndpoints.Map(app);
        SessionFlowEndpoints.Map(app);
        var assets = new PlaygroundAssets(sourceDirectory);
        app.MapGet("/api/model", () => Results.Ok(new { model = options.Model }));
        app.MapGet("/api/boundary-enforcement", (string? semanticVersion, string? evaluatorVersion, string? templateVersion) =>
        {
            try
            {
                // Absent query preserves the historical endpoint; explicit v2 selection is never a latest alias.
                var template = Filomena.Core.Boundaries.BoundaryDetectionTemplates.Resolve(templateVersion ?? "no_teasing_about_topic:v1",
                    semanticVersion ?? "boundary-semantic-features:v1", evaluatorVersion ?? "no_teasing_about_topic:evaluator-v1");
                return Results.Ok(new
                {
                    executionProfile = Filomena.Core.Boundaries.BoundaryEnforcementProfile.Id,
                    semanticContractVersion = template.SemanticVersion,
                    boundaryEvaluatorVersion = template.EvaluatorVersion,
                    boundaryTemplateVersion = template.TemplateId,
                    prompt = template.SemanticContractDefinition,
                    modelOutputs = template.SemanticSchema.ToJsonSchema(),
                    enforcementOutputs = new[] { "resultado", "diagnostico", "explicacao" }
                });
            }
            catch (InvalidDataException) { return Results.BadRequest(new { error = "Versões de detector/evaluator não suportadas." }); }
        });
        app.MapGet("/api/boundary-policies", () => Results.Ok(new
        {
            taxonomyVersion = "v1",
            grammarVersion = Filomena.Core.Boundaries.BoundarySourceParserV1.Version,
            supportedForms = Filomena.Core.Boundaries.BoundarySourceParserV1.SupportedForms,
            supportedTopics = Filomena.Core.Boundaries.BoundarySourceParserV1.SupportedTopics,
            templatesVersion = "v1",
            policy = Filomena.Core.Boundaries.BoundaryPolicies.TopicV1,
            disclosure = Filomena.Core.Boundaries.BoundaryPolicies.DisclosureV1
        }));
        app.MapGet("/api/model-catalog", () =>
        {
            try { return Results.Ok(PlaygroundModelCatalog.Read(sourceDirectory)); }
            catch (Exception error) when (error is JsonException or InvalidDataException)
            {
                return Results.BadRequest(new { error = "Catálogo inválido. Confira model-catalog.json: versão 1, IDs únicos e listas de providers/modelos." });
            }
            catch (IOException) { return Results.Json(new { error = "Não foi possível ler model-catalog.json. Tente novamente após salvar o arquivo." }, statusCode: 503); }
        });
        foreach (var (route, resource, contentType) in PlaygroundAssets.Files)
        {
            app.MapGet(route, () =>
            {
                return Results.Content(assets.Read(resource, contentType == "text/html"), contentType + "; charset=utf-8");
            });
        }
        if (sourceDirectory is not null)
        {
            app.MapGet("/__playground/revision", () =>
            {
                try { return Results.Ok(new { revision = assets.Revision() }); }
                catch (IOException) { return Results.StatusCode(503); } // An editor may be replacing a file; retry next tick.
            });
            app.MapGet("/__playground/live-reload.js", () => Results.Content(assets.Read("live-reload.js"), "text/javascript; charset=utf-8"));
        }
        return app;
    }
}
