using ContextMemory.Core.Contracts;
using ContextMemory.Core.Configuration;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Endpoints;

public static class HealthEndpoint
{
    public static void MapHealthEndpoint(this WebApplication app)
    {
        app.MapGet("/health", GetHealthAsync);
    }

    private static async Task<IResult> GetHealthAsync(
        ILlmAdapterResolver adapterResolver,
        IAppRegistry appRegistry,
        IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        var ollamaHealthy = await adapterResolver.Resolve("ollama").IsHealthyAsync().ConfigureAwait(false);
        var appsLoaded = appRegistry.GetAllApps().Count > 0;

        var status = ollamaHealthy && appsLoaded ? "healthy" : "degraded";
        var code = ollamaHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

        return Results.Json(new
        {
            status,
            checks = new
            {
                ollama = ollamaHealthy ? "up" : "down",
                appsLoaded,
                dataPath = config.DataPath
            }
        }, statusCode: code);
    }
}
