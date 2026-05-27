using ContextMemory.Core.Billing;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Middleware;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitService _rateLimitService;
    private readonly IAppConfigStore _appConfigStore;
    private readonly QuotaEnforcer _quotaEnforcer;
    private readonly IPlanStore _planStore;

    public RateLimitMiddleware(
        RequestDelegate next,
        IRateLimitService rateLimitService,
        IAppConfigStore appConfigStore,
        QuotaEnforcer quotaEnforcer,
        IPlanStore planStore)
    {
        _next = next;
        _rateLimitService = rateLimitService;
        _appConfigStore = appConfigStore;
        _quotaEnforcer = quotaEnforcer;
        _planStore = planStore;
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

        var quota = await _quotaEnforcer.CheckAsync(appId, estimatedTokens, context.RequestAborted).ConfigureAwait(false);
        if (!quota.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = Math.Max(1, (int)(quota.ResetAt - DateTimeOffset.UtcNow).TotalSeconds).ToString();
            context.Response.Headers["X-Quota-Remaining"] = "0";
            context.Response.Headers["X-Quota-Reset-At"] = quota.ResetAt.ToString("O");
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Daily quota exceeded.",
                reason = quota.Reason ?? "daily_quota_exceeded",
                retry_after = context.Response.Headers.RetryAfter.ToString()
            }).ConfigureAwait(false);
            return;
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Quota-Remaining"] = quota.RemainingRequests.ToString();
            context.Response.Headers["X-Quota-Reset-At"] = quota.ResetAt.ToString("O");
            return Task.CompletedTask;
        });

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

        if (context.Response.StatusCode < 400)
        {
            await _planStore
                .RecordUsageAsync(appId, 1, estimatedTokens, context.RequestAborted)
                .ConfigureAwait(false);
        }
    }

    private static int EstimateRequestTokens(HttpContext context)
    {
        if (context.Request.ContentLength is > 0 and var length)
            return (int)Math.Clamp(length / 4, 1, 50_000);

        return 500;
    }
}
