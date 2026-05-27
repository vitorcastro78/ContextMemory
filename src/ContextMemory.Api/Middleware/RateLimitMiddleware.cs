using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Middleware;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitService _rateLimitService;
    private readonly IAppConfigStore _appConfigStore;

    public RateLimitMiddleware(
        RequestDelegate next,
        IRateLimitService rateLimitService,
        IAppConfigStore appConfigStore)
    {
        _next = next;
        _rateLimitService = rateLimitService;
        _appConfigStore = appConfigStore;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isLlmRoute = path.StartsWithSegments("/api/chat", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/generate", StringComparison.OrdinalIgnoreCase);

        if (!isLlmRoute)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var appId = context.Items[AuthMiddleware.AppIdItemKey] as string;
        var userId = context.Items[AuthMiddleware.UserIdItemKey] as string;

        if (appId is null || userId is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var config = _appConfigStore.GetConfig(appId);
        var estimatedTokens = EstimateRequestTokens(context);

        var result = _rateLimitService.TryAcquire(appId, userId, estimatedTokens, config.RateLimits);
        if (!result.IsAcquired)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded.",
                retry_after = result.RetryAfterSeconds
            }).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static int EstimateRequestTokens(HttpContext context)
    {
        if (context.Request.ContentLength is > 0 and var length)
            return (int)Math.Clamp(length / 4, 1, 50_000);

        return 500;
    }
}
