using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;

namespace ContextMemory.Api.Endpoints;

public static class CompanyBrainEndpoint
{
    public static void MapCompanyBrainEndpoints(this WebApplication app)
    {
        app.MapPost("/companies/register", RegisterCompany).DisableAntiforgery();
        app.MapGet("/companies", ListCompanies);
        app.MapGet("/companies/dashboard", GetGlobalDashboard);
        app.MapGet("/companies/{companyId}", GetCompany);
        app.MapPost("/companies/{companyId}/processes", UpsertProcess).DisableAntiforgery();
        app.MapPost("/companies/{companyId}/processes/search", SearchProcesses).DisableAntiforgery();
        app.MapGet("/companies/{companyId}/processes/pending", ListPendingProcesses);
        app.MapPost("/companies/{companyId}/processes/{processId}/approve", ApproveProcess).DisableAntiforgery();
        app.MapPost("/companies/{companyId}/processes/approve-all", ApproveAllPending).DisableAntiforgery();
        app.MapGet("/companies/{companyId}/metrics", GetCompanyMetrics);
        app.MapPost("/companies/{companyId}/sources", AddSource).DisableAntiforgery();
        app.MapGet("/companies/{companyId}/sources", ListSources);
        app.MapPost("/companies/{companyId}/apps/link", LinkApp).DisableAntiforgery();
        app.MapPost("/companies/{companyId}/apps/unlink", UnlinkApp).DisableAntiforgery();
        app.MapGet("/companies/{companyId}/apps", ListLinkedApps);
        app.MapPost("/companies/{companyId}/sync", SyncCompany).DisableAntiforgery();
        app.MapGet("/companies/{companyId}/sync/history", GetSyncHistory);
        app.MapGet("/companies/{companyId}/sync/history/{entryId}", GetSyncHistoryEntry);
        app.MapGet("/companies/{companyId}/sources/{sourceId}/sharepoint/oauth/start", StartSharePointOAuth);
        app.MapGet("/companies/{companyId}/sources/{sourceId}/sharepoint/oauth/status", GetSharePointOAuthStatus);
        app.MapGet("/companies/sharepoint/oauth/callback", SharePointOAuthCallback);
        app.MapGet("/companies/{companyId}/alerts/config", GetAlertConfig);
        app.MapPut("/companies/{companyId}/alerts/config", UpdateAlertConfig).DisableAntiforgery();
        app.MapPost("/companies/{companyId}/ingest", IngestKnowledge).DisableAntiforgery();
        app.MapGet("/companies/{companyId}/skills", GetSkills);
        app.MapGet("/companies/{companyId}/skills.yaml", GetSkillsYaml);
        app.MapGet("/companies/{companyId}/skills.mcp.json", GetSkillsMcp);
        app.MapPost("/companies/{companyId}/webhook/rotate", RotateWebhookSecret).DisableAntiforgery();
        app.MapPost("/companies/{companyId}/webhook", CompanyWebhook).DisableAntiforgery();
        app.MapPost("/companies/{companyId}/mcp", CompanyMcp).DisableAntiforgery();
        app.MapGet("/companies/{companyId}/mcp/sse", CompanyMcpSse);
        app.MapPost("/companies/{companyId}/mcp/messages", CompanyMcpMessage).DisableAntiforgery();
        app.MapPost("/companies/import-from-disk", ImportFromDisk).DisableAntiforgery();
    }

