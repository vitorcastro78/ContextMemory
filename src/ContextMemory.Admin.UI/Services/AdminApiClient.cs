using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Admin.UI.Models;
using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Models;

namespace ContextMemory.Admin.UI.Services;

public sealed class AdminApiClient
{
    private readonly HttpClient _http;
    private readonly AdminSession _session;

    public AdminApiClient(HttpClient http, AdminSession session)
    {
        _http = http;
        _session = session;
    }

    public async Task<IReadOnlyList<AdminAppListItem>> GetAppsAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<AdminAppListItem>>("/admin/apps", cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public Task<AppStatsResponse?> GetAppStatsAsync(string appId, CancellationToken cancellationToken = default) =>
        GetAsync<AppStatsResponse>($"/admin/apps/{Uri.EscapeDataString(appId)}/stats", cancellationToken);

    public Task<AppCredentialsDto?> GetAppCredentialsAsync(string appId, CancellationToken cancellationToken = default) =>
        GetAsync<AppCredentialsDto>($"/admin/apps/{Uri.EscapeDataString(appId)}/credentials", cancellationToken);

    public async Task<AppCredentialsDto> RotateAppApiKeyAsync(string appId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/admin/apps/{Uri.EscapeDataString(appId)}/rotate-api-key");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<AppCredentialsDto>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from rotate-api-key.");
    }

    public async Task<IReadOnlyList<UserAdminSummaryDto>> GetUsersAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<UserAdminSummaryDto>>(
            $"/admin/apps/{Uri.EscapeDataString(appId)}/users",
            cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public Task<UserAdminDetailDto?> GetUserAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default) =>
        GetAsync<UserAdminDetailDto>(
            $"/admin/apps/{Uri.EscapeDataString(appId)}/users/{Uri.EscapeDataString(userId)}",
            cancellationToken);

    public async Task DeleteUserMemoryAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/users/{Uri.EscapeDataString(userId)}/memory");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<AppConfigFile?> PatchConfigAsync(
        string appId,
        AppConfigPatchRequest patch,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Patch,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/config");
        request.Content = JsonContent.Create(patch);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<AppConfigFile>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetAuditAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<AuditLogEntry>>(
            $"/admin/apps/{Uri.EscapeDataString(appId)}/audit",
            cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public async Task<RegisterAppResponse> RegisterAppAsync(
        RegisterAppRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "/apps/register");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<RegisterAppResponse>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from register endpoint.");
    }

    public async Task<IReadOnlyList<CompanyProfile>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<CompanyProfile>>("/companies", cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public Task<CompanyDetailResponse?> GetCompanyAsync(string companyId, CancellationToken cancellationToken = default) =>
        GetAsync<CompanyDetailResponse>($"/companies/{Uri.EscapeDataString(companyId)}", cancellationToken);

    public async Task<CompanyProfile> RegisterCompanyAsync(
        RegisterCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "/companies/register");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<CompanyProfile>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from companies/register.");
    }

    public async Task<IReadOnlyList<CompanyProcess>> GetCompanyProcessesAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<CompanyProcess>>(
            $"/companies/{Uri.EscapeDataString(companyId)}/processes",
            cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public async Task<IReadOnlyList<KnowledgeSource>> GetCompanySourcesAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<KnowledgeSource>>(
            $"/companies/{Uri.EscapeDataString(companyId)}/sources",
            cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public async Task<IReadOnlyList<string>> GetCompanyLinkedAppsAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<string>>(
            $"/companies/{Uri.EscapeDataString(companyId)}/apps",
            cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public async Task LinkCompanyAppAsync(string companyId, string appId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/companies/{Uri.EscapeDataString(companyId)}/apps/link");
        request.Content = JsonContent.Create(new LinkAppRequest { AppId = appId });
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task UnlinkCompanyAppAsync(string companyId, string appId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/companies/{Uri.EscapeDataString(companyId)}/apps/unlink");
        request.Content = JsonContent.Create(new LinkAppRequest { AppId = appId });
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<KnowledgeSource> AddCompanySourceAsync(
        string companyId,
        AddKnowledgeSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, $"/companies/{Uri.EscapeDataString(companyId)}/sources");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<KnowledgeSource>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from sources endpoint.");
    }

    public async Task<CompanySyncResult> SyncCompanyAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/companies/{Uri.EscapeDataString(companyId)}/sync");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<CompanySyncResult>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from sync endpoint.");
    }

    public async Task<CompanySkillsFile> GetCompanySkillsAsync(string companyId, CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<CompanySkillsFile>(
            $"/companies/{Uri.EscapeDataString(companyId)}/skills",
            cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty skills response.");
    }

    public string GetCompanySkillsYamlUrl(string companyId)
    {
        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/companies/{Uri.EscapeDataString(companyId)}/skills.yaml";
    }

    public string GetCompanySkillsMcpUrl(string companyId)
    {
        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/companies/{Uri.EscapeDataString(companyId)}/skills.mcp.json";
    }

    public async Task<WebhookSecretInfo> RotateCompanyWebhookSecretAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/companies/{Uri.EscapeDataString(companyId)}/webhook/rotate");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<WebhookSecretInfo>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty webhook rotate response.");
    }

    public string GetCompanyMcpUrl(string companyId)
    {
        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/companies/{Uri.EscapeDataString(companyId)}/mcp";
    }

    public string GetCompanyMcpSseUrl(string companyId)
    {
        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/companies/{Uri.EscapeDataString(companyId)}/mcp/sse";
    }

    public string GetCompanyMcpClientConfig(string companyId)
    {
        var sseUrl = GetCompanyMcpSseUrl(companyId);
        return $$"""
        {
          "mcpServers": {
            "company-brain": {
              "url": "{{sseUrl}}",
              "transport": "sse",
              "headers": { "Authorization": "Bearer <webhook-secret>" }
            }
          }
        }
        """;
    }

    public async Task<IReadOnlyList<CompanyProcess>> SearchCompanyProcessesAsync(
        string companyId,
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(
            HttpMethod.Post,
            $"/companies/{Uri.EscapeDataString(companyId)}/processes/search");
        httpRequest.Content = JsonContent.Create(new ProcessSearchRequest { Query = query, TopK = topK });
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ProcessSearchResponse>(cancellationToken)
            .ConfigureAwait(false);
        return result?.Processes ?? [];
    }

    public async Task<IReadOnlyList<CompanySyncHistoryEntry>> GetCompanySyncHistoryAsync(
        string companyId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<List<CompanySyncHistoryEntry>>(
            $"/companies/{Uri.EscapeDataString(companyId)}/sync/history?limit={limit}",
            cancellationToken).ConfigureAwait(false);
        return result ?? [];
    }

    public async Task<CompanyAlertConfig> GetCompanyAlertConfigAsync(string companyId, CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<CompanyAlertConfig>(
            $"/companies/{Uri.EscapeDataString(companyId)}/alerts/config",
            cancellationToken).ConfigureAwait(false);
        return result ?? new CompanyAlertConfig { CompanyId = companyId };
    }

    public async Task<CompanyAlertConfig> UpdateCompanyAlertConfigAsync(
        string companyId,
        UpdateCompanyAlertConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Put, $"/companies/{Uri.EscapeDataString(companyId)}/alerts/config");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<CompanyAlertConfig>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? new CompanyAlertConfig { CompanyId = companyId };
    }

