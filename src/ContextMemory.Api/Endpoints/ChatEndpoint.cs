using System.Text.Json;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class ChatEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapChatEndpoint(this WebApplication app)
    {
        app.MapPost("/api/chat", HandleChatAsync).DisableAntiforgery();
    }

    private static async Task HandleChatAsync(
        HttpContext httpContext,
        OllamaRequest request,
        IContextEngine contextEngine,
        CancellationToken cancellationToken)
    {
        var appId = (string)httpContext.Items[AuthMiddleware.AppIdItemKey]!;
        var userId = (string)httpContext.Items[AuthMiddleware.UserIdItemKey]!;

        var isStreaming = request.Stream ?? false;

        try
        {
            if (!isStreaming)
            {
                var result = await contextEngine
                    .ProcessChatAsync(appId, userId, request, cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageId is not null)
                    httpContext.Response.Headers["X-Context-Memory-Message-Id"] = result.MessageId;

                if (result.IsBlocked)
                {
                    httpContext.Response.StatusCode = result.StatusCode;
                    httpContext.Response.ContentType = "application/json";
                    await httpContext.Response.WriteAsync(result.ErrorBody ?? "{}", cancellationToken).ConfigureAwait(false);
                    return;
                }

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response
                    .WriteAsJsonAsync(result.Response, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/x-ndjson";

            var assistantContent = new System.Text.StringBuilder();

            await foreach (var chunk in contextEngine
                .ProcessChatStreamAsync(appId, userId, request, cancellationToken)
                .ConfigureAwait(false))
            {
                if (chunk.Message?.Content is { Length: > 0 } content)
                    assistantContent.Append(content);

                var line = JsonSerializer.Serialize(chunk, JsonOptions);
                await httpContext.Response.WriteAsync(line + "\n", cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var final = await contextEngine
                .FinalizeStreamAsync(appId, userId, request, assistantContent.ToString(), cancellationToken)
                .ConfigureAwait(false);

            if (final.MessageId is not null)
                httpContext.Response.Headers["X-Context-Memory-Message-Id"] = final.MessageId;
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
