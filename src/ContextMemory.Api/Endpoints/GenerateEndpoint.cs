using System.Text.Json;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class GenerateEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapGenerateEndpoint(this WebApplication app)
    {
        app.MapPost("/api/generate", HandleGenerateAsync).DisableAntiforgery();
    }

    private static async Task HandleGenerateAsync(
        HttpContext httpContext,
        OllamaGenerateRequest request,
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        ILlmAdapterResolver adapterResolver,
        CancellationToken cancellationToken)
    {
        var appId = (string)httpContext.Items[AuthMiddleware.AppIdItemKey]!;

        if (!appRegistry.TryGetApp(appId, out _))
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsJsonAsync(new { error = "App not found." }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var runtimeConfig = appConfigStore.GetConfig(appId);
        var adapter = adapterResolver.Resolve(runtimeConfig.LlmBackend);
        var isStreaming = request.Stream ?? false;

        try
        {
            if (!isStreaming)
            {
                var response = await adapter
                    .GenerateAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response
                    .WriteAsJsonAsync(response, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/x-ndjson";

            await foreach (var chunk in adapter
                .GenerateStreamAsync(request, cancellationToken)
                .ConfigureAwait(false))
            {
                var line = JsonSerializer.Serialize(chunk, JsonOptions);
                await httpContext.Response.WriteAsync(line + "\n", cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            httpContext.Response.StatusCode = (int)ex.StatusCode.Value;
            if (!string.IsNullOrEmpty(ex.Message))
            {
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