    private static IResult RegisterCompany(RegisterCompanyRequest request, ICompanyBrainService service)
    {
        try
        {
            var company = service.RegisterCompany(request);
            return Results.Json(company, statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static IResult ListCompanies(ICompanyBrainStore store) =>
        Results.Json(store.ListCompanies());

    private static IResult GetGlobalDashboard(ICompanyBrainService service) =>
        Results.Json(service.GetGlobalDashboard());

    private static IResult GetCompany(string companyId, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out var company) || company is null)
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(new CompanyDetailResponse
        {
            Company = company,
            LinkedApps = store.ListLinkedApps(companyId),
            ProcessCount = store.ListProcesses(companyId).Count,
            SourceCount = store.ListKnowledgeSources(companyId).Count
        });
    }

    private static IResult UpsertProcess(string companyId, UpsertProcessRequest request, ICompanyBrainService service)
    {
        try
        {
            var process = service.UpsertProcess(companyId, request);
            return Results.Json(process, statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static IResult ListProcesses(string companyId, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(store.ListProcesses(companyId));
    }

    private static IResult SearchProcesses(
        string companyId,
        ProcessSearchRequest request,
        ICompanyBrainService service,
        ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        if (string.IsNullOrWhiteSpace(request.Query))
            return Results.BadRequest(new { error = "Query is required." });

        var topK = Math.Clamp(request.TopK <= 0 ? 5 : request.TopK, 1, 20);
        var processes = service.SearchProcesses(companyId, request.Query.Trim(), topK);
        return Results.Json(new ProcessSearchResponse
        {
            CompanyId = companyId,
            Query = request.Query.Trim(),
            Processes = processes
        });
    }

    private static IResult ListPendingProcesses(string companyId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(service.ListPendingProcesses(companyId));
    }

    private static IResult ApproveProcess(string companyId, string processId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        try
        {
            return Results.Json(service.ApproveProcess(companyId, processId));
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static IResult ApproveAllPending(string companyId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(service.ApproveAllPending(companyId));
    }

    private static IResult GetCompanyMetrics(string companyId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(service.GetMetrics(companyId));
    }

    private static IResult AddSource(string companyId, AddKnowledgeSourceRequest request, ICompanyBrainService service)
    {
        try
        {
            var source = service.AddKnowledgeSource(companyId, request);
            return Results.Json(source, statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static IResult ListSources(string companyId, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(store.ListKnowledgeSources(companyId));
    }

    private static IResult LinkApp(string companyId, LinkAppRequest request, ICompanyBrainService service)
    {
        try
        {
            service.LinkApp(companyId, request.AppId);
            return Results.Ok(new { status = "linked", companyId, appId = request.AppId });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static IResult UnlinkApp(string companyId, LinkAppRequest request, ICompanyBrainService service)
    {
        try
        {
            service.UnlinkApp(companyId, request.AppId);
            return Results.Ok(new { status = "unlinked", companyId, appId = request.AppId });
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static IResult ListLinkedApps(string companyId, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(store.ListLinkedApps(companyId));
    }

    private static async Task<IResult> SyncCompany(string companyId, ICompanyBrainService service, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.SyncAsync(companyId, cancellationToken).ConfigureAwait(false);
            return Results.Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static IResult GetSyncHistory(string companyId, ICompanyBrainService service, ICompanyBrainStore store, int? limit)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        var entries = service.GetSyncHistory(companyId, limit ?? 20);
        return Results.Json(entries);
    }

    private static IResult GetSyncHistoryEntry(
        string companyId,
        string entryId,
        ICompanyBrainService service,
        ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        var entry = service.GetSyncHistoryEntry(companyId, entryId);
        return entry is null
            ? Results.NotFound(new { error = $"Sync history entry '{entryId}' not found." })
            : Results.Json(entry);
    }

    private static IResult StartSharePointOAuth(
        string companyId,
        string sourceId,
        ICompanyBrainService service,
        ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        try
        {
            return Results.Json(service.StartSharePointOAuth(companyId, sourceId));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IResult GetSharePointOAuthStatus(
        string companyId,
        string sourceId,
        ICompanyBrainService service,
        ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        try
        {
            return Results.Json(service.GetSharePointOAuthStatus(companyId, sourceId));
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SharePointOAuthCallback(
        string? code,
        string? state,
        string? error,
        ICompanyBrainService service)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return Results.BadRequest(new { error });

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Results.BadRequest(new { error = "Missing code or state." });

        try
        {
            var redirectUrl = await service.CompleteSharePointOAuthAsync(code, state).ConfigureAwait(false);
            return Results.Redirect(redirectUrl);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IResult GetAlertConfig(string companyId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(service.GetAlertConfig(companyId));
    }

    private static IResult UpdateAlertConfig(
        string companyId,
        UpdateCompanyAlertConfigRequest request,
        ICompanyBrainService service,
        ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        return Results.Json(service.UpdateAlertConfig(companyId, request));
    }

    private static IResult GetSkills(string companyId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        var skills = service.CompileSkills(companyId);
        return Results.Json(skills);
    }

    private static IResult IngestKnowledge(
        string companyId,
        IngestKnowledgeRequest request,
        ICompanyBrainService service)
    {
        try
        {
            var result = service.IngestKnowledge(companyId, request);
            return Results.Json(result, statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static IResult GetSkillsYaml(string companyId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        var skills = service.CompileSkills(companyId);
        return Results.Text(SkillsExporter.ToYaml(skills), "text/yaml; charset=utf-8");
    }

    private static IResult GetSkillsMcp(string companyId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        var skills = service.CompileSkills(companyId);
        return Results.Json(SkillsExporter.ToMcp(skills));
    }

    private static IResult RotateWebhookSecret(string companyId, ICompanyBrainService service, ICompanyBrainStore store)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        try
        {
            var info = service.RotateWebhookSecret(companyId);
            return Results.Json(info);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CompanyWebhook(
        string companyId,
        HttpRequest request,
        ICompanyBrainService service,
        ICompanyBrainStore store,
        CompanyBrainTelemetry telemetry)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        request.Body.Position = 0;

        if (!request.Headers.TryGetValue(CompanyWebhookAuth.SignatureHeader, out var signature))
        {
            return Results.Json(new { error = "Missing X-Company-Signature header." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!service.ValidateWebhookSignature(companyId, body, signature.ToString()))
            return Results.Json(new { error = "Invalid webhook signature." }, statusCode: StatusCodes.Status401Unauthorized);

        CompanyWebhookRequest? payload;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<CompanyWebhookRequest>(body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid JSON body." });
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Content))
            return Results.BadRequest(new { error = "Content is required." });

        try
        {
            var result = service.IngestKnowledge(companyId, new IngestKnowledgeRequest
            {
                Format = payload.Format,
                Content = payload.Content,
                SourceLabel = payload.SourceLabel ?? "webhook"
            });
            telemetry.RecordWebhook(companyId);
            return Results.Json(result, statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CompanyMcp(
        string companyId,
        HttpRequest request,
        ICompanyBrainService service,
        ICompanyBrainStore store,
        IConfiguration configuration)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        if (!TryAuthorizeMcp(request, configuration, companyId, service))
            return Results.Json(new { error = "Invalid authorization for MCP endpoint." }, statusCode: StatusCodes.Status401Unauthorized);

        JsonRpcRequest? rpc;
        try
        {
            rpc = await request.ReadFromJsonAsync<JsonRpcRequest>().ConfigureAwait(false);
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid JSON-RPC request." });
        }

        if (rpc is null || string.IsNullOrWhiteSpace(rpc.Method))
            return Results.BadRequest(new { error = "Invalid JSON-RPC request." });

        try
        {
            var response = service.HandleMcpRequest(companyId, rpc);
            return Results.Json(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static bool TryAuthorizeMcp(HttpRequest request, IConfiguration configuration, string companyId, ICompanyBrainService service)
    {
        var header = request.Headers.Authorization.ToString();
        string? token = null;
        if (!string.IsNullOrWhiteSpace(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = header["Bearer ".Length..].Trim();

        token ??= request.Query["token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var masterKey = configuration.GetValue<string>("ContextMemory:MasterKey") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(masterKey) && string.Equals(token, masterKey, StringComparison.Ordinal))
            return true;

        return service.ValidateCompanyToken(companyId, token);
    }

    private static IResult ImportFromDisk(ICompanyBrainService service) =>
        Results.Json(service.ImportFromDisk());

    private static async Task CompanyMcpSse(
        string companyId,
        HttpRequest request,
        HttpResponse response,
        ICompanyBrainService service,
        ICompanyBrainStore store,
        IConfiguration configuration,
        McpSseHub hub,
        CancellationToken cancellationToken)
    {
        if (!store.TryGetCompany(companyId, out _))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsJsonAsync(new { error = $"Company '{companyId}' not found." }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!TryAuthorizeMcp(request, configuration, companyId, service))
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            await response.WriteAsJsonAsync(new { error = "Invalid authorization for MCP SSE." }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.ContentType = "text/event-stream";

        var sessionId = hub.CreateSession(companyId);
        var messageEndpoint = $"/companies/{companyId}/mcp/messages?sessionId={sessionId}";
        await response.WriteAsync(McpSseHub.SerializeSseEvent("endpoint", messageEndpoint), cancellationToken)
            .ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (!hub.TryGetSession(sessionId, out var session) || session is null)
            return;

        try
        {
            await foreach (var message in session.Channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var payload = McpSseHub.SerializeMessage(message);
                await response.WriteAsync(McpSseHub.SerializeSseEvent("message", payload), cancellationToken)
                    .ConfigureAwait(false);
                await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            hub.RemoveSession(sessionId);
        }
    }

    private static async Task<IResult> CompanyMcpMessage(
        string companyId,
        string? sessionId,
        HttpRequest request,
        ICompanyBrainService service,
        ICompanyBrainStore store,
        IConfiguration configuration,
        McpSseHub hub)
    {
        if (!store.TryGetCompany(companyId, out _))
            return Results.NotFound(new { error = $"Company '{companyId}' not found." });

        if (!TryAuthorizeMcp(request, configuration, companyId, service))
            return Results.Json(new { error = "Invalid authorization for MCP endpoint." }, statusCode: StatusCodes.Status401Unauthorized);

        JsonRpcRequest? rpc;
        try
        {
            rpc = await request.ReadFromJsonAsync<JsonRpcRequest>().ConfigureAwait(false);
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid JSON-RPC request." });
        }

        if (rpc is null || string.IsNullOrWhiteSpace(rpc.Method))
            return Results.BadRequest(new { error = "Invalid JSON-RPC request." });

        JsonRpcResponse response;
        try
        {
            response = service.HandleMcpRequest(companyId, rpc);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            if (!hub.TryGetSession(sessionId, out var session)
                || session is null
                || !string.Equals(session.CompanyId, companyId, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { error = "Invalid MCP SSE session." });
            }

            hub.TryPushResponse(sessionId, response);
            return Results.Accepted();
        }

        return Results.Json(response);
    }
}
