using System.Diagnostics;
using System.Text.Json;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

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
        IContentFilter contentFilter,
        IContentRulesStore contentRulesStore,
        IAuditLog auditLog,
        ITelemetryCollector telemetry,
        IOptions<ContextMemoryOptions> options,
        CancellationToken cancellationToken)
    {
        var appId = (string)httpContext.Items[AuthMiddleware.AppIdItemKey]!;
        var userId = (string)httpContext.Items[AuthMiddleware.UserIdItemKey]!;
        var sw = Stopwatch.StartNew();
        var contentFilterEnabled = options.Value.EnableContentFilter;

        if (!appRegistry.TryGetApp(appId, out _))
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsJsonAsync(new { error = "App not found." }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var runtimeConfig = appConfigStore.GetConfig(appId);
        var prompt = request.Prompt;

        if (contentFilterEnabled)
        {
            var rules = contentRulesStore.GetRules(appId);
            var pre = contentFilter.FilterPre(appId, userId, prompt, rules);
            if (pre.IsBlocked)
            {
                await auditLog.AppendAsync(new AuditLogEntry
                {
                    AppId = appId,
                    UserId = userId,
                    Phase = "pre",
                    Reason = pre.AuditReason,
                    Timestamp = DateTimeOffset.UtcNow
                }, cancellationToken).ConfigureAwait(false);

                telemetry.RecordContentFiltered(appId, pre.AuditReason);
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response
                    .WriteAsJsonAsync(new { error = pre.BlockReason }, cancellationToken)
                    .ConfigureAwait(false);
                RecordGenerateTelemetry(telemetry, appId, userId, sw, prompt, null, StatusCodes.Status400BadRequest);
                return;
            }

            prompt = pre.ModifiedContent ?? prompt;
        }

        var adapter = adapterResolver.Resolve(runtimeConfig.LlmBackend);
        var generateRequest = request with { Prompt = prompt };
        var isStreaming = request.Stream ?? false;

        try
        {
            if (!isStreaming)
            {
                var response = await adapter
                    .GenerateAsync(generateRequest, cancellationToken)
                    .ConfigureAwait(false);

                var output = await ApplyPostFilterAsync(
                    contentFilterEnabled,
                    contentFilter,
                    contentRulesStore,
                    auditLog,
                    telemetry,
                    appId,
                    userId,
                    response.Response ?? string.Empty,
                    runtimeConfig.DefaultLanguage,
                    cancellationToken).ConfigureAwait(false);

                var finalResponse = response with { Response = output };

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response
                    .WriteAsJsonAsync(finalResponse, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                RecordGenerateTelemetry(telemetry, appId, userId, sw, prompt, output, StatusCodes.Status200OK);
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/x-ndjson";

            var generated = new System.Text.StringBuilder();
            await foreach (var chunk in adapter
                .GenerateStreamAsync(generateRequest, cancellationToken)
                .ConfigureAwait(false))
            {
                if (chunk.Response is { Length: > 0 } part)
                    generated.Append(part);

                var line = JsonSerializer.Serialize(chunk, JsonOptions);
                await httpContext.Response.WriteAsync(line + "\n", cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var streamedOutput = generated.ToString();
            if (contentFilterEnabled && streamedOutput.Length > 0)
            {
                streamedOutput = await ApplyPostFilterAsync(
                    true,
                    contentFilter,
                    contentRulesStore,
                    auditLog,
                    telemetry,
                    appId,
                    userId,
                    streamedOutput,
                    runtimeConfig.DefaultLanguage,
                    cancellationToken).ConfigureAwait(false);
            }

            RecordGenerateTelemetry(telemetry, appId, userId, sw, prompt, streamedOutput, StatusCodes.Status200OK);
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            RecordGenerateTelemetry(telemetry, appId, userId, sw, prompt, null, (int)ex.StatusCode.Value);
            httpContext.Response.StatusCode = (int)ex.StatusCode.Value;
            if (!string.IsNullOrEmpty(ex.Message))
            {
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> ApplyPostFilterAsync(
        bool enabled,
        IContentFilter contentFilter,
        IContentRulesStore contentRulesStore,
        IAuditLog auditLog,
        ITelemetryCollector telemetry,
        string appId,
        string userId,
        string content,
        string defaultLanguage,
        CancellationToken cancellationToken)
    {
        if (!enabled || string.IsNullOrEmpty(content))
            return content;

        var rules = contentRulesStore.GetRules(appId);
        var post = contentFilter.FilterPost(appId, userId, content, rules, defaultLanguage);
        if (!string.IsNullOrEmpty(post.AuditReason))
        {
            await auditLog.AppendAsync(new AuditLogEntry
            {
                AppId = appId,
                UserId = userId,
                Phase = "post",
                Reason = post.AuditReason,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
            telemetry.RecordContentFiltered(appId, post.AuditReason);
        }

        return post.ModifiedContent ?? content;
    }

    private static void RecordGenerateTelemetry(
        ITelemetryCollector telemetry,
        string appId,
        string userId,
        Stopwatch sw,
        string prompt,
        string? response,
        int statusCode)
    {
        sw.Stop();
        var promptTokens = string.IsNullOrEmpty(prompt) ? 0 : Math.Max(1, prompt.Length / 4);
        var completionTokens = string.IsNullOrEmpty(response) ? 0 : Math.Max(1, response.Length / 4);
        telemetry.RecordRequest(appId, userId, statusCode, sw.ElapsedMilliseconds, promptTokens, completionTokens, ragHit: false);
    }
}
