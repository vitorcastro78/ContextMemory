using ContextMemory.Core.Contracts;
using ContextMemory.Core.Security;

namespace ContextMemory.Api.Middleware;

public sealed class AuthMiddleware
{
    public const string AppIdItemKey = "ContextMemory.AppId";
    public const string UserIdItemKey = "ContextMemory.UserId";

    private const string AppIdHeader = "X-App-Id";
    private const string UserIdHeader = "X-User-Id";

    private readonly RequestDelegate _next;
    private readonly IAppRegistry _appRegistry;
    private readonly string _masterKey;
    private readonly int _maxPayloadBytes;

    public AuthMiddleware(
        RequestDelegate next,
        IAppRegistry appRegistry,
        IConfiguration configuration)
    {
        _next = next;
        _appRegistry = appRegistry;
        _masterKey = configuration.GetValue<string>("ContextMemory:MasterKey") ?? string.Empty;
        _maxPayloadBytes = configuration.GetValue<int?>("ContextMemory:MaxPayloadBytes") ?? 1_048_576;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (IsPublicPath(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (IsAdminPath(path))
        {
            if (!ValidateMasterKey(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid master key." }).ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        if (IsRegisterPath(path))
        {
            if (!ValidateMasterKey(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid master key." }).ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        if (TryGetWikiAppId(path, out var wikiAppId))
        {
            if (!await ValidateWikiUploadAsync(context, wikiAppId).ConfigureAwait(false))
                return;

            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!RequiresAuth(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (context.Request.ContentLength is > 0 and var length && length > _maxPayloadBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = "Payload too large." }).ConfigureAwait(false);
            return;
        }

        if (!context.Request.Headers.TryGetValue(AppIdHeader, out var appIdValues)
            || !context.Request.Headers.TryGetValue(UserIdHeader, out var userIdValues))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing X-App-Id or X-User-Id header." })
                .ConfigureAwait(false);
            return;
        }

        var appId = appIdValues.ToString();
        var userId = userIdValues.ToString();

        if (!IdentifierValidator.IsValid(appId) || !IdentifierValidator.IsValid(userId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid appId or userId format." })
                .ConfigureAwait(false);
            return;
        }

        if (!TryGetBearerToken(context, out var apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid Authorization header." })
                .ConfigureAwait(false);
            return;
        }

        if (!_appRegistry.TryGetApp(appId, out _) || !_appRegistry.ValidateApiKey(appId, apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid appId or API key." })
                .ConfigureAwait(false);
            return;
        }

        context.Items[AppIdItemKey] = appId;
        context.Items[UserIdItemKey] = userId;

        await _next(context).ConfigureAwait(false);
    }

    private async Task<bool> ValidateWikiUploadAsync(HttpContext context, string wikiAppId)
    {
        if (context.Request.ContentLength is > 0 and var length && length > _maxPayloadBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = "Payload too large." }).ConfigureAwait(false);
            return false;
        }

        if (!context.Request.Headers.TryGetValue(AppIdHeader, out var appIdValues)
            || !string.Equals(appIdValues.ToString(), wikiAppId, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "X-App-Id does not match wiki appId." })
                .ConfigureAwait(false);
            return false;
        }

        if (!TryGetBearerToken(context, out var apiKey)
            || !_appRegistry.ValidateApiKey(wikiAppId, apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key for wiki upload." })
                .ConfigureAwait(false);
            return false;
        }

        var userId = context.Request.Headers.TryGetValue(UserIdHeader, out var userIdValues)
            && IdentifierValidator.IsValid(userIdValues.ToString())
                ? userIdValues.ToString()!
                : "wiki-uploader";

        context.Items[AppIdItemKey] = wikiAppId;
        context.Items[UserIdItemKey] = userId;
        return true;
    }

    private bool ValidateMasterKey(HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(_masterKey))
            return false;

        return TryGetBearerToken(context, out var token)
            && string.Equals(token, _masterKey, StringComparison.Ordinal);
    }

    private static bool IsPublicPath(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdminPath(PathString path) =>
        path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);

    private static bool IsRegisterPath(PathString path) =>
        path.StartsWithSegments("/apps/register", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetWikiAppId(PathString path, out string appId)
    {
        appId = string.Empty;
        if (!path.StartsWithSegments("/apps", out var remaining))
            return false;

        var segments = remaining.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is null || segments.Length != 2)
            return false;

        if (!string.Equals(segments[1], "wiki", StringComparison.OrdinalIgnoreCase))
            return false;

        appId = segments[0];
        return IdentifierValidator.IsValid(appId);
    }

    private static bool RequiresAuth(PathString path) =>
        path.StartsWithSegments("/api/chat", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/generate", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/apps", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetBearerToken(HttpContext context, out string token)
    {
        token = string.Empty;
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        token = header["Bearer ".Length..].Trim();
        return token.Length > 0;
    }
}