    public async Task<IReadOnlyList<CompanyProcess>> GetPendingProcessesAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<List<CompanyProcess>>(
            $"/companies/{Uri.EscapeDataString(companyId)}/processes/pending",
            cancellationToken).ConfigureAwait(false);
        return result ?? [];
    }

    public async Task<ProcessApprovalResult> ApproveProcessAsync(
        string companyId,
        string processId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(
            HttpMethod.Post,
            $"/companies/{Uri.EscapeDataString(companyId)}/processes/{Uri.EscapeDataString(processId)}/approve");
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ProcessApprovalResult>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty approve response.");
    }

    public async Task<BulkApprovalResult> ApproveAllPendingAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(
            HttpMethod.Post,
            $"/companies/{Uri.EscapeDataString(companyId)}/processes/approve-all");
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<BulkApprovalResult>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? new BulkApprovalResult { CompanyId = companyId };
    }

    public async Task<CompanyBrainMetricsSnapshot> GetCompanyMetricsAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<CompanyBrainMetricsSnapshot>(
            $"/companies/{Uri.EscapeDataString(companyId)}/metrics",
            cancellationToken).ConfigureAwait(false);
        return result ?? new CompanyBrainMetricsSnapshot { CompanyId = companyId };
    }

    public async Task<CompanyBrainGlobalDashboard> GetGlobalDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<CompanyBrainGlobalDashboard>(
            "/companies/dashboard",
            cancellationToken).ConfigureAwait(false);
        return result ?? new CompanyBrainGlobalDashboard();
    }

    public Task<CompanySyncHistoryEntry?> GetSyncHistoryEntryAsync(
        string companyId,
        string entryId,
        CancellationToken cancellationToken = default) =>
        GetAsync<CompanySyncHistoryEntry>(
            $"/companies/{Uri.EscapeDataString(companyId)}/sync/history/{Uri.EscapeDataString(entryId)}",
            cancellationToken);

    public async Task<SharePointOAuthStartResult> StartSharePointOAuthAsync(
        string companyId,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<SharePointOAuthStartResult>(
            $"/companies/{Uri.EscapeDataString(companyId)}/sources/{Uri.EscapeDataString(sourceId)}/sharepoint/oauth/start",
            cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty SharePoint OAuth start response.");
    }

    public Task<SharePointOAuthStatus?> GetSharePointOAuthStatusAsync(
        string companyId,
        string sourceId,
        CancellationToken cancellationToken = default) =>
        GetAsync<SharePointOAuthStatus>(
            $"/companies/{Uri.EscapeDataString(companyId)}/sources/{Uri.EscapeDataString(sourceId)}/sharepoint/oauth/status",
            cancellationToken);

    public async Task<CompanyImportResult> ImportCompaniesFromDiskAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "/companies/import-from-disk");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<CompanyImportResult>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? new CompanyImportResult();
    }

    public string GetCompanyWebhookUrl(string companyId)
    {
        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/companies/{Uri.EscapeDataString(companyId)}/webhook";
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL and master key in Settings.");

        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Settings.MasterKey);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(body)
            ? response.ReasonPhrase ?? "Request failed."
            : body;
        throw new AdminApiException((int)response.StatusCode, message);
    }
}
